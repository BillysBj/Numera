using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class UgRuecklageTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(ChartVariant.Skr03, 1, "0860", "0846")]
    [InlineData(ChartVariant.Skr04, 4, "2970", "2929")]
    public async Task Booking_allocates_equity_once_without_changing_profit(
        ChartVariant chart, int startMonth, string debitNumber, string creditNumber)
    {
        var tenant = await SetupAsync(chart, startMonth: startMonth);
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        var report = await PreviewAsync(db, 4000m);
        Assert.Equal(new UgRuecklageReport(2026, 20000m, 4000m, 16000m, 4000m, null), report);
        var before = await new AbschlussCalculator(db).ComputeBilanzAsync(2026, default);
        var booking = Assert.IsType<Ok<UgRuecklageBuchung>>(await BookAsync(db, tenant)).Value!;
        Assert.Equal(4000m, booking.Betrag);
        Assert.Equal(4000m, booking.VerlustvortragVorjahr);
        Assert.Equal(2026, booking.Jahr);
        AssertStatus(await BookAsync(db, tenant), 409);
        Assert.Single(await db.Set<UgRuecklageBuchung>().ToListAsync());
        var entry = await db.Set<JournalEntry>().Include(entry => entry.Postings)
            .SingleAsync(entry => entry.Id == booking.JournalEntryId);
        Assert.Equal(new DateOnly(2026, startMonth, 1).AddMonths(12).AddDays(-1), entry.EntryDate);
        Assert.Equal("ug-ruecklage:2026", entry.SourceRef);
        Assert.Equal("Gesetzliche Rücklage §5a GmbHG 2026", entry.Description);
        Assert.Equal(LedgerSourceType.Manual, entry.SourceType);
        Assert.Equal(2, entry.Postings.Count);
        var accounts = await db.Set<Account>().ToDictionaryAsync(account => account.Id);
        Assert.Equal(debitNumber, accounts[Assert.Single(entry.Postings, p => p.Direction == PostingDirection.Debit).AccountId].Number);
        Assert.Equal(creditNumber, accounts[Assert.Single(entry.Postings, p => p.Direction == PostingDirection.Credit).AccountId].Number);
        Assert.All(entry.Postings, posting =>
        {
            Assert.Equal(AccountType.Equity, accounts[posting.AccountId].Type);
            Assert.Equal(4000m, posting.Amount);
            Assert.Null(posting.TaxCategory);
        });
        Assert.Contains(await db.Set<AuditEvent>().ToListAsync(), evt =>
            evt.Action == "ledger.ug_ruecklage_booked" && evt.EntityId == booking.Id);

        var after = await new AbschlussCalculator(db).ComputeBilanzAsync(2026, default);
        // Both reserve legs appear in the aggregated Equity group and net to zero.
        Assert.Contains(after.Passiva, line => line.Gruppe == BilanzPositionMap.Equity && line.Bezeichnung == BilanzPositionMap.Equity);
        Assert.Equal(before.SummePassiva, after.SummePassiva);
        Assert.Equal(0m, after.BilanzDifferenz);
        Assert.Equal(20000m, (await PreviewAsync(db, 4000m)).Jahresueberschuss);
        Assert.Equal(20000m, after.Passiva.Where(line => line.Gruppe == BilanzPositionMap.Equity).Sum(line => line.Betrag));

        db.Add(new UgRuecklageBuchung { TenantId = tenant.TenantId!.Value, Jahr = 2026, Betrag = 1m });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Concurrent_booking_is_serialized_and_posts_exactly_once()
    {
        var tenant = await SetupAsync();
        async Task<IResult> Book()
        {
            await using var db = fixture.CreateAppContext(tenant.TenantId);
            return await BookAsync(db, tenant);
        }

        var results = await Task.WhenAll(Book(), Book());
        Assert.Single(results, result => result is Ok<UgRuecklageBuchung>);
        Assert.Single(results, result => ((IStatusCodeHttpResult)result).StatusCode == 409);
        await using var read = fixture.CreateAppContext(tenant.TenantId);
        Assert.Single(await read.Set<UgRuecklageBuchung>().ToListAsync());
        Assert.Single(await read.Set<JournalEntry>().Where(entry => entry.SourceRef == "ug-ruecklage:2026").ToListAsync());
    }

    [Theory]
    [InlineData(Gewinnermittlungsart.Euer, true, 20000)]
    [InlineData(Gewinnermittlungsart.Bilanz, false, 20000)]
    [InlineData(Gewinnermittlungsart.Bilanz, true, -1000)]
    [InlineData(Gewinnermittlungsart.Bilanz, true, 4000)]
    [InlineData(null, true, 20000)]
    public async Task Gated_or_nonpositive_reserves_return_conflict_without_booking(
        Gewinnermittlungsart? method, bool active, decimal profit)
    {
        var tenant = await SetupAsync(method: method, active: active, profit: profit);
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        var report = await PreviewAsync(db, 4000m);
        Assert.Equal(0m, report.Ruecklage);
        if (method != Gewinnermittlungsart.Bilanz)
        {
            Assert.Equal((await new AbschlussCalculator(db).ComputeGuvAsync(2026, default)).Hinweis, report.Hinweis);
            Assert.Equal(0m, report.Jahresueberschuss);
            Assert.Equal(0m, report.VerlustvortragVorjahr);
            Assert.Equal(0m, report.MassgeblicherBetrag);
        }
        else if (!active)
        {
            Assert.Contains("deaktiviert", report.Hinweis);
        }

        AssertStatus(await BookAsync(db, tenant), 409);
        Assert.Empty(await db.Set<UgRuecklageBuchung>().ToListAsync());
        Assert.Empty(await db.Set<JournalEntry>().Where(entry => entry.SourceRef == "ug-ruecklage:2026").ToListAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Locked_closing_period_returns_conflict(int startMonth)
    {
        var tenant = await SetupAsync(startMonth: startMonth);
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        var date = new DateOnly(2026, startMonth, 1).AddMonths(12).AddDays(-1);
        db.Add(new FiscalPeriod { TenantId = tenant.TenantId!.Value, Year = date.Year, Month = date.Month, Status = FiscalPeriodStatus.Locked });
        await db.SaveChangesAsync();
        AssertStatus(await BookAsync(db, tenant), 409);
        Assert.Empty(await db.Set<UgRuecklageBuchung>().ToListAsync());
    }

    [Fact]
    public async Task Rls_isolates_bookings_rejects_forgery_and_grants_are_append_only()
    {
        var tenant = await SetupAsync();
        var other = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        var booking = Assert.IsType<Ok<UgRuecklageBuchung>>(await BookAsync(db, tenant)).Value!;
        await using var foreign = fixture.CreateAppContext(other.TenantId);
        Assert.Empty(await foreign.Set<UgRuecklageBuchung>().IgnoreQueryFilters().ToListAsync());
        Assert.IsType<Ok<UgRuecklageBuchung>>(await BookAsync(foreign, other));
        Assert.Equal(other.TenantId, (await foreign.Set<UgRuecklageBuchung>().IgnoreQueryFilters().SingleAsync()).TenantId);
        foreign.Add(new UgRuecklageBuchung { TenantId = tenant.TenantId!.Value, Jahr = 2027, Betrag = 1m });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        var updateError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ug_ruecklage_buchungen SET betrag = 1 WHERE id = {booking.Id}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, updateError.SqlState);
        var deleteError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ug_ruecklage_buchungen WHERE id = {booking.Id}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, deleteError.SqlState);
    }

    [Fact]
    public async Task Owner_toggle_persists_false_and_can_be_reenabled()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        Assert.True((await db.Set<LedgerSettings>().SingleAsync()).UgRuecklagepflichtAktiv);
        var response = Assert.IsType<Ok<LedgerSettingsDto>>(await LedgerSetupEndpoints.UpdateSettingsAsync(
            new(false), db, Audit(db, tenant), default)).Value!;
        Assert.False(response.UgRuecklagepflichtAktiv);
        db.ChangeTracker.Clear();
        Assert.False((await db.Set<LedgerSettings>().SingleAsync()).UgRuecklagepflichtAktiv);
        AssertStatus(await BookAsync(db, tenant), 409);
        await LedgerSetupEndpoints.UpdateSettingsAsync(new(true), db, Audit(db, tenant), default);
        Assert.IsType<Ok<UgRuecklageBuchung>>(await BookAsync(db, tenant));
    }

    private async Task<TenantContext> SetupAsync(ChartVariant chart = ChartVariant.Skr03,
        Gewinnermittlungsart? method = Gewinnermittlungsart.Bilanz, bool active = true, decimal profit = 20000m, int startMonth = 1)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        if (method is { } value)
        {
            db.Add(new LedgerSettings
            {
                TenantId = tenant.TenantId!.Value, ChartVariant = chart, Gewinnermittlungsart = value,
                UgRuecklagepflichtAktiv = active, FiscalYearStartMonth = startMonth,
            });
        }

        await new ChartSeeder(db).SeedAsync(chart, tenant.TenantId!.Value);
        var bankNumber = SkrMapping.StandardAccount(chart, StandardAccountKind.Bank);
        var bank = await db.Set<Account>().SingleAsync(account => account.Number == bankNumber);
        var revenueNumber = chart == ChartVariant.Skr03 ? "8400" : "4400";
        var revenue = await db.Set<Account>().SingleAsync(account => account.Number == revenueNumber);
        var entry = new JournalEntry
        {
            TenantId = tenant.TenantId.Value, EntryDate = new DateOnly(2026, startMonth, 1), SourceRef = "profit", Description = "Reserve test profit",
        };
        entry.Postings.Add(new Posting
        {
            TenantId = tenant.TenantId.Value, AccountId = bank.Id, JournalEntryId = entry.Id,
            Amount = Math.Abs(profit), Direction = profit >= 0m ? PostingDirection.Debit : PostingDirection.Credit,
        });
        entry.Postings.Add(new Posting
        {
            TenantId = tenant.TenantId.Value, AccountId = revenue.Id, JournalEntryId = entry.Id,
            Amount = Math.Abs(profit), Direction = profit >= 0m ? PostingDirection.Credit : PostingDirection.Debit,
        });
        db.Add(entry);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<UgRuecklageReport> PreviewAsync(NumeraDbContext db, decimal? loss = null) =>
        Assert.IsType<Ok<UgRuecklageReport>>(await ReportEndpoints.GetUgRuecklageAsync(2026, loss,
            db, new AbschlussCalculator(db), new UgRuecklageCalculator(), default)).Value!;
    private static Task<IResult> BookAsync(NumeraDbContext db, TenantContext tenant) =>
        ReportEndpoints.BookUgRuecklageAsync(2026, new(4000m), db, new AbschlussCalculator(db),
            new UgRuecklageCalculator(), new PostingEngine(db), Audit(db, tenant), tenant, default);
    private static AuditWriter Audit(NumeraDbContext db, TenantContext tenant) => new(db, tenant, new Actor());
    private static void AssertStatus(IResult result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    private sealed class Actor : ICurrentUser { public Guid? UserId { get; } = Guid.CreateVersion7(); }
}
