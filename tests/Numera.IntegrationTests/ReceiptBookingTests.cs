using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Modules.Crm;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

using CrmAddress = Numera.Modules.Crm.Address;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of the human-gated receipt booking path.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceiptBookingTests(PostgresFixture fixture)
{
    private static readonly DateOnly BookingDate = new(2026, 8, 4);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Extracted_receipt_is_never_auto_booked()
    {
        var tenant = await SetupTenantAsync();
        var receiptId = await SeedReceiptAsync(
            tenant, ReceiptStatus.Extracted, 19m, 100m, 19m, 119m);

        var result = await ConfirmAsync(tenant, receiptId);

        AssertStatus(result, StatusCodes.Status422UnprocessableEntity);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(0, await read.Set<JournalEntry>()
            .CountAsync(entry => entry.SourceType == LedgerSourceType.Expense));
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        Assert.Equal(ReceiptStatus.Extracted, receipt.Status);
        Assert.Null(receipt.JournalEntryId);
    }

    [Fact]
    public async Task Reviewed_19_percent_receipt_books_balanced_expense_input_tax_and_creditor()
    {
        var tenant = await SetupTenantAsync();
        var supplier = await SeedSupplierAsync(tenant);
        var receiptId = await SeedReceiptAsync(
            tenant, ReceiptStatus.Extracted, 19m, 100m, 19m, 119m);
        var reviewer = Guid.CreateVersion7();
        AssertStatus(
            await ReviewAsync(tenant, receiptId, supplier.Id, reviewer, 19m, 100m, 19m, 119m),
            StatusCodes.Status200OK);

        var result = await ConfirmAsync(tenant, receiptId);

        AssertStatus(result, StatusCodes.Status200OK);
        await using var read = fixture.CreateAppContext(tenant);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        var entry = await read.Set<JournalEntry>()
            .AsNoTracking()
            .Include(candidate => candidate.Postings)
            .SingleAsync(candidate =>
                candidate.SourceType == LedgerSourceType.Expense
                && candidate.SourceRef == receiptId.ToString());
        var accounts = await LoadAccountNumbersAsync(read, entry.Postings);

        Assert.Equal(ReceiptStatus.Booked, receipt.Status);
        Assert.Equal(entry.Id, receipt.JournalEntryId);
        Assert.Equal(reviewer, receipt.ReviewedByUserId);
        Assert.Equal(119m, entry.Postings.Where(p => p.Direction == PostingDirection.Debit).Sum(p => p.Amount));
        Assert.Equal(119m, entry.Postings.Where(p => p.Direction == PostingDirection.Credit).Sum(p => p.Amount));
        AssertPosting(entry, accounts, "4980", PostingDirection.Debit, 100m);
        AssertPosting(entry, accounts, "1576", PostingDirection.Debit, 19m);
        AssertPosting(entry, accounts, "1600", PostingDirection.Credit, 119m);
        var inputTaxAccount = await read.Set<Account>()
            .AsNoTracking()
            .SingleAsync(account => account.Number == "1576");
        Assert.Equal("66", inputTaxAccount.UstvaKennziffer);
    }

    [Fact]
    public async Task Second_confirm_book_is_idempotent()
    {
        var tenant = await SetupTenantAsync();
        var supplier = await SeedSupplierAsync(tenant);
        var receiptId = await SeedReceiptAsync(
            tenant, ReceiptStatus.Extracted, 19m, 80m, 15.20m, 95.20m);
        AssertStatus(
            await ReviewAsync(
                tenant, receiptId, supplier.Id, Guid.CreateVersion7(), 19m, 80m, 15.20m, 95.20m),
            StatusCodes.Status200OK);
        AssertStatus(await ConfirmAsync(tenant, receiptId), StatusCodes.Status200OK);

        var second = await ConfirmAsync(tenant, receiptId);

        AssertStatus(second, StatusCodes.Status200OK);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(1, await read.Set<JournalEntry>().CountAsync(entry =>
            entry.SourceType == LedgerSourceType.Expense
            && entry.SourceRef == receiptId.ToString()));
        Assert.Equal(
            1,
            await read.Set<Receipt>().CountAsync(receipt =>
                receipt.Id == receiptId
                && receipt.Status == ReceiptStatus.Booked
                && receipt.JournalEntryId != null));
    }

    [Fact]
    public async Task Tax_free_receipt_books_expense_and_creditor_without_input_tax()
    {
        var tenant = await SetupTenantAsync();
        var supplier = await SeedSupplierAsync(tenant);
        var receiptId = await SeedReceiptAsync(
            tenant, ReceiptStatus.Extracted, 0m, 75m, 0m, 75m);
        AssertStatus(
            await ReviewAsync(
                tenant, receiptId, supplier.Id, Guid.CreateVersion7(), 0m, 75m, 0m, 75m),
            StatusCodes.Status200OK);

        AssertStatus(await ConfirmAsync(tenant, receiptId), StatusCodes.Status200OK);

        await using var read = fixture.CreateAppContext(tenant);
        var entry = await read.Set<JournalEntry>()
            .AsNoTracking()
            .Include(candidate => candidate.Postings)
            .SingleAsync(candidate => candidate.SourceRef == receiptId.ToString());
        var accounts = await LoadAccountNumbersAsync(read, entry.Postings);
        Assert.Equal(2, entry.Postings.Count);
        AssertPosting(entry, accounts, "4980", PostingDirection.Debit, 75m);
        AssertPosting(entry, accounts, "1600", PostingDirection.Credit, 75m);
        Assert.DoesNotContain(accounts.Values, number => number is "1576" or "1571");
    }

    [Fact]
    public async Task Tier_a_multi_rate_receipt_books_all_rates_in_one_journal_entry()
    {
        var tenant = await SetupTenantAsync();
        var supplier = await SeedSupplierAsync(tenant);
        var receiptId = await SeedTierAReceiptAsync(tenant, supplier.Id);
        AssertStatus(
            await ReviewAsync(
                tenant, receiptId, supplier.Id, Guid.CreateVersion7(), null, 300m, 33m, 333m),
            StatusCodes.Status200OK);

        AssertStatus(await ConfirmAsync(tenant, receiptId), StatusCodes.Status200OK);

        await using var read = fixture.CreateAppContext(tenant);
        var entries = await read.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .Where(entry =>
                entry.SourceType == LedgerSourceType.Expense
                && entry.SourceRef == receiptId.ToString())
            .ToListAsync();
        var entry = Assert.Single(entries);
        var accounts = await LoadAccountNumbersAsync(read, entry.Postings);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync(candidate => candidate.Id == receiptId);

        Assert.Equal(entry.Id, receipt.JournalEntryId);
        Assert.Equal(ReceiptStatus.Booked, receipt.Status);
        Assert.Equal(5, entry.Postings.Count);
        Assert.Equal(2, entry.Postings.Count(posting => accounts[posting.AccountId] == "4980"));
        AssertPosting(entry, accounts, "1576", PostingDirection.Debit, 19m);
        AssertPosting(entry, accounts, "1571", PostingDirection.Debit, 14m);
        Assert.Equal(300m, entry.Postings
            .Where(posting => accounts[posting.AccountId] == "4980")
            .Sum(posting => posting.Amount));
        Assert.Equal(33m, entry.Postings
            .Where(posting => accounts[posting.AccountId] is "1576" or "1571")
            .Sum(posting => posting.Amount));
        AssertPosting(entry, accounts, "1600", PostingDirection.Credit, 333m);
    }

    [Fact]
    public async Task Booked_expense_feeds_ustva_kz_66_and_reduces_zahllast()
    {
        var tenant = await SetupTenantAsync();
        var supplier = await SeedSupplierAsync(tenant);
        var receiptId = await SeedReceiptAsync(
            tenant, ReceiptStatus.Extracted, 19m, 120m, 22.80m, 142.80m);
        AssertStatus(
            await ReviewAsync(
                tenant, receiptId, supplier.Id, Guid.CreateVersion7(), 19m, 120m, 22.80m, 142.80m),
            StatusCodes.Status200OK);
        AssertStatus(await ConfirmAsync(tenant, receiptId), StatusCodes.Status200OK);

        await using var db = fixture.CreateAppContext(tenant);
        var ustva = await new UstVaCalculator(db, new RecognitionReader(db))
            .ComputeAsync(2026, "08", CancellationToken.None);

        Assert.Equal(22.80m, Assert.Single(ustva.Lines, line => line.Kz == "66").Steuer);
        Assert.Equal(-22.80m, Assert.Single(ustva.Lines, line => line.Kz == "83").Steuer);
        Assert.Equal(-22.80m, ustva.Zahllast);
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

    private async Task<BusinessPartner> SeedSupplierAsync(Guid tenant)
    {
        var supplier = new BusinessPartner
        {
            TenantId = tenant,
            Name = "Lieferant GmbH",
            IsSupplier = true,
            CreditorAccount = "1600",
            BillingAddress = new CrmAddress
            {
                Street = "Lieferweg 1",
                PostalCode = "10115",
                City = "Berlin",
                CountryCode = "DE",
            },
        };
        await using var db = fixture.CreateAppContext(tenant);
        db.Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    private async Task<Guid> SeedReceiptAsync(
        Guid tenant,
        ReceiptStatus status,
        decimal rate,
        decimal net,
        decimal vat,
        decimal gross)
    {
        var receipt = new Receipt
        {
            TenantId = tenant,
            Source = ReceiptSource.Upload,
            Status = status,
            ContentHash = Hash(),
            SupplierName = "Lieferant GmbH",
            InvoiceNumber = "ER-2026-001",
            InvoiceDate = BookingDate,
            NetAmount = net,
            VatAmount = vat,
            GrossAmount = gross,
            VatRatePercent = rate,
            Currency = "EUR",
        };
        await using var db = fixture.CreateAppContext(tenant);
        db.Add(receipt);
        await db.SaveChangesAsync();
        return receipt.Id;
    }

    private async Task<Guid> SeedTierAReceiptAsync(Guid tenant, Guid supplierId)
    {
        var readModel = new InboundReadModel
        {
            InvoiceNumber = "ZF-2026-19-7",
            InvoiceDate = BookingDate,
            Currency = "EUR",
            TotalNet = 300m,
            TotalTax = 33m,
            TotalGross = 333m,
            Seller = new InboundReadModel.PartyBlock { Name = "Lieferant GmbH" },
            BreakdownRows =
            [
                new InboundReadModel.BreakdownRow
                {
                    TaxCategory = "S",
                    VatRatePercent = 19m,
                    TaxableBase = 100m,
                    TaxAmount = 19m,
                },
                new InboundReadModel.BreakdownRow
                {
                    TaxCategory = "S",
                    VatRatePercent = 7m,
                    TaxableBase = 200m,
                    TaxAmount = 14m,
                },
            ],
        };
        var inbound = new InboundDocument
        {
            TenantId = tenant,
            OriginalBytes = [1, 2, 3],
            OriginalFileName = "zugferd.pdf",
            OriginalContentType = "application/pdf",
            ByteSize = 3,
            DetectedFormat = InboundFormat.ZugferdPdf,
            ReadModel = JsonSerializer.Serialize(readModel, Json),
            ValidationStatus = EInvoiceValidationStatus.Accepted,
            MatchedPartnerId = supplierId,
            SellerName = "Lieferant GmbH",
            InvoiceNumber = readModel.InvoiceNumber,
            TotalGross = readModel.TotalGross,
            Currency = readModel.Currency,
            InvoiceDate = readModel.InvoiceDate,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        var receipt = new Receipt
        {
            TenantId = tenant,
            Source = ReceiptSource.EInvoice,
            Status = ReceiptStatus.Extracted,
            InboundDocumentId = inbound.Id,
            ContentHash = Hash(),
            SupplierName = inbound.SellerName,
            InvoiceNumber = readModel.InvoiceNumber,
            InvoiceDate = readModel.InvoiceDate,
            NetAmount = readModel.TotalNet,
            VatAmount = readModel.TotalTax,
            GrossAmount = readModel.TotalGross,
            Currency = readModel.Currency,
            MatchedPartnerId = supplierId,
        };

        await using var db = fixture.CreateAppContext(tenant);
        db.Add(inbound);
        db.Add(receipt);
        await db.SaveChangesAsync();
        return receipt.Id;
    }

    private async Task<IResult> ReviewAsync(
        Guid tenant,
        Guid receiptId,
        Guid supplierId,
        Guid reviewer,
        decimal? rate,
        decimal net,
        decimal vat,
        decimal gross)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await ReceiptEndpoints.ReviewAsync(
            receiptId,
            new ReceiptContracts.ReviewReceiptRequest(
                supplierId,
                null,
                rate,
                net,
                vat,
                gross,
                "ER-2026-001",
                BookingDate,
                null),
            db,
            new NoOpAuditWriter(),
            new TestCurrentUser(reviewer),
            CancellationToken.None);
    }

    private async Task<IResult> ConfirmAsync(Guid tenant, Guid receiptId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await ReceiptEndpoints.ConfirmBookAsync(
            receiptId,
            db,
            new PostingEngine(db),
            new AccountResolver(db),
            new NoOpAuditWriter(),
            TenantOf(tenant),
            NullLoggerFactory.Instance,
            CancellationToken.None);
    }

    private static async Task<Dictionary<Guid, string>> LoadAccountNumbersAsync(
        NumeraDbContext db,
        IEnumerable<Posting> postings)
    {
        var ids = postings.Select(posting => posting.AccountId).Distinct().ToArray();
        return await db.Set<Account>()
            .AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Number);
    }

    private static void AssertPosting(
        JournalEntry entry,
        IReadOnlyDictionary<Guid, string> accounts,
        string accountNumber,
        PostingDirection direction,
        decimal amount) =>
        Assert.Contains(entry.Postings, posting =>
            accounts[posting.AccountId] == accountNumber
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

    private static string Hash() => Guid.NewGuid().ToString("N").PadRight(64, '0');

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
