using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Pdf;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>
/// The shared render entrypoint the on-demand endpoint and the Hangfire render job both call
/// (Phase-4 DOCS-02). It loads a FINALIZED <see cref="SalesDocument"/> under RLS, reads the
/// tenant logo live (the sole permitted live read — presentation, not the §14 legal snapshot),
/// renders the professional PDF from the frozen snapshot via <see cref="SnapshotReader"/> +
/// <see cref="InvoiceDocument"/>, and stores the byte-identical artifact in
/// <c>document_render</c> — idempotently, one row per (document, language).
/// </summary>
/// <remarks>
/// <para>
/// LOCKED (RESEARCH.md Pattern 3): the render legal content comes ONLY from the frozen
/// <c>IssuerSnapshot</c>/<c>RecipientSnapshot</c> + persisted lines/breakdown/totals. The tenant
/// <see cref="CompanyProfile.LogoBytes"/> is read live because a logo is presentation, not part
/// of the immutable legal snapshot; a later logo change affects only re-renders.
/// </para>
/// <para>
/// Idempotency: <see cref="RenderAndStore"/> deletes any existing render for the
/// (document, language) pair and inserts a fresh one, so re-running (the render job's retry, an
/// explicit re-render) never duplicates. Rows are inserted via <c>db.Add</c> — the file-wide
/// client-set-UUIDv7-PK convention, never a navigation-collection add.
/// </para>
/// </remarks>
public sealed class DocumentPdfService
{
    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _tenant;

    /// <summary>Creates the service over the request/job-scoped DbContext + tenant.</summary>
    public DocumentPdfService(NumeraDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>The outcome of a render/download request.</summary>
    public enum Outcome
    {
        /// <summary>The PDF is available (stored or freshly rendered).</summary>
        Ok,

        /// <summary>No document with that id exists under the current tenant (RLS-scoped).</summary>
        NotFound,

        /// <summary>The document exists but is still a Draft — a draft has no frozen snapshot to render.</summary>
        NotFinalized,
    }

    /// <summary>A download result: the outcome plus (on <see cref="Outcome.Ok"/>) the bytes + filename number.</summary>
    /// <param name="Result">Whether the PDF is available, missing, or the document is a draft.</param>
    /// <param name="PdfBytes">The PDF bytes when <see cref="Result"/> is <see cref="Outcome.Ok"/>.</param>
    /// <param name="DocumentNumber">The legal number for the download filename.</param>
    public readonly record struct PdfResult(Outcome Result, byte[]? PdfBytes, string? DocumentNumber);

    /// <summary>
    /// Renders <paramref name="documentId"/> from its frozen snapshot and stores the bytes in
    /// <c>document_render</c>, replacing any prior render for the same (document, language).
    /// Called by the render job. Throws if the document is missing or still a Draft (the finalize
    /// hook only ever enqueues finalized documents, so either is a programming/timing error).
    /// </summary>
    public async Task<DocumentRender> RenderAndStore(Guid documentId, string language, CancellationToken ct)
    {
        var lang = Normalize(language);
        var doc = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Cannot render document {documentId}: not found under the current tenant.");

        if (doc.Status == DocumentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot render document {documentId}: it is still a Draft (no frozen snapshot).");
        }

        return await RenderAndStoreCore(doc, lang, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The render-if-absent download path (idempotent recovery): returns the stored render if one
    /// exists for the (document, language) pair, else renders + stores it on demand. Distinguishes
    /// a missing document (<see cref="Outcome.NotFound"/>) from a still-draft one
    /// (<see cref="Outcome.NotFinalized"/>) so the endpoint can map clean 404 / 409 responses.
    /// </summary>
    public async Task<PdfResult> GetOrRender(Guid documentId, string language, CancellationToken ct)
    {
        var lang = Normalize(language);

        var existing = await _db.Set<DocumentRender>()
            .AsNoTracking()
            .Where(r => r.DocumentId == documentId && r.Language == lang)
            .OrderByDescending(r => r.RenderedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new PdfResult(Outcome.Ok, existing.PdfBytes, existing.DocumentNumber);
        }

        var doc = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false);
        if (doc is null)
        {
            return new PdfResult(Outcome.NotFound, null, null);
        }

        if (doc.Status == DocumentStatus.Draft)
        {
            return new PdfResult(Outcome.NotFinalized, null, null);
        }

        var render = await RenderAndStoreCore(doc, lang, ct).ConfigureAwait(false);
        return new PdfResult(Outcome.Ok, render.PdfBytes, render.DocumentNumber);
    }

    // Loads the document (RLS-scoped) with the persisted lines + BG-23 breakdown the render needs.
    private Task<SalesDocument?> LoadDocumentAsync(Guid documentId, CancellationToken ct) =>
        _db.Set<SalesDocument>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.TaxBreakdown)
            .FirstOrDefaultAsync(x => x.Id == documentId, ct);

    // Renders the frozen snapshot (+ live logo) and stores it, replacing any prior render for the
    // (document, language) pair so the store is idempotent.
    private async Task<DocumentRender> RenderAndStoreCore(SalesDocument doc, string lang, CancellationToken ct)
    {
        // The tenant logo is the SOLE permitted live read (presentation, not the legal snapshot).
        var logo = await _db.Set<CompanyProfile>()
            .AsNoTracking()
            .Select(p => new { p.LogoBytes, p.LogoContentType })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var prepayments = await _db.Set<SalesDocumentPrepayment>()
            .AsNoTracking()
            .Where(p => p.DocumentId == doc.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var model = SnapshotReader.FromDocument(
            doc,
            logo?.LogoBytes,
            logo?.LogoContentType,
            lang,
            prepayments: prepayments);
        var bytes = InvoiceDocument.Render(model);

        // Idempotent replace: drop any prior render for this (document, language), insert the fresh one.
        var stale = await _db.Set<DocumentRender>()
            .Where(r => r.DocumentId == doc.Id && r.Language == lang)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stale.Count > 0)
        {
            _db.RemoveRange(stale);
        }

        var render = new DocumentRender
        {
            TenantId = _tenant.TenantId!.Value,
            DocumentId = doc.Id,
            PdfBytes = bytes,
            DocumentNumber = doc.DocumentNumber ?? doc.Id.ToString(),
            Language = lang,
            ByteSize = bytes.LongLength,
            RenderedAt = DateTimeOffset.UtcNow,
        };
        _db.Add(render);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return render;
    }

    private static string Normalize(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";
}
