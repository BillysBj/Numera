using System.Text.Json;

using Numera.Api.Endpoints;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>
/// Ingests a received (inbound) e-invoice as an Eingangsbeleg (Phase-5 EINV-04/EINV-05). The shared
/// entrypoint the upload endpoint calls: parse (detect → extract → <c>InvoiceDescriptor.Load</c>)
/// → validate against KoSIT → match the supplier by VAT id → store the IMMUTABLE original bytes +
/// the human-readable read-model + the verdict + the matched partner in <c>inbound_document</c>
/// (RLS), all in one <c>db.Add</c> + <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// <para>
/// GoBD: the received bytes are stored byte-for-byte untouched; everything else is a derived
/// projection. A validator OUTAGE (<see cref="EInvoiceValidationStatus.Unavailable"/>) does NOT
/// fail the upload — the original is still filed and can be re-validated later (RESEARCH Pitfall 6);
/// only a NON-e-invoice (no embedded/parseable XML) is refused, so the endpoint can 422.
/// </para>
/// <para>
/// Idempotency is NOT applied here: each upload is a distinct received artifact (a supplier may
/// legitimately resend), so every ingest inserts a new row — unlike the outbound
/// <see cref="EInvoiceService"/> which replaces per (document, format).
/// </para>
/// </remarks>
public sealed class InboundEInvoiceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IEInvoiceValidator _validator;
    private readonly SupplierMatcher _matcher;
    private readonly IAuditWriter _audit;

    /// <summary>Creates the service over the request-scoped DbContext + tenant + validator + matcher + audit.</summary>
    public InboundEInvoiceService(
        NumeraDbContext db,
        ICurrentTenant tenant,
        IEInvoiceValidator validator,
        SupplierMatcher matcher,
        IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _validator = validator;
        _matcher = matcher;
        _audit = audit;
    }

    /// <summary>The outcome of an ingest: the stored document, or a German rejection reason.</summary>
    /// <param name="Document">The stored <see cref="InboundDocument"/> on success, else null.</param>
    /// <param name="RejectReason">The German "no e-invoice" reason on failure, else null.</param>
    public readonly record struct IngestResult(InboundDocument? Document, string? RejectReason)
    {
        /// <summary>True when the upload was ingested as an e-invoice.</summary>
        public bool Success => Document is not null;
    }

    /// <summary>
    /// Parses, validates, supplier-matches and stores the received e-invoice. Returns a rejection
    /// reason (no row written) when the upload carries no parseable e-invoice; otherwise stores the
    /// immutable original + read-model + verdict + match and returns the row.
    /// </summary>
    public async Task<IngestResult> IngestAsync(
        byte[] bytes,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var parsed = InboundParser.Parse(bytes, contentType, fileName);
        if (!parsed.Success)
        {
            return new IngestResult(null, parsed.FailureReason ?? "Die Datei ist keine E-Rechnung.");
        }

        var readModel = parsed.ReadModel!;
        var xml = parsed.ExtractedXml!;

        // Validate the extracted XML against KoSIT. An outage stores Unavailable + a null report
        // (the original is still filed); a rejection stores the explained findings.
        var validation = await _validator.ValidateAsync(xml, ct).ConfigureAwait(false);

        // Match the supplier by seller VAT id (then name). Provenance only — no partner is created.
        var matchedPartnerId = await _matcher
            .MatchSellerAsync(_db, readModel.Seller.VatId, readModel.Seller.Name, ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var document = new InboundDocument
        {
            TenantId = _tenant.TenantId!.Value,
            OriginalBytes = bytes, // stored byte-for-byte, never mutated (GoBD)
            OriginalFileName = string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName,
            OriginalContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            ByteSize = bytes.LongLength,
            DetectedFormat = parsed.Format,
            ReadModel = JsonSerializer.Serialize(readModel, Json),
            ValidationStatus = validation.Status,
            ValidationReport = SerializeReport(validation),
            MatchedPartnerId = matchedPartnerId,
            SellerName = readModel.Seller.Name,
            SellerVatId = readModel.Seller.VatId,
            InvoiceNumber = readModel.InvoiceNumber,
            TotalGross = readModel.TotalGross,
            Currency = readModel.Currency,
            InvoiceDate = readModel.InvoiceDate,
            UploadedAt = now,
        };

        _db.Add(document);
        await _audit.RecordAsync(
            new InboundDocumentAuditEvent(
                "inbound_document.ingested", document.Id, Before: null,
                After: JsonSerializer.Serialize(
                    new
                    {
                        document.DetectedFormat,
                        document.ValidationStatus,
                        document.SellerVatId,
                        document.InvoiceNumber,
                        document.MatchedPartnerId,
                    },
                    Json)),
            ct).ConfigureAwait(false);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IngestResult(document, null);
    }

    // Persists the structured findings + verdict as jsonb; an outage with no findings → null report
    // (mirrors EInvoiceService.SerializeReport so both stores read identically).
    private static string? SerializeReport(EInvoiceValidationResult result)
    {
        if (result.Status == EInvoiceValidationStatus.Unavailable && result.Findings.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new { status = result.Status.ToString(), findings = result.Findings },
            Json);
    }
}
