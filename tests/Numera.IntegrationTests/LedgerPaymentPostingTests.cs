using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof that payment settlement and ledger posting are atomic.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerPaymentPostingTests(PostgresFixture fixture)
{
    private static readonly DateOnly ValueDate = new(2026, 8, 4);

    [Fact]
    public async Task Full_payment_books_bank_against_receivable_and_closes_open_item()
    {
        var tenant = await SetupTenantAsync(withLedger: true);
        var invoice = await CreateInvoiceAsync(tenant, 100m);

        PaymentOperationResult result;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            result = await CreateService(db, tenant).RecordAsync(
                Request(invoice.OpenItemId, invoice.Amount));
        }

        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        var paymentId = Assert.IsType<Guid>(result.PaymentId);
        await using var read = fixture.CreateAppContext(tenant);
        var entry = await PaymentEntryAsync(read, paymentId);
        var accountNumbers = await AccountNumbersAsync(read);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.Id == invoice.OpenItemId);

        AssertBalanced(entry.Postings);
        Assert.Equal(PostingType.Normal, entry.PostingType);
        Assert.Equal(2, entry.Postings.Count);
        AssertLeg(entry.Postings, accountNumbers, "1200", PostingDirection.Debit, invoice.Amount);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Credit, invoice.Amount);
        Assert.Equal(0m, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Paid, openItem.Status);
    }

    [Fact]
    public async Task Multi_item_payment_has_one_bank_leg_and_one_receivable_leg_per_allocation()
    {
        var tenant = await SetupTenantAsync(withLedger: true);
        var first = await CreateInvoiceAsync(tenant, 100m);
        var second = await CreateInvoiceAsync(tenant, 200m);
        var request = new RecordPaymentRequest(
            first.Amount + second.Amount,
            ValueDate,
            PaymentMethod.BankTransfer,
            "MULTI",
            [
                new PaymentAllocationInput(first.OpenItemId, first.Amount),
                new PaymentAllocationInput(second.OpenItemId, second.Amount),
            ]);

        PaymentOperationResult result;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            result = await CreateService(db, tenant).RecordAsync(request);
        }

        var paymentId = Assert.IsType<Guid>(result.PaymentId);
        await using var read = fixture.CreateAppContext(tenant);
        var entry = await PaymentEntryAsync(read, paymentId);
        var accountNumbers = await AccountNumbersAsync(read);
        var openItems = await read.Set<OpenItem>()
            .AsNoTracking()
            .Where(item => item.Id == first.OpenItemId || item.Id == second.OpenItemId)
            .ToListAsync();

        AssertBalanced(entry.Postings);
        Assert.Equal(3, entry.Postings.Count);
        AssertLeg(
            entry.Postings,
            accountNumbers,
            "1200",
            PostingDirection.Debit,
            first.Amount + second.Amount);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Credit, first.Amount);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Credit, second.Amount);
        Assert.Equal(2, openItems.Count);
        Assert.All(openItems, item =>
        {
            Assert.Equal(0m, item.OpenAmount);
            Assert.Equal(OpenItemStatus.Paid, item.Status);
        });
    }

    [Fact]
    public async Task Invalid_allocation_leaves_no_payment_journal_entry()
    {
        var tenant = await SetupTenantAsync(withLedger: true);
        var invoice = await CreateInvoiceAsync(tenant, 100m);

        PaymentOperationResult result;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            result = await CreateService(db, tenant).RecordAsync(
                Request(invoice.OpenItemId, invoice.Amount + 0.01m));
        }

        Assert.Equal(PaymentOperationStatus.Invalid, result.Status);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(
            0,
            await read.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
        Assert.Equal(0, await read.Set<Payment>().CountAsync());
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.Id == invoice.OpenItemId);
        Assert.Equal(invoice.Amount, openItem.OpenAmount);
    }

    [Fact]
    public async Task Reversal_posts_storno_nets_to_zero_and_restores_open_item()
    {
        var tenant = await SetupTenantAsync(withLedger: true);
        var invoice = await CreateInvoiceAsync(tenant, 100m);

        Guid originalPaymentId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var recorded = await CreateService(db, tenant).RecordAsync(
                Request(invoice.OpenItemId, invoice.Amount));
            originalPaymentId = Assert.IsType<Guid>(recorded.PaymentId);
        }

        Guid reversalPaymentId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var reversed = await CreateService(db, tenant).ReverseAsync(originalPaymentId);
            Assert.Equal(PaymentOperationStatus.Success, reversed.Status);
            reversalPaymentId = Assert.IsType<Guid>(reversed.PaymentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var entries = await read.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .Where(entry => entry.SourceType == LedgerSourceType.Payment)
            .ToListAsync();
        var original = Assert.Single(entries, entry => entry.SourceRef == originalPaymentId.ToString());
        var reversal = Assert.Single(entries, entry => entry.SourceRef == reversalPaymentId.ToString());
        var accountNumbers = await AccountNumbersAsync(read);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.Id == invoice.OpenItemId);

        Assert.Equal(PostingType.Storno, reversal.PostingType);
        Assert.Equal(original.Id, reversal.ReversesEntryId);
        AssertBalanced(reversal.Postings);
        AssertLeg(reversal.Postings, accountNumbers, "1200", PostingDirection.Credit, invoice.Amount);
        AssertLeg(reversal.Postings, accountNumbers, "1400", PostingDirection.Debit, invoice.Amount);
        var netByAccount = entries
            .SelectMany(entry => entry.Postings)
            .GroupBy(posting => posting.AccountId)
            .Select(group => group.Sum(posting =>
                posting.Direction == PostingDirection.Debit ? posting.Amount : -posting.Amount));
        Assert.All(netByAccount, net => Assert.Equal(0m, net));
        Assert.Equal(invoice.Amount, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, openItem.Status);
    }

    [Fact]
    public async Task Tenant_without_ledger_settings_records_payment_without_booking()
    {
        var tenant = await SetupTenantAsync(withLedger: false);
        var invoice = await CreateInvoiceAsync(tenant, 100m);

        PaymentOperationResult result;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            result = await CreateService(db, tenant).RecordAsync(
                Request(invoice.OpenItemId, invoice.Amount));
        }

        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(
            0,
            await read.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.Id == invoice.OpenItemId);
        Assert.Equal(0m, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Paid, openItem.Status);
    }

    private async Task<Guid> SetupTenantAsync(bool withLedger)
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        if (!withLedger)
        {
            return tenant;
        }

        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Soll,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            currentTenant,
            CancellationToken.None);
        Assert.Equal(
            StatusCodes.Status201Created,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        return tenant;
    }

    private async Task<(Guid DocumentId, Guid OpenItemId, decimal Amount)> CreateInvoiceAsync(
        Guid tenant,
        decimal netAmount)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, netAmount, TaxCategory.S, 19m)],
            new DateOnly(2026, 8, 1));
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(item => item.DocumentId == documentId);
        return (documentId, openItem.Id, openItem.OriginalAmount);
    }

    private static RecordPaymentRequest Request(Guid openItemId, decimal amount) =>
        new(
            amount,
            ValueDate,
            PaymentMethod.BankTransfer,
            "LEDGER-TEST",
            [new PaymentAllocationInput(openItemId, amount)]);

    private static PaymentService CreateService(NumeraDbContext db, Guid tenant)
    {
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        return new PaymentService(
            db,
            currentTenant,
            new NoOpAuditWriter(),
            new PostingEngine(db),
            new AccountResolver(db),
            NullLogger<PaymentService>.Instance);
    }

    private static async Task<JournalEntry> PaymentEntryAsync(NumeraDbContext db, Guid paymentId) =>
        await db.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .SingleAsync(entry =>
                entry.SourceType == LedgerSourceType.Payment
                && entry.SourceRef == paymentId.ToString());

    private static async Task<IReadOnlyDictionary<Guid, string>> AccountNumbersAsync(NumeraDbContext db) =>
        await db.Set<Account>()
            .AsNoTracking()
            .ToDictionaryAsync(account => account.Id, account => account.Number);

    private static void AssertBalanced(IEnumerable<Posting> postings)
    {
        var list = postings.ToList();
        var debit = list.Where(posting => posting.Direction == PostingDirection.Debit).Sum(posting => posting.Amount);
        var credit = list.Where(posting => posting.Direction == PostingDirection.Credit).Sum(posting => posting.Amount);
        Assert.Equal(decimal.Round(debit, 2, MidpointRounding.AwayFromZero), debit);
        Assert.Equal(decimal.Round(credit, 2, MidpointRounding.AwayFromZero), credit);
        Assert.Equal(debit, credit);
    }

    private static Posting AssertLeg(
        IEnumerable<Posting> postings,
        IReadOnlyDictionary<Guid, string> accountNumbers,
        string accountNumber,
        PostingDirection direction,
        decimal amount) =>
        Assert.Single(postings, posting =>
            accountNumbers[posting.AccountId] == accountNumber
            && posting.Direction == direction
            && posting.Amount == amount);
}
