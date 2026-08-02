using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Db;

namespace Numera.Api.Services;

/// <summary>
/// Streams the ENTIRE dataset of the current tenant (DSGVO Art. 20 / PLAT-08) as a
/// <see cref="ZipArchive"/>: one <c>data/{table}.json</c> file per RLS-scoped table plus the
/// real binary blobs (rendered PDFs, e-invoice XML, inbound originals, customer files, logo)
/// under <c>files/</c>. The archive is produced entry-by-entry directly onto a caller-supplied
/// <see cref="Stream"/> so peak memory stays bounded — the whole archive is NEVER materialized
/// as a <c>byte[]</c>, and blob rows are streamed one at a time.
/// </summary>
/// <remarks>
/// <para>
/// Every query is <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/> and the
/// service opens NO write transaction — the export is non-mutating. RLS is already active on the
/// request connection (the tenant middleware set <c>app.current_tenant</c>), so every query is
/// tenant-scoped automatically; there are no manual <c>WHERE tenant_id =</c> filters and a second
/// tenant's rows/bytes can never appear in the archive.
/// </para>
/// <para>
/// The set of exported tables is derived reflectively from the EF model (every non-owned mapped
/// entity type — which is every RLS-scoped table plus the self-scoped <c>tenants</c> row), so a
/// future table is exported automatically. Blob bytes are written straight into <c>files/</c>
/// entries and are dropped from the JSON via a type-info modifier (NEVER base64-inlined), so the
/// bytes are stored exactly once.
/// </para>
/// </remarks>
public sealed class TenantExportService
{
    /// <summary>Bumped when the archive layout changes; written into <c>manifest.json</c>.</summary>
    public const int SchemaVersion = 1;

    private static readonly MethodInfo CountRowsMethod = typeof(TenantExportService)
        .GetMethod(nameof(CountRowsAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo WriteTableMethod = typeof(TenantExportService)
        .GetMethod(nameof(WriteTableJsonAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Web defaults + string enums + cycle-safety; a modifier strips every byte[] property so blob
    // bytes never land in the JSON (they live under files/ instead).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropByteArrayProperties },
        },
    };

    private readonly NumeraDbContext _db;

    /// <summary>Creates the export service over the caller's tenant-scoped context.</summary>
    public TenantExportService(NumeraDbContext db) => _db = db;

    /// <summary>
    /// Writes the tenant's complete export archive onto <paramref name="output"/>. The stream is
    /// left open (<c>leaveOpen: true</c>) so the caller (the HTTP response body) owns its lifetime.
    /// </summary>
    public async Task WriteArchiveAsync(Stream output, CancellationToken ct)
    {
        // The exported tables: every non-owned mapped entity type (each is RLS-scoped, plus the
        // self-scoped tenants row). Ordered by table name for a stable, readable archive.
        var tables = _db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.GetTableName() is not null)
            .Select(t => (Table: t.GetTableName()!, Clr: t.ClrType))
            // Distinct table name (guards against any shared-table mapping).
            .GroupBy(t => t.Table, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => t.Table, StringComparer.Ordinal)
            .ToList();

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Row counts up front (cheap COUNT(*) — no blobs loaded) for the manifest.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (table, clr) in tables)
        {
            var count = await ((Task<int>)CountRowsMethod
                .MakeGenericMethod(clr)
                .Invoke(this, [ct])!).ConfigureAwait(false);
            counts[table] = count;
        }

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteManifestAsync(archive, tenant, counts, ct).ConfigureAwait(false);
        await WriteReadmeAsync(archive, ct).ConfigureAwait(false);

        // One data/{table}.json per table, streamed row-by-row (bounded memory).
        foreach (var (table, clr) in tables)
        {
            await ((Task)WriteTableMethod
                .MakeGenericMethod(clr)
                .Invoke(this, [archive, table, ct])!).ConfigureAwait(false);
        }

        // The real binary blobs, one entry per row, streamed one row at a time.
        await WriteBlobFilesAsync(archive, ct).ConfigureAwait(false);
    }

