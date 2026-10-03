using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Ledger.Tax;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres golden-value proof of the Phase-11 EÜR calculator.</summary>
[Collection(PostgresCollection.Name)]
public sealed class EuerCalculatorTests(PostgresFixture fixture)
{
    private static readonly DateOnly JanuaryDate = new(2026, 1, 12);
    private static readonly DateOnly FebruaryDate = new(2026, 2, 10);
    private static readonly DateOnly JanuaryFrom = new(2026, 1, 1);
    private static readonly DateOnly JanuaryTo = new(2026, 1, 31);
    private static readonly DateOnly FebruaryFrom = new(2026, 2, 1);
    private static readonly DateOnly FebruaryTo = new(2026, 2, 28);

    [Fact]
    public async Task Remitted_vat_makes_bruttomethode_profit_vat_neutral()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 100m, 19m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);
        var before = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);
        Assert.Equal(119m, before.Gewinn);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var currentTenant = new TenantContext();
            currentTenant.SetTenant(tenant);
            var entriesBefore = await db.Set<JournalEntry>().CountAsync();
            var result = await new VatPaymentService(db, currentTenant, new NoOpAuditWriter())
                .RecordAsync(19m, VatPaymentKind.Payment, FebruaryDate, "USt-Vorauszahlung");
            Assert.Equal(PaymentOperationStatus.Success, result.Status);
            Assert.Equal(entriesBefore, await db.Set<JournalEntry>().CountAsync());
        }

        var after = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);
        Assert.Equal(119m, after.SummeEinnahmen);
        Assert.Equal(19m, FindIncomeLine(after, EuerLineMap.CollectedVatZeile).Betrag);
        Assert.Equal(19m, Assert.Single(after.Betriebsausgaben, line => line.Zeile == EuerLineMap.PaidOutputVatZeile).Betrag);
        Assert.Equal(19m, after.SummeAusgaben);
        Assert.Equal(100m, after.Gewinn);
    }

    [Fact]
    public async Task Zufluss_timing_recognizes_income_only_in_the_payment_window()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 100m, 19m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);

        var invoiceWindow = await ComputeAsync(tenant, JanuaryFrom, JanuaryTo);
        var paymentWindow = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);

        Assert.Equal(0m, invoiceWindow.SummeEinnahmen);
        Assert.Equal(100m, FindIncomeLine(paymentWindow, EuerLineMap.TaxableRevenueZeile).Betrag);
        Assert.Equal(19m, FindIncomeLine(paymentWindow, EuerLineMap.CollectedVatZeile).Betrag);
        Assert.Equal(119m, paymentWindow.SummeEinnahmen);
    }

    [Fact]
    public async Task Invoice_and_payment_postings_do_not_double_count_paid_net_income()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 80m, 19m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            Assert.Equal(1, await db.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Invoice));
            Assert.Equal(1, await db.Set<JournalEntry>()
                .CountAsync(entry => entry.SourceType == LedgerSourceType.Payment));
        }

        var report = await ComputeAsync(tenant, JanuaryFrom, FebruaryTo);

        Assert.Equal(80m, FindIncomeLine(report, EuerLineMap.TaxableRevenueZeile).Betrag);
    }

    [Fact]
    public async Task Regelunternehmer_reports_net_revenue_and_collected_vat_separately()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 200m, 7m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);

        var report = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);

        Assert.False(report.IsKleinunternehmer);
        Assert.Equal(200m, FindIncomeLine(report, "15").Betrag);
        Assert.Equal(14m, FindIncomeLine(report, "17").Betrag);
        Assert.Equal(0m, FindIncomeLine(report, "12").Betrag);
        Assert.Contains(report.Betriebsausgaben, line => line.Zeile == EuerLineMap.PaidInputVatZeile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Kleinunternehmer_reports_gross_receipts_without_a_collected_vat_split(bool invoiceIssuedAsKleinunternehmer)
    {
        var tenant = await SetupTenantAsync(isKleinunternehmer: invoiceIssuedAsKleinunternehmer);
        var invoice = await CreateInvoiceAsync(tenant, 125m, 19m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);

        // Also cover frozen receipts containing VAT: the §19 report must fold net + VAT.
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var profile = await db.Set<CompanyProfile>().SingleAsync();
            profile.IsKleinunternehmer = true;
            await db.SaveChangesAsync();
        }

        var report = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);

        Assert.True(report.IsKleinunternehmer);
        Assert.Equal(invoice.GrossAmount, FindIncomeLine(report, "12").Betrag);
        Assert.Equal(0m, FindIncomeLine(report, "16").Betrag);
        Assert.Equal(invoiceIssuedAsKleinunternehmer ? 125m : 148.75m, report.SummeEinnahmen);
        Assert.DoesNotContain(
            report.Betriebseinnahmen,
            line => line.Zeile == EuerLineMap.CollectedVatZeile);
        Assert.DoesNotContain(
            report.Betriebsausgaben,
            line => line.Zeile == EuerLineMap.PaidInputVatZeile);
    }

    [Fact]
    public async Task Gewinn_equals_income_minus_expenses()
    {
        var tenant = await SetupTenantAsync();
        var invoice = await CreateInvoiceAsync(tenant, 150m, 19m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount);

        var report = await ComputeAsync(tenant, FebruaryFrom, FebruaryTo);

        Assert.Equal(178.50m, report.SummeEinnahmen);
        Assert.Equal(0m, report.SummeAusgaben);
        Assert.Equal(report.SummeEinnahmen - report.SummeAusgaben, report.Gewinn);
    }

    [Fact]
    public async Task Configured_ledger_has_complete_expense_recognition_even_without_payments()
    {
        var tenant = await SetupTenantAsync();

        var report = await ComputeAsync(tenant, JanuaryFrom, FebruaryTo);

        Assert.False(report.IsExpenseDataIncomplete);
        Assert.Null(report.Hinweis);
        Assert.NotEmpty(report.Betriebsausgaben);
        Assert.All(report.Betriebsausgaben, line => Assert.Equal(0m, line.Betrag));
    }

    private async Task<Guid> SetupTenantAsync(bool isKleinunternehmer = false)
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(
            fixture,
            tenant,
            kleinunternehmer: isKleinunternehmer);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");

        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Ist,
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

    private async Task<(Guid OpenItemId, decimal GrossAmount)> CreateInvoiceAsync(
        Guid tenant,
        decimal netAmount,
        decimal vatRatePercent)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec(
                "Leistung",
                1m,
                netAmount,
                TaxCategory.S,
                vatRatePercent)],
            JanuaryDate);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItem = await read.Set<OpenItem>()
            .AsNoTracking()
            .SingleAsync(item => item.DocumentId == documentId);
        return (openItem.Id, openItem.OriginalAmount);
    }

    private async Task RecordPaymentAsync(Guid tenant, Guid openItemId, decimal amount)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var result = await CreatePaymentService(db, tenant).RecordAsync(
            new RecordPaymentRequest(
                amount,
                FebruaryDate,
                PaymentMethod.BankTransfer,
                "EUER-TEST",
                [new PaymentAllocationInput(openItemId, amount)]));
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
    }

    private async Task<EuerReport> ComputeAsync(Guid tenant, DateOnly from, DateOnly to)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await new EuerCalculator(db, new RecognitionReader(db))
            .ComputeAsync(2026, from, to, CancellationToken.None);
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

    private static EuerLine FindIncomeLine(EuerReport report, string zeile) =>
        Assert.Single(report.Betriebseinnahmen, line => line.Zeile == zeile);
}
