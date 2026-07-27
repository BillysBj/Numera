using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Audit;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>
/// The inbound e-invoice (Eingangsbelege) HTTP surface (Phase-5 EINV-04/EINV-05): upload a received
/// XRechnung/ZUGFeRD, list the received e-invoices, view a human-readable detail, and download the
/// immutable original bytes as received (GoBD).
/// </summary>
/// <remarks>
/// Upload mirrors the CompanyProfile logo multipart convention (<c>IFormFile</c> +
/// <c>DisableAntiforgery</c> for the BFF POST) with a size cap + an allowed content-type/extension
/// guard. Ingest (parse → validate → match → store) lives in <see cref="InboundEInvoiceService"/>;
/// a non-e-invoice upload is 422'd (no row written). All reads are RLS-scoped to the current tenant.
/// </remarks>
public static class InboundDocumentEndpoints
{
    // A received e-invoice can be a sizeable ZUGFeRD PDF/A-3 (fonts + embedded XML), so the cap is
    // more generous than the letterhead logo's 1 MB — but still bounded to reject absurd uploads.
    private const long MaxUploadBytes = 15 * 1024 * 1024; // 15 MB
    private const int MaxPageSize = 100;

    private static readonly string[] AllowedContentTypes =
        ["application/pdf", "application/xml", "text/xml"];

    /// <summary>Maps <c>/api/inbound-documents</c> upload + list + detail + original-download.</summary>
    public static IEndpointRouteBuilder MapInboundDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/inbound-documents").RequireAuthorization();

        // POST /api/inbound-documents — upload a received e-invoice (multipart). Detects/extracts/
        // parses/validates/supplier-matches and stores the immutable original + read-model. 201 with
        // the new id + summary; 422 when the file carries no e-invoice; 400 on an invalid upload.
        g.MapPost("/", async (
            IFormFile file,
            InboundEInvoiceService ingest,
            CancellationToken ct) =>
        {
            if (file.Length <= 0 || file.Length > MaxUploadBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [$"Die Datei muss zwischen 1 Byte und {MaxUploadBytes} Bytes groß sein."],
                });
            }

            var contentType = file.ContentType?.Trim() ?? string.Empty;
            var fileName = file.FileName?.Trim() ?? "upload";
            if (!IsAllowed(contentType, fileName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["Es werden nur XRechnung-XML (application/xml, text/xml) oder ZUGFeRD-PDF (application/pdf) akzeptiert."],
                });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);
            var bytes = ms.ToArray();

            var result = await ingest.IngestAsync(bytes, fileName, contentType, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = [result.RejectReason!] },
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Keine E-Rechnung erkannt");
            }

            var doc = result.Document!;
            return Results.Created(
                $"/api/inbound-documents/{doc.Id}",
                new
                {
                    id = doc.Id,
                    detectedFormat = (int)doc.DetectedFormat,
                    validationStatus = (int)doc.ValidationStatus,
                    sellerName = doc.SellerName,
                    invoiceNumber = doc.InvoiceNumber,
                    totalGross = doc.TotalGross,
                    currency = doc.Currency,
                    matchedPartnerId = doc.MatchedPartnerId,
                });
        }).DisableAntiforgery();

        // GET /api/inbound-documents — paged list (RLS-scoped), newest upload first.
        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25) =>
        {
            var take = Math.Clamp(pageSize, 1, MaxPageSize);
            var skip = (Math.Max(page, 1) - 1) * take;

            var query = db.Set<InboundDocument>().AsNoTracking().OrderByDescending(d => d.UploadedAt);
            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var items = await query
                .Skip(skip)
                .Take(take)
                .Select(d => new InboundContracts.InboundListItem(
                    d.Id,
                    (int)d.DetectedFormat,
                    d.SellerName,
                    d.SellerVatId,
                    d.InvoiceNumber,
                    d.InvoiceDate,
                    d.TotalGross,
                    d.Currency,
                    (int)d.ValidationStatus,
                    d.MatchedPartnerId,
                    d.OriginalFileName,
                    d.UploadedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new InboundContracts.InboundListResponse(items, Math.Max(page, 1), take, total));
        });

        // GET /api/inbound-documents/{id} — human-readable detail: read-model + validation findings
        // + matched supplier. 404 when unknown under the current tenant (RLS).
        g.MapGet("/{id:guid}", async (
            Guid id,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var d = await db.Set<InboundDocument>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (d is null)
            {
                return Results.NotFound();
            }

            var detail = new InboundContracts.InboundDetail(
                d.Id,
                (int)d.DetectedFormat,
                (int)d.ValidationStatus,
                d.MatchedPartnerId,
                d.OriginalFileName,
                d.OriginalContentType,
                d.ByteSize,
                d.UploadedAt,
                d.SellerName,
                d.SellerVatId,
                d.InvoiceNumber,
                d.InvoiceDate,
                d.TotalGross,
                d.Currency,
                ParseReadModel(d.ReadModel),
                EInvoiceGate.ReadFindings(d.ValidationReport));

            return Results.Ok(detail);
        });

        // GET /api/inbound-documents/{id}/original — download the immutable received bytes as-is
        // (GoBD: the received artifact is retrievable byte-for-byte with its stored content-type/name).
        g.MapGet("/{id:guid}/original", async (
            Guid id,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var d = await db.Set<InboundDocument>()
                .AsNoTracking()
                .Select(x => new { x.Id, x.OriginalBytes, x.OriginalContentType, x.OriginalFileName })
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);

            return d is null
                ? Results.NotFound()
                : Results.File(d.OriginalBytes, d.OriginalContentType, d.OriginalFileName);
        });

        return app;
    }

    // Content-type OR file-extension gate: browsers frequently send application/octet-stream for a
    // .xml upload, so a matching extension is accepted even when the content-type is generic.
    private static bool IsAllowed(string contentType, string fileName)
    {
        if (AllowedContentTypes.Any(t => contentType.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    // Parses the stored read-model jsonb string into a JsonElement for the detail response; a
    // missing/malformed read-model yields null (the summary fields still render).
    private static JsonElement? ParseReadModel(string? readModelJson)
    {
        if (string.IsNullOrWhiteSpace(readModelJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(readModelJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// A minimal <see cref="IAuditEvent"/> for inbound e-invoice ingests. The writer stamps tenant +
/// actor from ambient context; the service supplies only the change.
/// </summary>
internal sealed record InboundDocumentAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(InboundDocument);
}
