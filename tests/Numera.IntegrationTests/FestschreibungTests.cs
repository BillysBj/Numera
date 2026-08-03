using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of gapless Festschreibung and DB-enforced period locks.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FestschreibungTests(PostgresFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 8;

    [Fact]
    public async Task Lock_stamps_gapless_numbers_in_entry_date_and_id_order()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var laterId = await CreateJournalEntryAsync(tenant, new DateOnly(Year, Month, 20), "later");
        var earlierId = await CreateJournalEntryAsync(tenant, new DateOnly(Year, Month, 2), "earlier");

        var result = await LockAsync(tenant);

        Assert.Equal(PeriodLockStatus.Success, result.Status);
        Assert.Equal(2, result.EntriesLocked);
        await using var read = fixture.CreateAppContext(tenant);
        var entries = await read.Set<JournalEntry>()
            .AsNoTracking()
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Id)
            .ToListAsync();
        var period = await read.Set<FiscalPeriod>().AsNoTracking().SingleAsync();

        Assert.Equal(earlierId, entries[0].Id);
        Assert.Equal("2026-000001", entries[0].JournalNumber);
        Assert.Equal(laterId, entries[1].Id);
        Assert.Equal("2026-000002", entries[1].JournalNumber);
        Assert.All(entries, entry =>
        {
            Assert.NotNull(entry.FestgeschriebenAt);
            Assert.Equal(period.Id, entry.PeriodId);
        });
        Assert.Equal(FiscalPeriodStatus.Locked, period.Status);
        Assert.NotNull(period.LockedAt);
    }

    [Fact]
    public async Task Booking_into_locked_period_fails_and_rolls_back_payment_settlement()
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SetupLedgerAsync(tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            new DateOnly(Year, Month, 1));
        await using (var finalize = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(finalize, documentId);
        }

        Guid openItemId;
        decimal originalAmount;
        await using (var read = fixture.CreateAppContext(tenant))
        {
            var openItem = await read.Set<OpenItem>().AsNoTracking()
                .SingleAsync(item => item.DocumentId == documentId);
            openItemId = openItem.Id;
            originalAmount = openItem.OriginalAmount;
        }

        Assert.Equal(PeriodLockStatus.Success, (await LockAsync(tenant)).Status);

        await using (var paymentDb = fixture.CreateAppContext(tenant))
        {
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                new PaymentService(paymentDb, tenantContext, new NoOpAuditWriter())
                    .RecordAsync(new RecordPaymentRequest(
                        originalAmount,
                        new DateOnly(Year, Month, 15),
                        PaymentMethod.BankTransfer,
                        "LOCKED-PERIOD",
                        [new PaymentAllocationInput(openItemId, originalAmount)])));
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Contains("festgeschrieben (locked)", postgres.MessageText);
        }

        await using var verify = fixture.CreateAppContext(tenant);
        Assert.Equal(0, await verify.Set<Payment>().CountAsync());
        Assert.Equal(0, await verify.Set<PaymentAllocation>().CountAsync());
        Assert.Equal(
            0,
            await verify.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
        var unchanged = await verify.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.Id == openItemId);
        Assert.Equal(originalAmount, unchanged.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, unchanged.Status);
    }

    [Fact]
    public async Task Locking_an_already_locked_period_returns_conflict()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        await CreateJournalEntryAsync(tenant, new DateOnly(Year, Month, 2), "entry");
        Assert.Equal(PeriodLockStatus.Success, (await LockAsync(tenant)).Status);

        var second = await LockAsync(tenant);

        Assert.Equal(PeriodLockStatus.Conflict, second.Status);
        Assert.Contains("already locked", second.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Festgeschriebene_entry_rejects_business_field_edits()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var entryId = await CreateJournalEntryAsync(
            tenant, new DateOnly(Year, Month, 2), "immutable-entry");
        Assert.Equal(PeriodLockStatus.Success, (await LockAsync(tenant)).Status);

        await using var db = fixture.CreateAppContext(tenant);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE journal_entries SET description = 'changed' WHERE id = {entryId}"));
        Assert.Contains("festgeschrieben and immutable", exception.MessageText);
    }

    private async Task<Guid> CreateJournalEntryAsync(Guid tenant, DateOnly entryDate, string sourceRef)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var input = new PaymentPostingInput(
            BankAccount: null,
            entryDate,
            [new PaymentPostingAllocation(null, 10m)],
            IsReversal: false);
        var entry = await new PostingEngine(db).PostAsync(
            new PaymentPostingSource(tenant, ChartVariant.Skr03, input, new AccountResolver(db)),
            new JournalEntry
            {
                TenantId = tenant,
                EntryDate = entryDate,
                SourceRef = sourceRef,
                SourceType = LedgerSourceType.Manual,
                Description = sourceRef,
                PostingType = PostingType.Normal,
            },
            CancellationToken.None);
        return entry.Id;
    }

    private async Task<PeriodLockResult> LockAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        return await new FestschreibungService(db, tenantContext, new NoOpAuditWriter())
            .LockPeriodAsync(Year, Month, CancellationToken.None);
    }

    private async Task SetupLedgerAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Soll,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            tenantContext,
            CancellationToken.None);
        Assert.Equal(
            StatusCodes.Status201Created,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
