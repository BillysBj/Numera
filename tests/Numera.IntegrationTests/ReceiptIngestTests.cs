using System.Text;
using System.Text.Json;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres coverage for Tier-B capture/extraction and Tier-A convergence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceiptIngestTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Benign_pdf_is_archived_byte_for_byte_captured_and_enqueued()
    {
        var tenant = Guid.CreateVersion7();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nNumera receipt fixture\n%%EOF");
        var jobs = new RecordingJobClient();

        var result = await IngestAsync(tenant, bytes, jobs);

        Assert.Equal(ReceiptStatus.Captured, result.Status);
        Assert.Equal(1, jobs.CreateCalls);
        Assert.Equal(typeof(ExtractReceiptJob), jobs.LastJob!.Type);
        Assert.Equal(tenant, jobs.LastJob.Args[0]);
        Assert.Equal(result.ReceiptId, jobs.LastJob.Args[1]);

        await using var read = fixture.CreateAppContext(tenant);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        var archive = await read.Set<ReceiptArchive>().AsNoTracking().SingleAsync();
        Assert.Equal(ReceiptStatus.Captured, receipt.Status);
        Assert.Equal(archive.Id, receipt.ArchiveId);
        Assert.Equal(receipt.Id, archive.ReceiptId);
        Assert.Equal(bytes, archive.OriginalBytes);
        Assert.Equal("application/pdf", archive.ContentType);
        Assert.Null(receipt.JournalEntryId);
    }

    [Fact]
    public async Task Extraction_job_moves_captured_receipt_to_extracted_with_stub_confidence()
    {
        var tenant = Guid.CreateVersion7();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nManual-entry receipt\n%%EOF");
        var result = await IngestAsync(tenant, bytes, new RecordingJobClient());

        await RunExtractionAsync(tenant, result.ReceiptId);

        await using var read = fixture.CreateAppContext(tenant);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        Assert.Equal(ReceiptStatus.Extracted, receipt.Status);
        Assert.Null(receipt.SupplierName);
        Assert.Null(receipt.GrossAmount);
        Assert.NotNull(receipt.FieldConfidence);
        using var confidence = JsonDocument.Parse(receipt.FieldConfidence);
        Assert.True(confidence.RootElement.TryGetProperty("supplierName", out var supplierConfidence));
        Assert.Equal(JsonValueKind.Null, supplierConfidence.ValueKind);
        Assert.True(confidence.RootElement.TryGetProperty("netAmount", out var netAmountConfidence));
        Assert.Equal(JsonValueKind.Null, netAmountConfidence.ValueKind);
        Assert.Null(receipt.JournalEntryId);
    }

    [Fact]
    public async Task Eicar_attachment_is_quarantined_without_archive_or_extraction()
    {
        var tenant = Guid.CreateVersion7();
        var eicar = Encoding.ASCII.GetBytes(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        var jobs = new RecordingJobClient();

        var result = await IngestAsync(tenant, eicar, jobs);

        Assert.True(result.Quarantined);
        Assert.Equal(0, jobs.CreateCalls);
        Assert.Contains("Schadsoftware", result.RejectReason, StringComparison.Ordinal);

        await using var read = fixture.CreateAppContext(tenant);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        Assert.Equal(ReceiptStatus.Quarantined, receipt.Status);
        Assert.Null(receipt.ArchiveId);
        Assert.Null(receipt.JournalEntryId);
        Assert.Equal(0, await read.Set<ReceiptArchive>().AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Zugferd_ingest_creates_inbound_document_and_linked_extracted_receipt()
    {
        var tenant = Guid.CreateVersion7();
        var invoiceDate = new DateOnly(2026, 8, 4);
        var documentId = await SeedAndFinalizeInvoiceAsync(tenant, invoiceDate);
        var cii = XRechnungGenerator.GenerateCiiForZugferd(
            await LoadPdfModelAsync(tenant, documentId));
        var zugferd = BuildZugferdPdf(cii);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var service = new InboundEInvoiceService(
                db,
                TenantOf(tenant),
                new AcceptedEInvoiceValidator(),
                new SupplierMatcher(),
                new NoOpAuditWriter());
            var result = await service.IngestAsync(
                zugferd, "RE-2026-00001.pdf", "application/pdf", CancellationToken.None);
            Assert.True(result.Success, result.RejectReason);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var inbound = await read.Set<InboundDocument>().AsNoTracking().SingleAsync();
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync();
        Assert.Equal(ReceiptSource.EInvoice, receipt.Source);
        Assert.Equal(ReceiptStatus.Extracted, receipt.Status);
        Assert.Equal(inbound.Id, receipt.InboundDocumentId);
        Assert.Null(receipt.ArchiveId);
        Assert.Equal(inbound.SellerName, receipt.SupplierName);
        Assert.Equal(inbound.SellerVatId, receipt.SupplierVatId);
        Assert.Equal(inbound.InvoiceNumber, receipt.InvoiceNumber);
        Assert.Equal(invoiceDate, receipt.InvoiceDate);
        Assert.Equal(100m, receipt.NetAmount);
        Assert.Equal(19m, receipt.VatAmount);
        Assert.Equal(119m, receipt.GrossAmount);
        Assert.Equal("EUR", receipt.Currency);
        Assert.Null(receipt.JournalEntryId);
    }

    private async Task<ReceiptIngestService.IngestResult> IngestAsync(
        Guid tenant,
        byte[] bytes,
        RecordingJobClient jobs)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var service = new ReceiptIngestService(
            db,
            TenantOf(tenant),
            new NoopAttachmentScanner(),
            new ReceiptDeduplicator(),
            jobs,
            new NoOpAuditWriter());
        return await service.IngestAsync(
            bytes,
            "receipt.pdf",
            "application/pdf",
            ReceiptSource.Upload,
            Guid.CreateVersion7(),
            CancellationToken.None);
    }

    private async Task RunExtractionAsync(Guid tenant, Guid receiptId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<Numera.Platform.Db.NumeraDbContext>(
            options => options.UseNpgsql(fixture.AppConnectionString));
        services.AddScoped<IReceiptExtractor, StubReceiptExtractor>();
        services.AddScoped<SupplierMatcher>();

        await using var provider = services.BuildServiceProvider();
        var job = new ExtractReceiptJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExtractReceiptJob>.Instance);
        await job.RunAsync(tenant, receiptId, CancellationToken.None);
    }

    private async Task<Guid> SeedAndFinalizeInvoiceAsync(Guid tenant, DateOnly date)
    {
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        var buyer = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            buyer.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            date);
        await using var db = fixture.CreateAppContext(tenant);
        await SalesTestData.FinalizeAsync(db, documentId);
        return documentId;
    }

    private async Task<InvoicePdfModel> LoadPdfModelAsync(Guid tenant, Guid documentId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var document = await db.Set<SalesDocument>()
            .AsNoTracking()
            .Include(d => d.Lines)
            .Include(d => d.TaxBreakdown)
            .FirstAsync(d => d.Id == documentId);
        return SnapshotReader.FromDocument(document);
    }

    private static byte[] BuildZugferdPdf(byte[] ciiXml)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var directory = Path.Combine(Path.GetTempPath(), "numera-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var basePdf = Path.Combine(directory, "base.pdf");
        var xmlPath = Path.Combine(directory, "factur-x.xml");
        var outputPdf = Path.Combine(directory, "zugferd.pdf");
        try
        {
            Document.Create(container => container.Page(page =>
            {
                page.Margin(20);
                page.Size(PageSizes.A4);
                page.Content().Text("ZUGFeRD receipt convergence fixture");
            })).GeneratePdf(basePdf);
            File.WriteAllBytes(xmlPath, ciiXml);
            DocumentOperation.LoadFile(basePdf)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    Key = "factur-x",
                    AttachmentName = "factur-x.xml",
                    FilePath = xmlPath,
                    MimeType = "text/xml",
                    Description = "Factur-X Invoice",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Source,
                })
                .Save(outputPdf);
            return File.ReadAllBytes(outputPdf);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort test fixture cleanup.
            }
        }
    }

    private static TenantContext TenantOf(Guid tenant)
    {
        var context = new TenantContext();
        context.SetTenant(tenant);
        return context;
    }

    private sealed class AcceptedEInvoiceValidator : IEInvoiceValidator
    {
        public Task<EInvoiceValidationResult> ValidateAsync(
            byte[] xml,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EInvoiceValidationResult(
                EInvoiceValidationStatus.Accepted,
                [],
                "<rep:report/>") );
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public int CreateCalls { get; private set; }

        public Job? LastJob { get; private set; }

        public string Create(Job job, IState state)
        {
            CreateCalls++;
            LastJob = job;
            return Guid.CreateVersion7().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
