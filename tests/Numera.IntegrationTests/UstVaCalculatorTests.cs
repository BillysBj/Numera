using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Numbering;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres golden-value proof of the Phase-11 USt-VA calculator.</summary>
[Collection(PostgresCollection.Name)]
public sealed class UstVaCalculatorTests(PostgresFixture fixture)
{
    private static readonly DateOnly JanuaryDate = new(2026, 1, 12);
    private static readonly DateOnly FebruaryDate = new(2026, 2, 10);

    [Fact]
    public async Task Soll_computes_81_86_zero_66_and_83_from_untruncated_bases()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll, ChartVariant.Skr03);
        await CreateInvoiceAsync(
            tenant,
            JanuaryDate,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100.75m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200.50m, TaxCategory.S, 7m),
            ]);

        var report = await ComputeAsync(tenant, "01");

        Assert.Equal(Besteuerungsart.Soll, report.Besteuerungsart);
        Assert.Equal(100m, FindLine(report, "81").Bemessungsgrundlage);
        Assert.Equal(200m, FindLine(report, "86").Bemessungsgrundlage);
        Assert.Equal(0m, FindLine(report, "66").Steuer);
        Assert.Equal(33.18m, FindLine(report, "83").Steuer);
        Assert.Equal(33.18m, report.Zahllast);
        Assert.Null(report.Hinweis);
    }

    [Fact]
    public async Task Ist_attributes_81_to_payment_month_instead_of_invoice_month()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Ist, ChartVariant.Skr03);
        var invoice = await CreateInvoiceAsync(
            tenant,
            JanuaryDate,
            [new SalesTestData.LineSpec("Beratung", 1m, 125m, TaxCategory.S, 19m)]);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount, FebruaryDate);

        var invoiceMonth = await ComputeAsync(tenant, "01");
        var paymentMonth = await ComputeAsync(tenant, "02");

        Assert.Equal(0m, FindLine(invoiceMonth, "81").Bemessungsgrundlage);
        Assert.Equal(125m, FindLine(paymentMonth, "81").Bemessungsgrundlage);
        Assert.Equal(23.75m, paymentMonth.Zahllast);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, "8125")]
    [InlineData(ChartVariant.Skr04, "4125")]
    public async Task Intra_community_delivery_is_reported_as_41_from_the_seeded_revenue_account(
        ChartVariant chartVariant,
        string revenueAccount)
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll, chartVariant);
        await CreateInvoiceAsync(
            tenant,
            JanuaryDate,
            [new SalesTestData.LineSpec("Innergemeinschaftliche Lieferung", 1m, 450m, TaxCategory.K, 0m)]);

        var report = await ComputeAsync(tenant, "01");
        var line = FindLine(report, "41");

        Assert.Equal(450m, line.Bemessungsgrundlage);
        Assert.Contains(revenueAccount, line.ContributingAccountNumbers);
        await using var db = fixture.CreateAppContext(tenant);
        var account = await db.Set<Account>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Number == revenueAccount);
        Assert.Equal(Steuerschluessel.TaxFreeWithInput, account.Steuerschluessel);
        Assert.Equal("41", account.UstvaKennziffer);
    }

    [Fact]
    public async Task Soll_cross_period_storno_reduces_81_in_the_reversal_period()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll, ChartVariant.Skr03);
        var invoice = await CreateInvoiceAsync(
            tenant,
            JanuaryDate,
            [new SalesTestData.LineSpec("Beratung", 1m, 321m, TaxCategory.S, 19m)]);
        await StornoAsync(tenant, invoice.DocumentId, FebruaryDate);

        var originalPeriod = await ComputeAsync(tenant, "01");
        var reversalPeriod = await ComputeAsync(tenant, "02");

        Assert.Equal(321m, FindLine(originalPeriod, "81").Bemessungsgrundlage);
        Assert.Equal(-321m, FindLine(reversalPeriod, "81").Bemessungsgrundlage);
        Assert.Equal(-60.99m, reversalPeriod.Zahllast);
    }

    [Theory]
    [InlineData(Besteuerungsart.Soll)]
    [InlineData(Besteuerungsart.Ist)]
    public async Task Input_vat_uses_booking_date_for_both_taxation_methods_and_storno_nets(
        Besteuerungsart besteuerungsart)
    {
        var tenant = await SetupTenantAsync(besteuerungsart, ChartVariant.Skr03);
        var originalEntryId = await PostExpenseAsync(tenant, JanuaryDate);
        await PostExpenseReversalAsync(tenant, originalEntryId, FebruaryDate);

        var originalPeriod = await ComputeAsync(tenant, "01");
        var reversalPeriod = await ComputeAsync(tenant, "02");

        Assert.Equal(19m, FindLine(originalPeriod, "66").Steuer);
        Assert.Equal(-19m, originalPeriod.Zahllast);
        Assert.Equal(-19m, FindLine(reversalPeriod, "66").Steuer);
        Assert.Equal(19m, reversalPeriod.Zahllast);
    }

    [Fact]
    public async Task Kleinunternehmer_is_gated_without_zero_filled_vat_lines()
    {
        var tenant = await SetupTenantAsync(
            Besteuerungsart.Soll,
            ChartVariant.Skr03,
            isKleinunternehmer: true);

        var report = await ComputeAsync(tenant, "01");

        Assert.True(report.IsKleinunternehmer);
        Assert.Empty(report.Lines);
        Assert.Equal(0m, report.Zahllast);
        Assert.Null(report.Hinweis);
    }

    [Fact]
    public async Task Report_never_fabricates_unsupported_kennziffern()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll, ChartVariant.Skr03);
        await CreateInvoiceAsync(
            tenant,
            JanuaryDate,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);

        var report = await ComputeAsync(tenant, "01");
        var actual = report.Lines.Select(line => line.Kz).ToHashSet(StringComparer.Ordinal);
        var producible = new HashSet<string>(["81", "86", "41", "66", "83"], StringComparer.Ordinal);
        string[] omitted = ["35", "36", "89", "61", "46", "47"];

        Assert.All(actual, kz => Assert.Contains(kz, producible));
        Assert.All(omitted, kz => Assert.DoesNotContain(kz, actual));
    }

    private async Task<Guid> SetupTenantAsync(
        Besteuerungsart besteuerungsart,
        ChartVariant chartVariant,
        bool isKleinunternehmer = false)
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant, kleinunternehmer: isKleinunternehmer);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Storno, "ST-");

        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                chartVariant,
                besteuerungsart,
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

    private async Task<(Guid DocumentId, Guid OpenItemId, decimal GrossAmount)> CreateInvoiceAsync(
        Guid tenant,
        DateOnly documentDate,
        IReadOnlyList<SalesTestData.LineSpec> lines)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            lines,
            documentDate);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItem = await read.Set<OpenItem>()
            .AsNoTracking()
            .SingleAsync(item => item.DocumentId == documentId);
        return (documentId, openItem.Id, openItem.OriginalAmount);
    }

    private async Task RecordPaymentAsync(
        Guid tenant,
        Guid openItemId,
        decimal amount,
        DateOnly valueDate)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var result = await CreatePaymentService(db, tenant).RecordAsync(
            new RecordPaymentRequest(
                amount,
                valueDate,
                PaymentMethod.BankTransfer,
                "USTVA-TEST",
                [new PaymentAllocationInput(openItemId, amount)]));
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
    }

    private async Task<Guid> PostExpenseAsync(Guid tenant, DateOnly entryDate)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var header = new JournalEntry
        {
            TenantId = tenant,
            EntryDate = entryDate,
            SourceType = LedgerSourceType.Expense,
            SourceRef = $"expense-{Guid.CreateVersion7()}",
            Description = "Vorsteuer-Test",
            PostingType = PostingType.Normal,
        };
        await new PostingEngine(db).PostAsync(
            new ExpensePostingSource(
                tenant,
                ChartVariant.Skr03,
                new ExpensePostingInput(null, null, 19m, 100m, 19m, entryDate),
                new AccountResolver(db)),
            header,
            CancellationToken.None);
        return header.Id;
    }

    private async Task PostExpenseReversalAsync(Guid tenant, Guid originalEntryId, DateOnly entryDate)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var original = await db.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .SingleAsync(entry => entry.Id == originalEntryId);
        var header = new JournalEntry
        {
            TenantId = tenant,
            EntryDate = entryDate,
            SourceType = LedgerSourceType.Expense,
            SourceRef = $"expense-storno-{Guid.CreateVersion7()}",
            Description = "Vorsteuer-Test Storno",
            PostingType = PostingType.Storno,
            ReversesEntryId = original.Id,
        };
        await new PostingEngine(db).PostAsync(
            new TestReversalPostingSource(original.Postings, tenant),
            header,
            CancellationToken.None);
    }

    private async Task StornoAsync(Guid tenant, Guid originalDocumentId, DateOnly stornoDate)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var original = await db.Set<SalesDocument>()
            .Include(document => document.Lines)
            .SingleAsync(document => document.Id == originalDocumentId);
        var profile = await db.Set<CompanyProfile>().SingleAsync();
        var partner = await db.Set<BusinessPartner>()
            .SingleAsync(candidate => candidate.Id == original.PartnerId);
        var storno = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Storno,
            Status = DocumentStatus.Draft,
            CorrectsDocumentId = original.Id,
            PartnerId = original.PartnerId,
            DocumentDate = stornoDate,
            ServiceDate = original.ServiceDate,
            Currency = original.Currency,
        };
        foreach (var line in original.Lines.OrderBy(line => line.LineNumber))
        {
            storno.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = storno.Id,
                LineNumber = line.LineNumber,
                Name = line.Name,
                Quantity = -line.Quantity,
                UnitCode = line.UnitCode,
                NetUnitPrice = line.NetUnitPrice,
                LineNetAmount = -line.LineNetAmount,
                TaxCategory = line.TaxCategory,
                VatRatePercent = line.VatRatePercent,
            });
        }

        db.Add(storno);
        await using var tx = await db.Database.BeginTransactionAsync();
        var postingEngine = new PostingEngine(db);
        await SalesDocumentEndpoints.FinalizeCoreAsync(
            storno,
            profile,
            partner,
            db,
            new NumberingService(db),
            new NoOpAuditWriter(),
            postingEngine,
            new AccountResolver(db),
            NullLoggerFactory.Instance,
            tenant,
            "UstVaStornoTest",
            CancellationToken.None);

        original.Status = DocumentStatus.Cancelled;
        original.CancelledByDocumentId = storno.Id;
        var openItem = await db.Set<OpenItem>().SingleAsync(item => item.DocumentId == original.Id);
        openItem.Status = OpenItemStatus.Cancelled;
        openItem.OpenAmount = 0m;

        await SalesDocumentEndpoints.PostStornoReversalAsync(
            original,
            storno,
            db,
            postingEngine,
            NullLoggerFactory.Instance,
            tenant,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<UstVaReport> ComputeAsync(Guid tenant, string zeitraum)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await new UstVaCalculator(db, new RecognitionReader(db))
            .ComputeAsync(2026, zeitraum, CancellationToken.None);
    }

    private static PaymentService CreatePaymentService(NumeraDbContext db, Guid tenant)
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

    private static UstVaLine FindLine(UstVaReport report, string kz) =>
        Assert.Single(report.Lines, line => line.Kz == kz);

    private sealed class TestReversalPostingSource(
        IEnumerable<Posting> originalPostings,
        Guid tenantId) : IPostingSource
    {
        public IReadOnlyList<Posting> BuildPostings() => originalPostings
            .Select(posting => new Posting
            {
                TenantId = tenantId,
                AccountId = posting.AccountId,
                Amount = posting.Amount,
                Direction = posting.Direction == PostingDirection.Debit
                    ? PostingDirection.Credit
                    : PostingDirection.Debit,
                Steuerschluessel = posting.Steuerschluessel,
                TaxRatePercent = posting.TaxRatePercent,
                TaxCategory = posting.TaxCategory,
            })
            .ToList();
    }
}
