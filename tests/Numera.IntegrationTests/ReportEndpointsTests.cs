using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

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

using QuestPDF.Infrastructure;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Real-Postgres proof of the report endpoint cores, including RLS-scoped filing
/// persistence. Existing endpoint suites call internal handlers directly rather than
/// booting an HTTP test host, so this suite mirrors that invocation style.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportEndpointsTests(PostgresFixture fixture)
{
    private static readonly DateOnly JanuaryDate = new(2026, 1, 12);
    private static readonly DateOnly FebruaryDate = new(2026, 2, 10);

    static ReportEndpointsTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task Ustva_review_returns_lines_taxation_type_and_festschreibung_badge()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll);
        await CreateInvoiceAsync(tenant, JanuaryDate, 100m);

        var preliminary = await ReadUstVaAsync(tenant, "01");

        Assert.Equal(Besteuerungsart.Soll, preliminary.Besteuerungsart);
        Assert.Equal(100m, Assert.Single(preliminary.Lines, line => line.Kz == "81").Bemessungsgrundlage);
        Assert.Equal(19m, preliminary.Zahllast);
        Assert.False(preliminary.IsFestgeschrieben);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var currentTenant = CurrentTenant(tenant);
            var locked = await new FestschreibungService(db, currentTenant, new NoOpAuditWriter())
                .LockPeriodAsync(2026, 1, CancellationToken.None);
            Assert.Equal(PeriodLockStatus.Success, locked.Status);
        }

        var final = await ReadUstVaAsync(tenant, "01");
        Assert.True(final.IsFestgeschrieben);
    }

    [Fact]
    public async Task Drill_down_uses_payment_value_date_for_Ist_and_journal_entry_date_for_Soll()
    {
        var istTenant = await SetupTenantAsync(Besteuerungsart.Ist);
        var istInvoice = await CreateInvoiceAsync(istTenant, JanuaryDate, 125m);
        var paymentId = await RecordPaymentAsync(
            istTenant, istInvoice.OpenItemId, istInvoice.GrossAmount, FebruaryDate);

        await using (var db = fixture.CreateAppContext(istTenant))
        {
            var calculator = Calculator(db);
            var february = Assert.IsType<UstVaDrillDownResult>(
                await ReportEndpoints.GetUstVaEntriesAsync(
                    "81", 2026, "02", db, calculator, CancellationToken.None));
            var contribution = Assert.Single(february.Entries);
            Assert.Equal("paymentValueDate", february.RecognitionBasis);
            Assert.Equal("payment", contribution.Kind);
            Assert.Equal(istInvoice.DocumentId, contribution.DocumentId);
            Assert.Equal(istInvoice.DocumentNumber, contribution.DocumentNumber);
            Assert.Equal(JanuaryDate, contribution.InvoiceDate);
            Assert.Equal(paymentId, contribution.PaymentId);
            Assert.Equal(FebruaryDate, contribution.PaymentValueDate);
            Assert.Equal(125m, contribution.AttributedNet);

            var january = Assert.IsType<UstVaDrillDownResult>(
                await ReportEndpoints.GetUstVaEntriesAsync(
                    "81", 2026, "01", db, calculator, CancellationToken.None));
            Assert.Empty(january.Entries);
        }

        var sollTenant = await SetupTenantAsync(Besteuerungsart.Soll);
        await CreateInvoiceAsync(sollTenant, JanuaryDate, 80m);
        await using (var db = fixture.CreateAppContext(sollTenant))
        {
            var january = Assert.IsType<UstVaDrillDownResult>(
                await ReportEndpoints.GetUstVaEntriesAsync(
                    "81", 2026, "01", db, Calculator(db), CancellationToken.None));
            var contribution = Assert.Single(january.Entries);
            Assert.Equal("journalEntryDate", january.RecognitionBasis);
            Assert.Equal("journal", contribution.Kind);
            Assert.Equal(JanuaryDate, contribution.EntryDate);
            Assert.NotNull(contribution.JournalEntryId);
            Assert.Null(contribution.PaymentId);
        }
    }

    [Fact]
    public async Task Euer_endpoint_core_returns_the_Anlage_Euer_model()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Ist);
        var invoice = await CreateInvoiceAsync(tenant, JanuaryDate, 100m);
        await RecordPaymentAsync(tenant, invoice.OpenItemId, invoice.GrossAmount, FebruaryDate);

        await using var db = fixture.CreateAppContext(tenant);
        var report = await ReportEndpoints.GetEuerAsync(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new EuerCalculator(db, new RecognitionReader(db)),
            CancellationToken.None);

        Assert.Equal(2026, report.Jahr);
        Assert.NotEmpty(report.Betriebseinnahmen);
        Assert.Equal(119m, report.SummeEinnahmen);
        Assert.Equal(119m, report.Gewinn);
        Assert.True(report.IsExpenseDataIncomplete);
    }

    [Fact]
    public async Task Exports_are_non_empty_and_xml_generation_is_idempotent_per_period()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll);
        await CreateInvoiceAsync(tenant, JanuaryDate, 100m);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var calculator = Calculator(db);
            var first = Assert.IsType<ReportExport>(
                await ReportEndpoints.ExportUstVaXmlAsync(
                    2026, "01", db, calculator, CancellationToken.None));
            var second = Assert.IsType<ReportExport>(
                await ReportEndpoints.ExportUstVaXmlAsync(
                    2026, "01", db, calculator, CancellationToken.None));
            var ustVaPdf = Assert.IsType<ReportExport>(
                await ReportEndpoints.ExportUstVaPdfAsync(
                    2026, "01", calculator, CancellationToken.None));
            var euerPdf = await ReportEndpoints.ExportEuerPdfAsync(
                2026,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                new EuerCalculator(db, new RecognitionReader(db)),
                CancellationToken.None);

            Assert.NotEmpty(first.Bytes);
            Assert.Equal(first.Bytes, second.Bytes);
            Assert.Contains("iso-8859-15", first.ContentType, StringComparison.OrdinalIgnoreCase);
            AssertPdf(ustVaPdf.Bytes);
            AssertPdf(euerPdf.Bytes);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var filing = Assert.Single(await read.Set<UstVaFiling>().AsNoTracking().ToListAsync());
        Assert.Equal(UstVaFilingStatus.Draft, filing.Status);
        Assert.NotEmpty(filing.XmlBytes!);
        Assert.Equal("01", filing.Zeitraum);
    }

    [Fact]
    public async Task Submitted_filing_rejects_update_and_delete_in_the_database()
    {
        var tenant = await SetupTenantAsync(Besteuerungsart.Soll);
        var filing = new UstVaFiling
        {
            TenantId = tenant,
            Jahr = 2026,
            Zeitraum = "01",
            Besteuerungsart = Besteuerungsart.Soll,
            KzSnapshotJson = "[]",
            Zahllast = 19m,
            XmlBytes = [1, 2, 3],
            Status = UstVaFilingStatus.Submitted,
            CreatedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        await using (var insert = fixture.CreateAppContext(tenant))
        {
            insert.Add(filing);
            await insert.SaveChangesAsync();
        }

        await using (var update = fixture.CreateAppContext(tenant))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                update.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE ust_va_filing SET zahllast = 0 WHERE id = {filing.Id}"));
            AssertSubmittedImmutable(ex);
        }

        await using (var delete = fixture.CreateAppContext(tenant))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                delete.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM ust_va_filing WHERE id = {filing.Id}"));
            AssertSubmittedImmutable(ex);
        }
    }

    [Fact]
    public async Task Kleinunternehmer_review_is_gated_and_never_persists_a_filing()
    {
        var tenant = await SetupTenantAsync(
            Besteuerungsart.Soll,
            isKleinunternehmer: true);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var calculator = Calculator(db);
            var report = await ReportEndpoints.GetUstVaAsync(
                2026, "01", calculator, CancellationToken.None);
            var export = await ReportEndpoints.ExportUstVaXmlAsync(
                2026, "01", db, calculator, CancellationToken.None);

            Assert.True(report.IsKleinunternehmer);
            Assert.Empty(report.Lines);
            Assert.Null(export);
        }

        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(0, await read.Set<UstVaFiling>().CountAsync());
    }

    private async Task<Guid> SetupTenantAsync(
        Besteuerungsart besteuerungsart,
        bool isKleinunternehmer = false)
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(
            fixture,
            tenant,
            kleinunternehmer: isKleinunternehmer);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");

        await using var db = fixture.CreateAppContext(tenant);
        var profile = await db.Set<CompanyProfile>().SingleAsync();
        profile.VatId = null;
        profile.TaxNumber = "151/815/08154";
        profile.Bundesland = Bundesland.Bayern;
        await db.SaveChangesAsync();

        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                besteuerungsart,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            CurrentTenant(tenant),
            CancellationToken.None);
        Assert.Equal(
            StatusCodes.Status201Created,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        return tenant;
    }

    private async Task<(Guid DocumentId, Guid OpenItemId, decimal GrossAmount, string DocumentNumber)>
        CreateInvoiceAsync(Guid tenant, DateOnly documentDate, decimal netAmount)
    {
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, netAmount, TaxCategory.S, 19m)],
            documentDate);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var document = await read.Set<SalesDocument>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == documentId);
        var openItem = await read.Set<OpenItem>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.DocumentId == documentId);
        return (documentId, openItem.Id, openItem.OriginalAmount, document.DocumentNumber!);
    }

    private async Task<Guid> RecordPaymentAsync(
        Guid tenant,
        Guid openItemId,
        decimal amount,
        DateOnly valueDate)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var result = await new PaymentService(
            db,
            CurrentTenant(tenant),
            new NoOpAuditWriter(),
            new PostingEngine(db),
            new AccountResolver(db),
            NullLogger<PaymentService>.Instance)
            .RecordAsync(new RecordPaymentRequest(
                amount,
                valueDate,
                PaymentMethod.BankTransfer,
                "REPORT-ENDPOINT-TEST",
                [new PaymentAllocationInput(openItemId, amount)]));
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        return Assert.IsType<Guid>(result.PaymentId);
    }

    private async Task<UstVaReport> ReadUstVaAsync(Guid tenant, string zeitraum)
    {
        await using var db = fixture.CreateAppContext(tenant);
        return await ReportEndpoints.GetUstVaAsync(
            2026, zeitraum, Calculator(db), CancellationToken.None);
    }

    private static UstVaCalculator Calculator(NumeraDbContext db) =>
        new(db, new RecognitionReader(db));

    private static TenantContext CurrentTenant(Guid tenant)
    {
        var current = new TenantContext();
        current.SetTenant(tenant);
        return current;
    }

    private static void AssertSubmittedImmutable(PostgresException exception)
    {
        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains(
            "submitted ust_va_filing rows are append-only",
            exception.MessageText,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPdf(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length >= 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
