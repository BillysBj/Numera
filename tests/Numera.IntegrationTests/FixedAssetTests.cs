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
public sealed class FixedAssetTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(ChartVariant.Skr03, "4830")]
    [InlineData(ChartVariant.Skr04, "6220")]
    public async Task Create_run_balances_expected_accounts_and_repeat_is_noop(ChartVariant chart, string expense)
    {
        var tenant = await SetupAsync(chart);
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        var asset = Assert.IsType<Created<FixedAsset>>(await CreateAsync(db, tenant, Request())).Value!;
        Assert.Equal(expense, asset.AbschreibungskontoNumber);
        Assert.Equal(new AfaRunSummary(1, 0, 1000m), await RunAsync(db, tenant));
        Assert.Equal(new AfaRunSummary(0, 1, 0m), await RunAsync(db, tenant));
        var entry = await db.Set<JournalEntry>().Include(entry => entry.Postings).SingleAsync();
        Assert.Equal(asset.Id.ToString(), entry.SourceRef);
        Assert.Equal("AfA 2026 – Maschine", entry.Description);
        Assert.Equal(new DateOnly(2026, 12, 31), entry.EntryDate);
        Assert.Equal(LedgerSourceType.Manual, entry.SourceType);
        Assert.Equal(2, entry.Postings.Count);
        var accounts = await db.Set<Account>().ToDictionaryAsync(account => account.Id, account => account.Number);
        Assert.Equal(expense, accounts[Assert.Single(entry.Postings, p => p.Direction == PostingDirection.Debit).AccountId]);
        Assert.Equal("0400", accounts[Assert.Single(entry.Postings, p => p.Direction == PostingDirection.Credit).AccountId]);
        Assert.All(entry.Postings, posting => Assert.Equal(1000m, posting.Amount));
        var booking = await db.Set<AfaBuchung>().SingleAsync();
        Assert.Equal(entry.Id, booking.JournalEntryId);
        Assert.Contains(await db.Set<AuditEvent>().ToListAsync(), evt => evt.Action == "fixed_asset.afa_run");

        var report = await new AnlagenspiegelCalculator(db).ComputeAsync(2026, default);
        Assert.Equal(new AnlagenspiegelLine("Maschine", 0m, 6000m, 0m, 1000m, 5000m), Assert.Single(report.Anlagen));
        Assert.Equal(5000m, report.Summe.BuchwertJahresende);

        db.Add(new AfaBuchung { TenantId = tenant.TenantId!.Value, FixedAssetId = asset.Id, Jahr = 2026, Betrag = 1000m });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Concurrent_runs_book_exactly_once()
    {
        var tenant = await SetupAsync();
        await using (var db = fixture.CreateAppContext(tenant.TenantId))
        {
            await CreateAsync(db, tenant, Request());
        }

        async Task<AfaRunSummary> Run()
        {
            await using var db = fixture.CreateAppContext(tenant.TenantId);
            return await RunAsync(db, tenant);
        }

        var results = await Task.WhenAll(Run(), Run());
        Assert.Equal(1, results.Sum(result => result.BookedCount));
        await using var read = fixture.CreateAppContext(tenant.TenantId);
        Assert.Single(await read.Set<JournalEntry>().ToListAsync());
        Assert.Single(await read.Set<AfaBuchung>().ToListAsync());
    }

    [Fact]
    public async Task Locked_period_is_skipped_without_journal_or_depreciation_rows()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        await CreateAsync(db, tenant, Request());
        var period = new FiscalPeriod { TenantId = tenant.TenantId!.Value, Year = 2026, Month = 12, Status = FiscalPeriodStatus.Locked };
        db.Add(period);
        await db.SaveChangesAsync();
        Assert.Equal(new AfaRunSummary(0, 1, 0m), await RunAsync(db, tenant));
        Assert.Empty(await db.Set<JournalEntry>().ToListAsync());
        Assert.Empty(await db.Set<AfaBuchung>().ToListAsync());
    }

    [Fact]
    public async Task Disposal_year_posts_through_disposal_month_and_report_removes_remaining_value()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        await CreateAsync(db, tenant, Request() with { AbgangsDatum = new(2026, 9, 10), AbgangsArt = AssetDisposal.Verkauf });
        Assert.Equal(new AfaRunSummary(1, 0, 500m), await RunAsync(db, tenant));
        Assert.Equal(new DateOnly(2026, 9, 10), (await db.Set<JournalEntry>().SingleAsync()).EntryDate);
        var line = Assert.Single((await new AnlagenspiegelCalculator(db).ComputeAsync(2026, default)).Anlagen);
        Assert.Equal(5500m, line.Abgaenge);
        Assert.Equal(0m, line.BuchwertJahresende);
        Assert.Equal(new AfaRunSummary(0, 1, 0m), await RunAsync(db, tenant, 2027));
    }

    [Fact]
    public async Task Invalid_input_and_retroactive_changes_are_rejected()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        foreach (var request in new[]
        {
            Request() with { NutzungsdauerJahre = 0 }, Request() with { AnschaffungskostenNetto = -1m },
            Request() with { InbetriebnahmeDatum = new(2025, 1, 1) }, Request() with { AnlagekontoNumber = "4830" },
            Request() with { AbschreibungskontoNumber = "0400" }, Request() with { AbgangsArt = AssetDisposal.Verkauf },
        })
        {
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await CreateAsync(db, tenant, request)).StatusCode);
        }

        var asset = Assert.IsType<Created<FixedAsset>>(await CreateAsync(db, tenant, Request())).Value!;
        Assert.IsType<Ok<FixedAsset>>(await FixedAssetEndpoints.UpdateAsync(asset.Id, Request() with { Lieferant = "Supplier" },
            db, Audit(db, tenant), tenant, default));
        await RunAsync(db, tenant);
        var update = await FixedAssetEndpoints.UpdateAsync(asset.Id, Request() with { AnschaffungskostenNetto = 9000m },
            db, Audit(db, tenant), tenant, default);
        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(update).StatusCode);
    }

    [Fact]
    public async Task Rls_isolates_both_tables_and_rejects_forged_tenant_inserts()
    {
        var tenant = await SetupAsync();
        await using var owner = fixture.CreateAppContext(tenant.TenantId);
        var asset = Assert.IsType<Created<FixedAsset>>(await CreateAsync(owner, tenant, Request())).Value!;
        await RunAsync(owner, tenant);
        var other = new TenantContext();
        other.SetTenant(Guid.CreateVersion7());
        await using var foreign = fixture.CreateAppContext(other.TenantId);
        Assert.Empty(await foreign.Set<FixedAsset>().IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await foreign.Set<AfaBuchung>().IgnoreQueryFilters().ToListAsync());
        Assert.IsType<NotFound>(await FixedAssetEndpoints.UpdateAsync(asset.Id, Request(), foreign, Audit(foreign, other), other, default));
        foreign.Add(new FixedAsset
        {
            TenantId = tenant.TenantId!.Value, Bezeichnung = "Forged", AnlagekontoNumber = "0400", AbschreibungskontoNumber = "4830",
            NutzungsdauerJahre = 3, AnschaffungsDatum = new(2026, 1, 1), InbetriebnahmeDatum = new(2026, 1, 1),
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        foreign.ChangeTracker.Clear();
        foreign.Add(new AfaBuchung { TenantId = tenant.TenantId.Value, FixedAssetId = asset.Id, Jahr = 2027, Betrag = 1m });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    private async Task<TenantContext> SetupAsync(ChartVariant chart = ChartVariant.Skr03)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = fixture.CreateAppContext(tenant.TenantId);
        db.Add(new LedgerSettings { TenantId = tenant.TenantId!.Value, ChartVariant = chart });
        await new ChartSeeder(db).SeedAsync(chart, tenant.TenantId.Value);
        db.Add(new Account { TenantId = tenant.TenantId.Value, Number = "0400", Name = "Testanlage", Type = AccountType.Asset, ChartVariant = chart });
        await db.SaveChangesAsync();
        return tenant;
    }

    private static FixedAssetRequest Request() => new("Maschine", new(2026, 1, 1), new(2026, 7, 1), 6000m, "0400", 3);
    private static AuditWriter Audit(NumeraDbContext db, TenantContext tenant) => new(db, tenant, new Actor());
    private static Task<IResult> CreateAsync(NumeraDbContext db, TenantContext tenant, FixedAssetRequest request) =>
        FixedAssetEndpoints.CreateAsync(request, db, Audit(db, tenant), tenant, default);
    private static async Task<AfaRunSummary> RunAsync(NumeraDbContext db, TenantContext tenant, int year = 2026) =>
        Assert.IsType<Ok<AfaRunSummary>>(await FixedAssetEndpoints.RunAsync(year, db, new AfaCalculator(),
            new PostingEngine(db), Audit(db, tenant), tenant, default)).Value!;
    private sealed class Actor : ICurrentUser { public Guid? UserId { get; } = Guid.CreateVersion7(); }
}
