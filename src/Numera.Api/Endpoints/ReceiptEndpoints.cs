using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Upload and RLS-scoped review/read access for the unified receipt aggregate.</summary>
public static class ReceiptEndpoints
{
    private const int MaxPageSize = 100;

    /// <summary>Maps the authenticated <c>/api/receipts</c> surface.</summary>
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/receipts").RequireAuthorization();

        g.MapPost("/", async (
            IFormFile? file,
            ReceiptIngestService ingest,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (file is null || file.Length <= 0 || file.Length > ReceiptIngestService.MaxUploadBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] =
                    [
                        $"Die Datei muss zwischen 1 Byte und {ReceiptIngestService.MaxUploadBytes} Bytes gro\u00df sein.",
                    ],
                });
            }

            var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "upload" : file.FileName.Trim();
            var contentType = file.ContentType?.Trim() ?? string.Empty;
            if (ReceiptIngestService.ResolveContentType(contentType, fileName) is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["Es werden nur PDF-, JPEG-, PNG-, TIFF- oder HEIF-Dateien akzeptiert."],
                });
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);

            ReceiptIngestService.IngestResult result;
            try
            {
                result = await ingest.IngestAsync(
                    stream.ToArray(),
                    fileName,
                    contentType,
                    ReceiptSource.Upload,
                    currentUser.UserId,
                    ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [ex.Message],
                });
            }

            if (result.Quarantined)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = [result.RejectReason!] },
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Datei quarant\u00e4nisiert");
            }

            return Results.Created(
                $"/api/receipts/{result.ReceiptId}",
                new { id = result.ReceiptId, status = (int)result.Status });
        }).DisableAntiforgery();

        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            ReceiptStatus? status = null,
            int page = 1,
            int pageSize = 25) =>
        {
            var take = Math.Clamp(pageSize, 1, MaxPageSize);
            var normalizedPage = Math.Max(page, 1);
            var query = db.Set<Receipt>().AsNoTracking();
            if (status is { } requestedStatus)
            {
                query = query.Where(receipt => receipt.Status == requestedStatus);
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var receipts = await query
                .OrderByDescending(receipt => receipt.CreatedAt)
                .Skip((normalizedPage - 1) * take)
                .Take(take)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var archives = await LoadArchivesAsync(db, receipts, ct).ConfigureAwait(false);
            var inbound = await LoadInboundAsync(db, receipts, ct).ConfigureAwait(false);
            var items = receipts.Select(receipt =>
            {
                var originalFileName = receipt.ArchiveId is { } archiveId
                    && archives.TryGetValue(archiveId, out var archive)
                        ? archive.FileName
                        : receipt.InboundDocumentId is { } inboundId
                            && inbound.TryGetValue(inboundId, out var inboundOriginal)
                                ? inboundOriginal.FileName
                                : null;
                return new ReceiptContracts.ReceiptListItem(
                    receipt.Id,
                    (int)receipt.Source,
                    (int)receipt.Status,
                    receipt.SupplierName,
                    receipt.SupplierVatId,
                    receipt.InvoiceNumber,
                    receipt.InvoiceDate,
                    receipt.NetAmount,
                    receipt.VatAmount,
                    receipt.GrossAmount,
                    receipt.VatRatePercent,
                    receipt.Currency,
                    ParseConfidence(receipt.FieldConfidence),
                    receipt.MatchedPartnerId,
                    originalFileName,
                    receipt.CreatedAt);
            }).ToList();

            return Results.Ok(new ReceiptContracts.ReceiptListResponse(
                items, normalizedPage, take, total));
        });

        g.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var receipt = await db.Set<Receipt>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, ct)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                return Results.NotFound();
            }

            OriginalMetadata? original = null;
            if (receipt.ArchiveId is { } archiveId)
            {
                original = await db.Set<ReceiptArchive>()
                    .AsNoTracking()
                    .Where(a => a.Id == archiveId)
                    .Select(a => new OriginalMetadata(
                        a.OriginalFileName,
                        a.ContentType,
                        a.ByteSize,
                        a.ReceivedAt,
                        a.UploadedByUserId))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }
            else if (receipt.InboundDocumentId is { } inboundDocumentId)
            {
                original = await db.Set<InboundDocument>()
                    .AsNoTracking()
                    .Where(d => d.Id == inboundDocumentId)
                    .Select(d => new OriginalMetadata(
                        d.OriginalFileName,
                        d.OriginalContentType,
                        d.ByteSize,
                        d.UploadedAt,
                        null))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }

            return Results.Ok(new ReceiptContracts.ReceiptDetail(
                receipt.Id,
                (int)receipt.Source,
                (int)receipt.Status,
                receipt.InboundDocumentId,
                receipt.ArchiveId,
                receipt.ContentHash,
                receipt.SupplierName,
                receipt.SupplierVatId,
                receipt.InvoiceNumber,
                receipt.InvoiceDate,
                receipt.ExpenseDate,
                receipt.NetAmount,
                receipt.VatAmount,
                receipt.GrossAmount,
                receipt.VatRatePercent,
                receipt.Currency,
                ParseConfidence(receipt.FieldConfidence),
                receipt.MatchedPartnerId,
                receipt.ExpenseAccountOverride,
                receipt.JournalEntryId,
                original?.FileName,
                original?.ContentType,
                original?.ByteSize,
                original?.ReceivedAt,
                original?.UploadedByUserId,
                receipt.CreatedAt));
        });

        g.MapGet("/{id:guid}/original", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var receipt = await db.Set<Receipt>()
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new { r.ArchiveId, r.InboundDocumentId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                return Results.NotFound();
            }

            if (receipt.ArchiveId is { } archiveId)
            {
                var archive = await db.Set<ReceiptArchive>()
                    .AsNoTracking()
                    .Where(a => a.Id == archiveId)
                    .Select(a => new { a.OriginalBytes, a.ContentType, a.OriginalFileName })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                return archive is null
                    ? Results.NotFound()
                    : Results.File(archive.OriginalBytes, archive.ContentType, archive.OriginalFileName);
            }

            if (receipt.InboundDocumentId is { } inboundId)
            {
                var document = await db.Set<InboundDocument>()
                    .AsNoTracking()
                    .Where(d => d.Id == inboundId)
                    .Select(d => new { d.OriginalBytes, d.OriginalContentType, d.OriginalFileName })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                return document is null
                    ? Results.NotFound()
                    : Results.File(document.OriginalBytes, document.OriginalContentType, document.OriginalFileName);
            }

            return Results.NotFound();
        });

        return app;
    }

    private static async Task<Dictionary<Guid, OriginalMetadata>> LoadArchivesAsync(
        NumeraDbContext db,
        IReadOnlyCollection<Receipt> receipts,
        CancellationToken ct)
    {
        var ids = receipts.Where(r => r.ArchiveId.HasValue).Select(r => r.ArchiveId!.Value).ToArray();
        return await db.Set<ReceiptArchive>()
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new OriginalMetadata(
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.ByteSize,
                a.ReceivedAt,
                a.UploadedByUserId))
            .ToDictionaryAsync(a => a.Id, ct)
            .ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, OriginalMetadata>> LoadInboundAsync(
        NumeraDbContext db,
        IReadOnlyCollection<Receipt> receipts,
        CancellationToken ct)
    {
        var ids = receipts
            .Where(r => r.InboundDocumentId.HasValue)
            .Select(r => r.InboundDocumentId!.Value)
            .ToArray();
        return await db.Set<InboundDocument>()
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new OriginalMetadata(
                d.Id,
                d.OriginalFileName,
                d.OriginalContentType,
                d.ByteSize,
                d.UploadedAt,
                null))
            .ToDictionaryAsync(d => d.Id, ct)
            .ConfigureAwait(false);
    }

    private static JsonElement? ParseConfidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OriginalMetadata(
        Guid Id,
        string FileName,
        string ContentType,
        long ByteSize,
        DateTimeOffset ReceivedAt,
        Guid? UploadedByUserId)
    {
        public OriginalMetadata(
            string fileName,
            string contentType,
            long byteSize,
            DateTimeOffset receivedAt,
            Guid? uploadedByUserId)
            : this(Guid.Empty, fileName, contentType, byteSize, receivedAt, uploadedByUserId)
        {
        }
    }
}
