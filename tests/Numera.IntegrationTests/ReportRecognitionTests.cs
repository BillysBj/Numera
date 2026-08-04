using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
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

/// <summary>Real-Postgres proof of report-time Soll and cash recognition.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportRecognitionTests(PostgresFixture fixture)
{
    private static readonly DateOnly CurrentMonth = MonthStart();
    private static readonly DateOnly InvoiceDate = CurrentMonth.AddMonths(-2).AddDays(5);
    private static readonly DateOnly PaymentDate = CurrentMonth.AddMonths(-1).AddDays(9);

    [Fact]
    public async Task Soll_returns_invoice_month_tax_legs_and_no_rows_in_other_month()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll);
        await MapOutputVatAccountsForReaderProofAsync(tenant);
        await CreateInvoiceAsync(
            tenant,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ]);

        await using var db = fixture.CreateAppContext(tenant);
        var reader = new RecognitionReader(db);
        var invoiceMonth = MonthRange(InvoiceDate);
        var otherMonth = MonthRange(PaymentDate);

        var actual = await reader.ReadSollAsync(
            invoiceMonth.From,
            invoiceMonth.To,
            CancellationToken.None);
        var outsidePeriod = await reader.ReadSollAsync(
            otherMonth.From,
            otherMonth.To,
            CancellationToken.None);

        SollRecognitionRow[] expected =
        [
            new("81", TaxCategory.S, 19m, PostingDirection.Credit, 100m),
            new("83", TaxCategory.S, 7m, PostingDirection.Credit, 14m),
            new("83", TaxCategory.S, 19m, PostingDirection.Credit, 19m),
            new("86", TaxCategory.S, 7m, PostingDirection.Credit, 200m),
        ];
        Assert.Equal(expected, actual);
        Assert.Empty(outsidePeriod);
    }

    [Fact]
    public async Task Cash_recognition_moves_from_invoice_month_to_payment_month()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Ist);
        var invoice = await CreateInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount, PaymentDate);

        await using var db = fixture.CreateAppContext(tenant);
        var reader = new RecognitionReader(db);
        var invoiceMonth = MonthRange(InvoiceDate);
        var paymentMonth = MonthRange(PaymentDate);

        var beforePayment = await reader.ReadCashRecognitionAsync(
            invoiceMonth.From,
            invoiceMonth.To,
            CancellationToken.None);
        var onPayment = await reader.ReadCashRecognitionAsync(
            paymentMonth.From,
            paymentMonth.To,
            CancellationToken.None);

        Assert.Empty(beforePayment);
        Assert.Equal(
            [new CashRecognitionRow(TaxCategory.S, 19m, 100m, 19m, PaymentDate)],
            onPayment);
    }

    [Fact]
    public async Task Cash_recognition_attributes_a_partial_payment_pro_rata_without_rounding()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Ist);
        var invoice = await CreateInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);
        const decimal halfGross = 59.5m;
        await RecordPaymentAsync(tenant, invoice.OpenItemId, halfGross, PaymentDate);

        await using var db = fixture.CreateAppContext(tenant);
        var paymentMonth = MonthRange(PaymentDate);
        var actual = await new RecognitionReader(db).ReadCashRecognitionAsync(
            paymentMonth.From,
            paymentMonth.To,
            CancellationToken.None);

        Assert.Equal(
            [new CashRecognitionRow(TaxCategory.S, 19m, 50m, 9.5m, PaymentDate)],
            actual);
    }

    [Fact]
    public async Task Cash_recognition_reversal_nets_out_in_its_own_value_date_month()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Ist);
        var invoice = await CreateInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);
        var paymentId = await RecordPaymentAsync(
            tenant,
            invoice.OpenItemId,
            invoice.GrossAmount,
            PaymentDate);

        Guid reversalId;
        await using (var reverseDb = fixture.CreateAppContext(tenant))
        {
            var reversed = await CreatePaymentService(reverseDb, tenant).ReverseAsync(paymentId);
            Assert.Equal(PaymentOperationStatus.Success, reversed.Status);
            reversalId = Assert.IsType<Guid>(reversed.PaymentId);
        }

        await using var db = fixture.CreateAppContext(tenant);
        var reversal = await db.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.Id == reversalId)
            .Select(payment => new
            {
                payment.Amount,
                payment.ValueDate,
                payment.ReversesPaymentId,
            })
            .SingleAsync();
        Assert.Equal(-119m, reversal.Amount);
        Assert.Equal(paymentId, reversal.ReversesPaymentId);
        var reversalMonth = MonthRange(reversal.ValueDate);

        var actual = await new RecognitionReader(db).ReadCashRecognitionAsync(
            reversalMonth.From,
            reversalMonth.To,
            CancellationToken.None);

        Assert.Equal(
            [new CashRecognitionRow(TaxCategory.S, 19m, -100m, -19m, reversal.ValueDate)],
            actual);
    }

    private async Task<Guid> SetupTenantAsync(Besteuerungsart besteuerungsart)
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");

        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
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

    private async Task MapOutputVatAccountsForReaderProofAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var outputVatAccounts = await db.Set<Account>()
            .Where(account => account.Number == "1776" || account.Number == "1771")
            .ToListAsync();
        Assert.Equal(2, outputVatAccounts.Count);

        // The minimal Phase-10 chart leaves the output-tax accounts unmapped because
        // Plan 02 computes Kz 83. This reader-focused arrangement assigns a real Kz so
        // the Soll golden rows also prove that output-VAT posting legs are grouped.
        foreach (var account in outputVatAccounts)
        {
            account.UstvaKennziffer = "83";
        }

        await db.SaveChangesAsync();
    }

    private async Task<(Guid DocumentId, Guid OpenItemId, decimal GrossAmount)> CreateInvoiceAsync(
        Guid tenant,
        IReadOnlyList<SalesTestData.LineSpec> lines)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            lines,
            InvoiceDate);
        await using (var finalizeDb = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(finalizeDb, documentId);
        }

        await using var db = fixture.CreateAppContext(tenant);
        var openItem = await db.Set<OpenItem>()
            .AsNoTracking()
            .SingleAsync(item => item.DocumentId == documentId);
        return (documentId, openItem.Id, openItem.OriginalAmount);
    }

    private async Task<Guid> RecordPaymentAsync(
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
                "REPORT-RECOGNITION",
                [new PaymentAllocationInput(openItemId, amount)]));
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        return Assert.IsType<Guid>(result.PaymentId);
    }

    private static PaymentService CreatePaymentService(NumeraDbContext db, Guid tenant)
    {
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        return new PaymentService(db, currentTenant, new NoOpAuditWriter());
    }

    private static DateOnly MonthStart()
    {
        var now = DateTime.UtcNow;
        return new DateOnly(now.Year, now.Month, 1);
    }

    private static (DateOnly From, DateOnly To) MonthRange(DateOnly date)
    {
        var from = new DateOnly(date.Year, date.Month, 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }
}
