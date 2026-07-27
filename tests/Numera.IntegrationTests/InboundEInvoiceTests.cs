using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using Xunit;

using CrmAddress = Numera.Modules.Crm.Address;

namespace Numera.IntegrationTests;

/// <summary>
/// The Phase-5 inbound e-invoice hard gate (EINV-04/EINV-05) on real postgres:18 as the
/// non-BYPASSRLS <c>numera_app</c> role. Proves the receive path: an uploaded XRechnung XML (and a
/// ZUGFeRD PDF) is detected/extracted/parsed via the SAME ZUGFeRD-csharp library used outbound,
/// stored with its ORIGINAL bytes IMMUTABLE + a human-readable read-model in <c>inbound_document</c>
/// under RLS, its seller matched to a <see cref="BusinessPartner"/> by VAT id, and the whole thing
/// isolated per tenant.
/// </summary>
/// <remarks>
/// The validator is a deterministic FAKE (<see cref="FakeEInvoiceValidator"/>) — the real KoSIT
/// report parsing is golden-tested in 05-02 and the live conformance of generated XRechnung is
/// proven by <see cref="KoSitConformanceTests"/>. Realistic e-invoice bytes come from the OUTBOUND
/// path (05-01 <see cref="XRechnungGenerator"/> over a finalized fixture), so ingest parses our own
/// output — the single-library in/out guarantee. The ZUGFeRD PDF fixture embeds that CII via
/// QuestPDF's <c>DocumentOperation</c> (the same attach mechanism 05-04 ships), proving the
/// PdfPig embedded-XML extraction path without depending on 05-04's generator.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class InboundEInvoiceTests
{
    // The issuer VatId SalesTestData.SeedProfileAsync freezes onto every finalized doc → the seller
    // VatId in the generated XRechnung, and thus the supplier-match key on ingest.
    private const string SellerVatId = "DE811907980";

    private static readonly System.Text.Json.JsonSerializerOptions Web =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly PostgresFixture _fixture;

    public InboundEInvoiceTests(PostgresFixture fixture) => _fixture = fixture;

    // -------------------------------------------------- (1)+(2)+(3-match) XML ingest round-trip
    [Fact]
    public async Task Ingest_xrechnung_xml_stores_one_immutable_row_round_trips_and_matches_supplier_by_vat_id()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        var docId = await SeedAndFinalizeRechnungAsync(tenant, date);
        var xml = await GenerateUblAsync(tenant, docId);

        // A supplier whose VatId equals the (issuer) seller VatId → the ingest must match it.
        var supplier = await SeedSupplierAsync(tenant, SellerVatId, "Aussteller GmbH (Lieferant)");

        var result = await IngestAsync(tenant, xml, "RE-2026-00001-ubl.xml", "application/xml");
        Assert.True(result.Success);

        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var rows = await read.Set<InboundDocument>().AsNoTracking().ToListAsync();
            var row = Assert.Single(rows);

            // (1) immutable original byte-for-byte + a stored read-model + the fake verdict.
            Assert.Equal(xml, row.OriginalBytes);
            Assert.Equal(xml.LongLength, row.ByteSize);
            Assert.Equal(InboundFormat.XmlUbl, row.DetectedFormat);
            Assert.NotNull(row.ReadModel);
            Assert.Equal(EInvoiceValidationStatus.Accepted, row.ValidationStatus);

            // (2) round-trip parse: the read-model's seller/number/totalGross match the source.
            Assert.Equal("RE-2026-00001", row.InvoiceNumber);
            Assert.Equal(SellerVatId, row.SellerVatId);
            Assert.Equal(333.00m, row.TotalGross);

            var rm = System.Text.Json.JsonSerializer.Deserialize<InboundReadModel>(row.ReadModel!, Web);
            Assert.NotNull(rm);
            Assert.Equal(SellerVatId, rm!.Seller.VatId);
            Assert.Equal("RE-2026-00001", rm.InvoiceNumber);
            Assert.Equal(333.00m, rm.TotalGross);
            Assert.NotEmpty(rm.Lines);
            Assert.NotEmpty(rm.BreakdownRows);

            // (3) supplier matched by VAT id.
            Assert.Equal(supplier.Id, row.MatchedPartnerId);
        }

        // RLS: a second tenant sees NONE of these rows even with the EF filter off.
        await using (var other = _fixture.CreateAppContext(otherTenant))
        {
            Assert.Equal(0, await other.Set<InboundDocument>().IgnoreQueryFilters().CountAsync());
        }
    }

    // -------------------------------------------------- (3-no-match) unmatched supplier → null
    [Fact]
    public async Task Ingest_with_no_matching_supplier_records_a_null_matched_partner()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        var docId = await SeedAndFinalizeRechnungAsync(tenant, date);
        var xml = await GenerateUblAsync(tenant, docId);

        // No partner with the seller's VAT id nor name exists (only the seeded recipient customer,
        // whose VatId differs), so the match must be null ("nicht zugeordnet").
        var result = await IngestAsync(tenant, xml, "RE-2026-00001-ubl.xml", "application/xml");
        Assert.True(result.Success);

        await using var read = _fixture.CreateAppContext(tenant);
        var row = await read.Set<InboundDocument>().AsNoTracking().SingleAsync();
        Assert.Null(row.MatchedPartnerId);
    }

    // -------------------------------------------------- (4) ZUGFeRD PDF embedded-XML extraction
    [Fact]
    public async Task Ingest_zugferd_pdf_extracts_and_parses_the_embedded_factur_x_xml()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        var docId = await SeedAndFinalizeRechnungAsync(tenant, date);
        var cii = await GenerateCiiAsync(tenant, docId);
        var pdf = BuildZugferdPdf(cii);

        var result = await IngestAsync(tenant, pdf, "RE-2026-00001.pdf", "application/pdf");
        Assert.True(result.Success, $"ingest failed: {result.RejectReason} (pdf {pdf.Length} bytes)");

        await using var read = _fixture.CreateAppContext(tenant);
        var row = await read.Set<InboundDocument>().AsNoTracking().SingleAsync();

        // The PDF was recognized and its embedded factur-x.xml extracted + parsed (same seller/number/total).
        Assert.Equal(InboundFormat.ZugferdPdf, row.DetectedFormat);
        Assert.Equal(pdf, row.OriginalBytes); // the ORIGINAL PDF is stored, not the extracted XML
        Assert.Equal("RE-2026-00001", row.InvoiceNumber);
        Assert.Equal(SellerVatId, row.SellerVatId);
        Assert.Equal(333.00m, row.TotalGross);
    }

    // -------------------------------------------------- a non-e-invoice is refused (no row)
    [Fact]
    public async Task Ingest_a_non_e_invoice_pdf_is_rejected_and_writes_no_row()
    {
        var tenant = Guid.CreateVersion7();

        // A plain PDF with no embedded e-invoice XML is not an e-invoice.
        var plainPdf = BuildPlainPdf();

        var result = await IngestAsync(tenant, plainPdf, "scan.pdf", "application/pdf");
        Assert.False(result.Success);
        Assert.NotNull(result.RejectReason);

        await using var read = _fixture.CreateAppContext(tenant);
        Assert.Equal(0, await read.Set<InboundDocument>().AsNoTracking().CountAsync());
    }

    // ------------------------------------------------------------- helpers

    private async Task<InboundEInvoiceService.IngestResult> IngestAsync(
        Guid tenant, byte[] bytes, string fileName, string contentType)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var svc = new InboundEInvoiceService(
            db,
            TenantOf(tenant),
            new FakeEInvoiceValidator { Status = EInvoiceValidationStatus.Accepted },
            new SupplierMatcher(),
            new NoOpAuditWriter());
        return await svc.IngestAsync(bytes, fileName, contentType, CancellationToken.None);
    }

    private async Task<Guid> SeedAndFinalizeRechnungAsync(Guid tenant, DateOnly date)
    {
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ],
            date);

        await using var db = _fixture.CreateAppContext(tenant);
        await SalesTestData.FinalizeAsync(db, docId);
        return docId;
    }

    private async Task<BusinessPartner> SeedSupplierAsync(Guid tenant, string vatId, string name)
    {
        var supplier = new BusinessPartner
        {
            TenantId = tenant,
            Name = name,
            IsSupplier = true,
            VatId = vatId,
            BillingAddress = new CrmAddress
            {
                Street = "Lieferantenstr. 1",
                PostalCode = "10115",
                City = "Berlin",
                CountryCode = "DE",
            },
        };

        await using var db = _fixture.CreateAppContext(tenant);
        db.Set<BusinessPartner>().Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    private async Task<byte[]> GenerateUblAsync(Guid tenant, Guid docId) =>
        XRechnungGenerator.GenerateUbl(await LoadModelAsync(tenant, docId));

    private async Task<byte[]> GenerateCiiAsync(Guid tenant, Guid docId) =>
        XRechnungGenerator.GenerateCiiForZugferd(await LoadModelAsync(tenant, docId));

    private async Task<InvoicePdfModel> LoadModelAsync(Guid tenant, Guid docId)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var doc = await db.Set<SalesDocument>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.TaxBreakdown)
            .FirstAsync(x => x.Id == docId);
        return SnapshotReader.FromDocument(doc);
    }

    // Builds a ZUGFeRD-style PDF: a trivial base PDF with the CII XML embedded as factur-x.xml via
    // QuestPDF's DocumentOperation (the AF/name-tree mechanism PdfPig reads). Temp files are used
    // because DocumentOperation is file-path based; both are cleaned up.
    private static byte[] BuildZugferdPdf(byte[] ciiXml)
    {
        EnsureQuestPdfLicense();

        var dir = Path.Combine(Path.GetTempPath(), "numera-inbound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var basePdf = Path.Combine(dir, "base.pdf");
        var xmlPath = Path.Combine(dir, "factur-x.xml");
        var outPdf = Path.Combine(dir, "zugferd.pdf");
        try
        {
            Document.Create(c => c.Page(p =>
            {
                p.Margin(20);
                p.Size(PageSizes.A4);
                p.Content().Text("ZUGFeRD test invoice");
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
                .Save(outPdf);

            return File.ReadAllBytes(outPdf);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static byte[] BuildPlainPdf()
    {
        EnsureQuestPdfLicense();
        return Document.Create(c => c.Page(p =>
        {
            p.Margin(20);
            p.Size(PageSizes.A4);
            p.Content().Text("Just a scanned document, no e-invoice here.");
        })).GeneratePdf();
    }

    private static void EnsureQuestPdfLicense() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static TenantContext TenantOf(Guid tenant)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant);
        return ctx;
    }

    /// <summary>A deterministic <see cref="IEInvoiceValidator"/> returning a configured verdict.</summary>
    private sealed class FakeEInvoiceValidator : IEInvoiceValidator
    {
        public EInvoiceValidationStatus Status { get; init; } = EInvoiceValidationStatus.Accepted;

        public Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EInvoiceValidationResult(
                Status,
                [],
                Status == EInvoiceValidationStatus.Unavailable ? null : "<rep:report/>"));
    }
}
