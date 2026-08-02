using System.IO.Compression;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// DSGVO Art. 20 tenant-data export proofs (09-01, criterion 1) on real postgres:18. Exercises
/// <see cref="TenantExportService"/> directly (the endpoint's Owner-only + TaxAdvisor-deny is the
/// RequireOwner policy + the 08-01 read-only write-guard, both HTTP middleware proven there). Covers
/// completeness (JSON per table + byte-exact blobs), cross-tenant absence (RLS — also criterion-4
/// evidence), service non-mutation, and the DataExport tarif gate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantExportTests(PostgresFixture fixture)
{
    private static readonly DateOnly Date = new(2026, 6, 1);

    private sealed record SeededBlobs(
        Guid PartnerId,
        string DocumentNumber,
        byte[] Logo,
        byte[] RenderPdf,
        byte[] EInvoiceXml,
        byte[] InboundBytes,
        byte[] CustomerFileBytes);

    // Seeds a fully-populated tenant: profile (+logo), partner, one finalized invoice, and one blob
    // of every kind. `tag` makes each blob's bytes unique per tenant so cross-tenant leakage is
    // detectable byte-for-byte.
    private async Task<SeededBlobs> SeedFullTenantAsync(Guid tenant, string tag)
    {
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant, netDays: 30);
        var docId = await SalesTestData.SeedDraftAsync(
            fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            Date);

        var logo = Encoding.UTF8.GetBytes($"PNG-logo-{tag}");
        var renderPdf = Encoding.UTF8.GetBytes($"%PDF-1.4 render {tag}");
        var einvoiceXml = Encoding.UTF8.GetBytes($"<Invoice>{tag}</Invoice>");
        var inboundBytes = Encoding.UTF8.GetBytes($"<InboundInvoice>{tag}</InboundInvoice>");
        var customerBytes = Encoding.UTF8.GetBytes($"customer-file-{tag}");

        string docNumber;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        await using (var db = fixture.CreateAppContext(tenant))
        {
            docNumber = await db.Set<SalesDocument>().AsNoTracking()
                .Where(d => d.Id == docId).Select(d => d.DocumentNumber!).FirstAsync();

            var profile = await db.Set<CompanyProfile>().FirstAsync();
            profile.LogoBytes = logo;
            profile.LogoContentType = "image/png";

            db.Set<DocumentRender>().Add(new DocumentRender
            {
                TenantId = tenant,
                DocumentId = docId,
                PdfBytes = renderPdf,
                DocumentNumber = docNumber,
                Language = "de",
                ByteSize = renderPdf.LongLength,
                RenderedAt = DateTimeOffset.UtcNow,
            });

            db.Set<EInvoiceArtifact>().Add(new EInvoiceArtifact
            {
                TenantId = tenant,
                DocumentId = docId,
                Format = EInvoiceFormat.XRechnungUbl,
                Xml = einvoiceXml,
                DocumentNumber = docNumber,
                ValidationStatus = EInvoiceValidationStatus.Accepted,
                ByteSize = einvoiceXml.LongLength,
                GeneratedAt = DateTimeOffset.UtcNow,
            });

            db.Set<InboundDocument>().Add(new InboundDocument
            {
                TenantId = tenant,
                OriginalBytes = inboundBytes,
                OriginalFileName = $"inbound-{tag}.xml",
                OriginalContentType = "application/xml",
                ByteSize = inboundBytes.LongLength,
                UploadedAt = DateTimeOffset.UtcNow,
            });

            db.Set<CustomerFile>().Add(new CustomerFile
            {
                TenantId = tenant,
                PartnerId = partner.Id,
                Bytes = customerBytes,
                FileName = $"akte-{tag}.pdf",
                ContentType = "application/pdf",
                ByteSize = customerBytes.LongLength,
                UploadedByUserId = Guid.CreateVersion7(),
            });

            await db.SaveChangesAsync();
        }

        return new SeededBlobs(partner.Id, docNumber, logo, renderPdf, einvoiceXml, inboundBytes, customerBytes);
    }

    private async Task<byte[]> ExportAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var service = new TenantExportService(db);
        using var ms = new MemoryStream();
        await service.WriteArchiveAsync(ms, CancellationToken.None);
        return ms.ToArray();
    }

    private static byte[] ReadEntry(ZipArchive zip, Func<ZipArchiveEntry, bool> match)
    {
        var entry = zip.Entries.FirstOrDefault(match)
            ?? throw new Xunit.Sdk.XunitException("Expected archive entry not found.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Export_contains_manifest_json_per_table_and_byte_exact_blobs()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedFullTenantAsync(tenant, "A");

        using var zip = new ZipArchive(new MemoryStream(await ExportAsync(tenant)), ZipArchiveMode.Read);

        // Structure: manifest + README + at least one data/{table}.json.
        Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(zip.Entries, e => e.FullName == "README.txt");
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("data/", StringComparison.Ordinal)
            && e.FullName.EndsWith(".json", StringComparison.Ordinal));

        // README stays honest (no certification claim).
        var readme = Encoding.UTF8.GetString(ReadEntry(zip, e => e.FullName == "README.txt"));
        Assert.DoesNotContain("zertifiziert", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certified", readme, StringComparison.OrdinalIgnoreCase);

        // The finalized invoice's row is present in the data JSON (proves sales_documents exported).
        var allData = string.Concat(zip.Entries
            .Where(e => e.FullName.StartsWith("data/", StringComparison.Ordinal))
            .Select(e => Encoding.UTF8.GetString(ReadEntry(zip, x => x.FullName == e.FullName))));
        Assert.Contains(seeded.DocumentNumber, allData, StringComparison.Ordinal);

        // Every blob is present under files/ with BYTE-EXACT content.
        Assert.Equal(seeded.RenderPdf, ReadEntry(zip, e => e.FullName.StartsWith("files/renders/", StringComparison.Ordinal)));
        Assert.Equal(seeded.EInvoiceXml, ReadEntry(zip, e => e.FullName.StartsWith("files/einvoice/", StringComparison.Ordinal)));
        Assert.Equal(seeded.InboundBytes, ReadEntry(zip, e => e.FullName.StartsWith("files/inbound/", StringComparison.Ordinal)));
        Assert.Equal(seeded.CustomerFileBytes, ReadEntry(zip, e => e.FullName.StartsWith("files/customer-files/", StringComparison.Ordinal)));
        Assert.Equal(seeded.Logo, ReadEntry(zip, e => e.FullName.StartsWith("files/company/logo", StringComparison.Ordinal)));

        // Blob bytes must NOT be duplicated into the JSON (byte[] properties are dropped).
        Assert.DoesNotContain(Convert.ToBase64String(seeded.CustomerFileBytes), allData, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_excludes_a_second_tenants_rows_and_bytes()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedFullTenantAsync(tenantA, "A");
        var b = await SeedFullTenantAsync(tenantB, "B");

        using var zip = new ZipArchive(new MemoryStream(await ExportAsync(tenantA)), ZipArchiveMode.Read);

        // No tenant-B row identifier appears anywhere in tenant A's JSON. The partner id is a
        // tenant-unique GUID (document numbers are NOT — each tenant's sequence starts at 00001).
        var allData = string.Concat(zip.Entries
            .Where(e => e.FullName.StartsWith("data/", StringComparison.Ordinal))
            .Select(e => Encoding.UTF8.GetString(ReadEntry(zip, x => x.FullName == e.FullName))));
        Assert.DoesNotContain(b.PartnerId.ToString(), allData, StringComparison.OrdinalIgnoreCase);

        // No tenant-B blob bytes appear under files/.
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("files/", StringComparison.Ordinal)))
        {
            var bytes = ReadEntry(zip, x => x.FullName == entry.FullName);
            Assert.NotEqual(b.CustomerFileBytes, bytes);
            Assert.NotEqual(b.InboundBytes, bytes);
            Assert.NotEqual(b.RenderPdf, bytes);
            Assert.NotEqual(b.EInvoiceXml, bytes);
            Assert.NotEqual(b.Logo, bytes);
        }
    }

    [Fact]
    public async Task Export_mutates_no_business_data()
    {
        var tenant = Guid.CreateVersion7();
        await SeedFullTenantAsync(tenant, "A");

        async Task<(int Docs, int Partners, int Audit)> CountsAsync()
        {
            await using var db = fixture.CreateAppContext(tenant);
            return (
                await db.Set<SalesDocument>().AsNoTracking().CountAsync(),
                await db.Set<BusinessPartner>().AsNoTracking().CountAsync(),
                await db.Set<AuditEvent>().AsNoTracking().CountAsync());
        }

        var before = await CountsAsync();
        _ = await ExportAsync(tenant);
        var after = await CountsAsync();

        // The service itself opens no write transaction — nothing changes, not even audit (the single
        // data.exported row is appended by ExportEndpoints, asserted below).
        Assert.Equal(before, after);
        Assert.Equal("data.exported", new ExportAuditEvent().Action);
    }

    [Fact]
    public async Task DataExport_gate_denies_without_the_capability_and_403s()
    {
        Assert.True(await ExportEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.DataExport), CancellationToken.None));
        Assert.False(await ExportEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.EInvoicing), CancellationToken.None));
        Assert.False(await ExportEndpoints.HasCapabilityAsync(
            FakeEntitlementService.Denying, CancellationToken.None));

        var result = Assert.IsType<ProblemHttpResult>(ExportEndpoints.UpgradeRequired());
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }
}
