using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Modules.Banking;
using Numera.Modules.Banking.Reconciliation;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of the human-confirmed bank reconciliation booking seam.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BankReconciliationBookingTests(PostgresFixture fixture)
{
    private static readonly DateOnly ValueDate = new(2026, 8, 5);

    [Fact]
    public async Task Confirm_exact_match_books_payment_and_closes_receivable()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 200m);
        var transactionId = await SeedTransactionAsync(
            tenant,
            invoice.Amount,
            $"Zahlung {invoice.DocumentNumber}",
            "E2E-BOOKING");

        var result = await ConfirmAsync(
            tenant,
            transactionId,
            [new PaymentAllocationInput(invoice.OpenItemId, invoice.Amount)]);

        AssertStatus(result, StatusCodes.Status201Created);
        await using var read = fixture.CreateAppContext(tenant);
        var transaction = await read.Set<BankTransaction>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == transactionId);
        var paymentId = Assert.IsType<Guid>(transaction.MatchedPaymentId);
        var payment = await read.Set<Payment>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == paymentId);
        var allocation = await read.Set<PaymentAllocation>().AsNoTracking()
            .SingleAsync(candidate => candidate.PaymentId == paymentId);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.OpenItemId);
        var document = await read.Set<SalesDocument>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.DocumentId);
        var entry = await PaymentEntryAsync(read, paymentId);
        var accountNumbers = await AccountNumbersAsync(read);

        Assert.Equal(MatchStatus.Confirmed, transaction.MatchStatus);
        Assert.Equal(invoice.Amount, payment.Amount);
        Assert.Equal(ValueDate, payment.ValueDate);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal("E2E-BOOKING", payment.Reference);
        Assert.Equal(invoice.OpenItemId, allocation.OpenItemId);
        Assert.Equal(invoice.Amount, allocation.AllocatedAmount);
        Assert.Equal(0m, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Paid, openItem.Status);
        Assert.Equal(0m, document.AmountDue);
        Assert.Equal(DocumentStatus.Paid, document.Status);
        Assert.Equal(LedgerSourceType.Payment, entry.SourceType);
        AssertLeg(entry.Postings, accountNumbers, "1200", PostingDirection.Debit, invoice.Amount);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Credit, invoice.Amount);
    }

    [Fact]
    public async Task Confirm_split_allocates_three_hundred_euros_across_two_open_items()
    {
        var tenant = await SetupTenantAsync();
        var first = await CreateInvoiceAsync(tenant, 200m);
        var second = await CreateInvoiceAsync(tenant, 100m);
        var transactionId = await SeedTransactionAsync(tenant, 300m, "Sammelzahlung");

        var result = await ConfirmAsync(
            tenant,
            transactionId,
            [
                new PaymentAllocationInput(first.OpenItemId, 200m),
                new PaymentAllocationInput(second.OpenItemId, 100m),
            ]);

        AssertStatus(result, StatusCodes.Status201Created);
        await using var read = fixture.CreateAppContext(tenant);
        var transaction = await read.Set<BankTransaction>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == transactionId);
        var paymentId = Assert.IsType<Guid>(transaction.MatchedPaymentId);
        var allocations = await read.Set<PaymentAllocation>()
            .AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId)
            .OrderByDescending(allocation => allocation.AllocatedAmount)
            .ToListAsync();
        var openItems = await read.Set<OpenItem>()
            .AsNoTracking()
            .Where(item => item.Id == first.OpenItemId || item.Id == second.OpenItemId)
            .ToListAsync();

        Assert.Equal(2, allocations.Count);
        Assert.Equal([200m, 100m], allocations.Select(allocation => allocation.AllocatedAmount));
        Assert.Equal(2, openItems.Count);
        Assert.All(openItems, item =>
        {
            Assert.Equal(0m, item.OpenAmount);
            Assert.Equal(OpenItemStatus.Paid, item.Status);
        });
    }

    [Fact]
    public async Task Unmatch_reverses_payment_and_reopens_document()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 200m);
        var transactionId = await SeedTransactionAsync(tenant, invoice.Amount, "Korrekturfall");
        AssertStatus(
            await ConfirmAsync(
                tenant,
                transactionId,
                [new PaymentAllocationInput(invoice.OpenItemId, invoice.Amount)]),
            StatusCodes.Status201Created);

        var result = await UnmatchAsync(tenant, transactionId);

        AssertStatus(result, StatusCodes.Status200OK);
        await using var read = fixture.CreateAppContext(tenant);
        var transaction = await read.Set<BankTransaction>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == transactionId);
        var payments = await read.Set<Payment>().AsNoTracking()
            .OrderBy(payment => payment.RecordedAt)
            .ToListAsync();
        var original = Assert.Single(payments, payment => payment.ReversesPaymentId == null);
        var reversal = Assert.Single(payments, payment => payment.ReversesPaymentId == original.Id);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.OpenItemId);
        var document = await read.Set<SalesDocument>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.DocumentId);

        Assert.Equal(-invoice.Amount, reversal.Amount);
        Assert.Equal(MatchStatus.Unmatched, transaction.MatchStatus);
        Assert.Null(transaction.MatchedPaymentId);
        Assert.Equal(invoice.Amount, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, openItem.Status);
        Assert.Equal(invoice.Amount, document.AmountDue);
        Assert.Equal(DocumentStatus.Finalized, document.Status);
        Assert.Equal(2, await read.Set<JournalEntry>()
            .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
    }

    [Fact]
    public async Task Suggestions_never_book_before_human_confirm()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 200m);
        var transactionId = await SeedTransactionAsync(
            tenant,
            invoice.Amount,
            $"Rechnung {invoice.DocumentNumber}");

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await BankTransactionEndpoints.SuggestionsAsync(
                transactionId,
                db,
                new ReconciliationScorer(db),
                CancellationToken.None);
            AssertStatus(result, StatusCodes.Status200OK);
        }

        await using (var beforeConfirm = fixture.CreateAppContext(tenant))
        {
            Assert.Equal(0, await beforeConfirm.Set<Payment>().CountAsync());
            Assert.Equal(0, await beforeConfirm.Set<PaymentAllocation>().CountAsync());
            Assert.Equal(0, await beforeConfirm.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
            var transaction = await beforeConfirm.Set<BankTransaction>().AsNoTracking()
                .SingleAsync(candidate => candidate.Id == transactionId);
            Assert.Equal(MatchStatus.Unmatched, transaction.MatchStatus);
            Assert.Null(transaction.MatchedPaymentId);
        }

        AssertStatus(
            await ConfirmAsync(
                tenant,
                transactionId,
                [new PaymentAllocationInput(invoice.OpenItemId, invoice.Amount)]),
            StatusCodes.Status201Created);
        await using var afterConfirm = fixture.CreateAppContext(tenant);
        Assert.Equal(1, await afterConfirm.Set<Payment>().CountAsync());
    }

    [Fact]
    public async Task Confirm_is_idempotent_and_does_not_double_book()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 200m);
        var transactionId = await SeedTransactionAsync(tenant, invoice.Amount, "Doppelklick");
        var allocations = new[]
        {
            new PaymentAllocationInput(invoice.OpenItemId, invoice.Amount),
        };

        AssertStatus(
            await ConfirmAsync(tenant, transactionId, allocations),
            StatusCodes.Status201Created);
        AssertStatus(
            await ConfirmAsync(tenant, transactionId, allocations),
            StatusCodes.Status200OK);

        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(1, await read.Set<Payment>().CountAsync());
        Assert.Equal(1, await read.Set<PaymentAllocation>().CountAsync());
        Assert.Equal(1, await read.Set<JournalEntry>()
            .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
        var transaction = await read.Set<BankTransaction>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == transactionId);
        Assert.Equal(MatchStatus.Confirmed, transaction.MatchStatus);
        Assert.NotNull(transaction.MatchedPaymentId);
    }

    private async Task<Guid> SetupTenantAsync()
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await using var db = fixture.CreateAppContext(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Soll,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            TenantOf(tenant),
            CancellationToken.None);
        AssertStatus(result, StatusCodes.Status201Created);
        return tenant;
    }

    private async Task<InvoiceSeed> CreateInvoiceAsync(Guid tenant, decimal amount)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, amount, TaxCategory.Z, 0m)],
            new DateOnly(2026, 8, 1));
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var document = await read.Set<SalesDocument>().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == documentId);
        var openItem = await read.Set<OpenItem>().AsNoTracking()
            .SingleAsync(candidate => candidate.DocumentId == documentId);
        return new InvoiceSeed(
            document.Id,
            document.DocumentNumber!,
            openItem.Id,
            openItem.OriginalAmount);
    }

    private async Task<Guid> SeedTransactionAsync(
        Guid tenant,
        decimal amount,
        string purpose,
        string? endToEndId = null)
    {
        var account = new BankAccount
        {
            TenantId = tenant,
            Iban = "IMPORT",
            DisplayName = "Kontoauszug-Import",
        };
        var transaction = new BankTransaction
        {
            TenantId = tenant,
            BankAccountId = account.Id,
            DedupeKey = Guid.NewGuid().ToString("N"),
            Source = BankTransactionSource.Csv,
            Amount = amount,
            ValueDate = ValueDate,
            BookingDate = ValueDate,
            Purpose = purpose,
            CounterpartyName = "Beispielkunde GmbH",
            CounterpartyIban = "DE02120300000000202051",
            EndToEndId = endToEndId,
            MatchStatus = MatchStatus.Unmatched,
        };

        await using var db = fixture.CreateAppContext(tenant);
        db.Add(account);
        db.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private async Task<IResult> ConfirmAsync(
        Guid tenant,
        Guid transactionId,
        IReadOnlyList<PaymentAllocationInput> allocations)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await BankTransactionEndpoints.ConfirmAsync(
            transactionId,
            new BankingContracts.ConfirmBankTransactionRequest(
                allocations,
                PaymentMethod.BankTransfer),
            db,
            CreatePaymentService(db, tenant),
            CancellationToken.None);
    }

    private async Task<IResult> UnmatchAsync(Guid tenant, Guid transactionId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await BankTransactionEndpoints.UnmatchAsync(
            transactionId,
            db,
            CreatePaymentService(db, tenant),
            CancellationToken.None);
    }

    private static PaymentService CreatePaymentService(NumeraDbContext db, Guid tenant) =>
        new(
            db,
            TenantOf(tenant),
            new NoOpAuditWriter(),
            new PostingEngine(db),
            new AccountResolver(db),
            NullLogger<PaymentService>.Instance);

    private static async Task<JournalEntry> PaymentEntryAsync(NumeraDbContext db, Guid paymentId) =>
        await db.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .SingleAsync(entry =>
                entry.SourceType == LedgerSourceType.Payment
                && entry.SourceRef == paymentId.ToString());

    private static async Task<IReadOnlyDictionary<Guid, string>> AccountNumbersAsync(
        NumeraDbContext db) =>
        await db.Set<Account>()
            .AsNoTracking()
            .ToDictionaryAsync(account => account.Id, account => account.Number);

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

    private static void AssertStatus(IResult result, int expectedStatusCode)
    {
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(expectedStatusCode, status.StatusCode);
    }

    private static TenantContext TenantOf(Guid tenant)
    {
        var current = new TenantContext();
        current.SetTenant(tenant);
        return current;
    }

    private sealed record InvoiceSeed(
        Guid DocumentId,
        string DocumentNumber,
        Guid OpenItemId,
        decimal Amount);
}