    private async Task WriteManifestAsync(
        ZipArchive archive,
        Numera.Platform.Db.Entities.Tenant? tenant,
        IReadOnlyDictionary<string, int> counts,
        CancellationToken ct)
    {
        var manifest = new
        {
            schemaVersion = SchemaVersion,
            exportedAtUtc = DateTimeOffset.UtcNow,
            tenantId = tenant?.Id,
            tenantName = tenant?.Name,
            plan = tenant?.Plan.ToString(),
            rowCounts = counts,
        };

        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct).ConfigureAwait(false);
    }

    private static async Task WriteReadmeAsync(ZipArchive archive, CancellationToken ct)
    {
        // WORDING (locked 09-CONTEXT): "GoBD-konform"/"DSGVO-konform" only — NEVER
        // "zertifiziert"/"certified".
        const string readme =
            """
            Numera Datenexport
            ==================

            Dieses Archiv enthält alle Daten Ihres Mandanten in einem strukturierten,
            maschinenlesbaren und gängigen Format (DSGVO-konform, Art. 20 DSGVO —
            Recht auf Datenübertragbarkeit).

            Aufbau des Archivs:
            - manifest.json       Exportzeitpunkt, Mandant, Schema-Version und Zeilenzahl je Tabelle.
            - data/<tabelle>.json Ein JSON-Array je Tabelle mit allen Datensätzen Ihres Mandanten.
            - files/              Die zugehörigen Originaldateien (Rechnungs-PDFs, E-Rechnungs-XML,
                                  eingehende Belege, Kundenakte-Dateien, Firmenlogo) im Original.

            Die Ablage der Belege und der Prüfpfad sind GoBD-konform gestaltet. Dieser Export
            ist eine Kopie Ihrer Daten und verändert nichts an den gespeicherten Originaldaten.
            """;

        var entry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(readme);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    // Generic per-table helpers invoked reflectively so the concrete entity type drives an
    // EF-native, typed, streaming query (no Cast<object> that EF may refuse to translate).
    private Task<int> CountRowsAsync<T>(CancellationToken ct)
        where T : class
        => _db.Set<T>().AsNoTracking().CountAsync(ct);

    private async Task WriteTableJsonAsync<T>(ZipArchive archive, string table, CancellationToken ct)
        where T : class
    {
        var entry = archive.CreateEntry($"data/{table}.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        // IAsyncEnumerable => System.Text.Json streams element-by-element; only one row is ever
        // materialized at a time (byte[] columns are dropped by the type-info modifier).
        var rows = _db.Set<T>().AsNoTracking().AsAsyncEnumerable();
        await JsonSerializer.SerializeAsync(stream, rows, JsonOptions, ct).ConfigureAwait(false);
    }

    private async Task WriteBlobFilesAsync(ZipArchive archive, CancellationToken ct)
    {
        // document_render -> files/renders/{documentNumber}.pdf
        await foreach (var r in _db.Set<DocumentRender>().AsNoTracking().AsAsyncEnumerable()
            .WithCancellation(ct).ConfigureAwait(false))
        {
            await WriteBlobAsync(
                archive,
                $"files/renders/{Safe(NonEmpty(r.DocumentNumber, r.Id))}.pdf",
                r.PdfBytes,
                ct).ConfigureAwait(false);
        }

        // document_einvoice -> files/einvoice/{documentNumber}-{format}.{xml|pdf}
        await foreach (var a in _db.Set<EInvoiceArtifact>().AsNoTracking().AsAsyncEnumerable()
            .WithCancellation(ct).ConfigureAwait(false))
        {
            var ext = a.Format == EInvoiceFormat.ZugferdPdfA3 ? "pdf" : "xml";
            await WriteBlobAsync(
                archive,
                $"files/einvoice/{Safe(NonEmpty(a.DocumentNumber, a.Id))}-{a.Format}.{ext}",
                a.Xml,
                ct).ConfigureAwait(false);
        }

        // inbound_document -> files/inbound/{id}-{originalName}
        await foreach (var i in _db.Set<InboundDocument>().AsNoTracking().AsAsyncEnumerable()
            .WithCancellation(ct).ConfigureAwait(false))
        {
            await WriteBlobAsync(
                archive,
                $"files/inbound/{i.Id}-{Safe(NonEmpty(i.OriginalFileName, i.Id))}",
                i.OriginalBytes,
                ct).ConfigureAwait(false);
        }

        // customer_files -> files/customer-files/{partnerId}/{fileName}
        await foreach (var f in _db.Set<CustomerFile>().AsNoTracking().AsAsyncEnumerable()
            .WithCancellation(ct).ConfigureAwait(false))
        {
            await WriteBlobAsync(
                archive,
                $"files/customer-files/{f.PartnerId}/{Safe(NonEmpty(f.FileName, f.Id))}",
                f.Bytes,
                ct).ConfigureAwait(false);
        }

        // company_profile -> files/company/logo.{ext} (only when a logo is present)
        await foreach (var p in _db.Set<CompanyProfile>().AsNoTracking().AsAsyncEnumerable()
            .WithCancellation(ct).ConfigureAwait(false))
        {
            if (p.LogoBytes is { Length: > 0 } logo)
            {
                await WriteBlobAsync(
                    archive,
                    $"files/company/logo.{LogoExtension(p.LogoContentType)}",
                    logo,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteBlobAsync(ZipArchive archive, string path, byte[] bytes, CancellationToken ct)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static void DropByteArrayProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].PropertyType == typeof(byte[]))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }

    private static string NonEmpty(string? value, Guid fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback.ToString() : value;

    // Reduce any value to a single safe file-name segment (strips directory components and
    // invalid characters) so a hostile original name can never escape its files/ subtree.
    private static string Safe(string value)
    {
        var name = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = value;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.Length == 0 ? "file" : sb.ToString();
    }

    private static string LogoExtension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" or "image/jpg" => "jpg",
        "image/gif" => "gif",
        "image/svg+xml" => "svg",
        "image/webp" => "webp",
        _ => "bin",
    };
}
