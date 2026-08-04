using System.Text.Json;

using Hangfire;

using Numera.Api.Jobs;
using Numera.Modules.Sales.Belege;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>
/// Shared Tier-B receipt intake: validate, scan, deduplicate, archive the immutable original and
/// enqueue asynchronous extraction. No path in this service creates a booking.
/// </summary>
public sealed class ReceiptIngestService
{
    /// <summary>Maximum accepted upload/attachment size (15 MiB).</summary>
    public const long MaxUploadBytes = 15 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IAttachmentScanner _scanner;
    private readonly ReceiptDeduplicator _deduplicator;
    private readonly IBackgroundJobClient _jobs;
    private readonly IAuditWriter _audit;

    /// <summary>Creates the scoped intake service.</summary>
    public ReceiptIngestService(
        NumeraDbContext db,
        ICurrentTenant tenant,
        IAttachmentScanner scanner,
        ReceiptDeduplicator deduplicator,
        IBackgroundJobClient jobs,
        IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _scanner = scanner;
        _deduplicator = deduplicator;
        _jobs = jobs;
        _audit = audit;
    }

    /// <summary>The persisted intake outcome.</summary>
    public readonly record struct IngestResult(
        Guid ReceiptId,
        ReceiptStatus Status,
        string? RejectReason = null)
    {
        /// <summary>Whether malware scanning failed closed.</summary>
        public bool Quarantined => Status == ReceiptStatus.Quarantined;
    }

    /// <summary>
    /// Scans before any archive write, then archives clean bytes and enqueues extraction only after
    /// the receipt/archive unit has committed.
    /// </summary>
    public async Task<IngestResult> IngestAsync(
        byte[] bytes,
        string fileName,
        string contentType,
        ReceiptSource source,
        Guid? uploadedByUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var storedFileName = string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName.Trim();
        var storedContentType = ResolveContentType(contentType, storedFileName);
        if (bytes.LongLength is <= 0 or > MaxUploadBytes)
        {
            throw new ArgumentException(
                $"Die Datei muss zwischen 1 Byte und {MaxUploadBytes} Bytes gro\u00df sein.",
                nameof(bytes));
        }

        if (storedContentType is null)
        {
            throw new ArgumentException(
                "Es werden nur PDF-, Bild- oder XML-Dateien akzeptiert.",
                nameof(contentType));
        }

        var tenantId = _tenant.TenantId
            ?? throw new InvalidOperationException("No tenant is active for receipt ingest.");

        // D4: fail closed and do not persist the untrusted bytes in receipt_archive.
        var scan = await _scanner.ScanAsync(bytes, storedFileName, ct).ConfigureAwait(false);
        var contentHash = _deduplicator.ComputeHash(bytes);
        if (scan.Verdict is ScanVerdict.Infected or ScanVerdict.Error)
        {
            var quarantined = new Receipt
            {
                TenantId = tenantId,
                Source = source,
                Status = ReceiptStatus.Quarantined,
                ContentHash = contentHash,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _db.Add(quarantined);
            var reason = scan.Verdict == ScanVerdict.Infected
                ? $"Die Datei wurde als Schadsoftware erkannt ({scan.Signature ?? "unbekannte Signatur"})."
                : "Die Datei wurde quarant\u00e4nisiert, weil die Schadsoftwarepr\u00fcfung fehlgeschlagen ist.";
            await _audit.RecordAsync(
                new ReceiptAuditEvent(
                    "receipt.quarantined",
                    quarantined.Id,
                    Before: null,
                    After: JsonSerializer.Serialize(
                        new { source, scan.Verdict, scan.Signature, contentHash }, Json)),
                ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new IngestResult(quarantined.Id, quarantined.Status, reason);
        }

        var duplicate = await _deduplicator.CheckAsync(
            _db,
            contentHash,
            supplierId: null,
            invoiceNumber: null,
            gross: null,
            invoiceDate: null,
            ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var receipt = new Receipt
        {
            TenantId = tenantId,
            Source = source,
            Status = duplicate == DuplicateVerdict.None
                ? ReceiptStatus.Captured
                : ReceiptStatus.Duplicate,
            ContentHash = contentHash,
            CreatedAt = now,
        };
        var archive = new ReceiptArchive
        {
            TenantId = tenantId,
            ReceiptId = receipt.Id,
            OriginalBytes = bytes,
            OriginalFileName = storedFileName,
            ContentType = storedContentType,
            ByteSize = bytes.LongLength,
            ContentHash = contentHash,
            Source = source,
            ReceivedAt = now,
            UploadedByUserId = uploadedByUserId,
        };
        receipt.ArchiveId = archive.Id;

        _db.Add(receipt);
        _db.Add(archive);
        await _audit.RecordAsync(
            new ReceiptAuditEvent(
                duplicate == DuplicateVerdict.None ? "receipt.captured" : "receipt.duplicate_detected",
                receipt.Id,
                Before: null,
                After: JsonSerializer.Serialize(
                    new { source, receipt.Status, archiveId = archive.Id, contentHash, duplicate }, Json)),
            ct).ConfigureAwait(false);

        // One SaveChanges is the archive + aggregate commit boundary.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Duplicate is a deliberate review state; only Captured receipts enter OCR.
        if (receipt.Status == ReceiptStatus.Captured)
        {
            _jobs.Enqueue<ExtractReceiptJob>(
                job => job.RunAsync(tenantId, receipt.Id, CancellationToken.None));
        }

        return new IngestResult(receipt.Id, receipt.Status);
    }

    /// <summary>Validates MIME type or, for generic browser uploads, infers it from the extension.</summary>
    internal static string? ResolveContentType(string? contentType, string fileName)
    {
        var mime = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mime is "application/pdf" or "application/xml" or "text/xml"
            or "image/jpeg" or "image/png" or "image/tiff"
            or "image/heif" or "image/heic" or "image/heif-sequence" or "image/heic-sequence")
        {
            return mime;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            ".heif" => "image/heif",
            ".heic" => "image/heic",
            _ => null,
        };
    }
}

internal sealed record ReceiptAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(Receipt);
}
