using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Rendering;

/// <summary>
/// The rendered PDF artifact of a finalized <see cref="SalesDocument"/> (Phase-4 DOCS-02).
/// A render job produces the professional §14 PDF once from the frozen document snapshot
/// and stores the bytes here, keyed to the immutable document, so download and e-mail reuse
/// one byte-identical artifact (a stable GoBD/audit object and the Phase-5 ZUGFeRD carrier)
/// instead of re-rendering. Because the source document is immutable post-finalize, the
/// rendered bytes are deterministic and cacheable (RESEARCH.md Q4).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy (hand-written in the migration —
/// reflective discovery never emits policies) + the tenant query filter; the entity
/// self-describes via attributes so <c>NumeraDbContext</c> is not edited and no DbSet is
/// added. <see cref="DocumentId"/> is provenance only (a plain Guid FK to
/// <c>sales_documents.id</c>) — NO navigation, to avoid coupling; persist via
/// <c>db.Add(...)</c>, never a collection-navigation add (the file-wide convention that a
/// client-set UUIDv7 PK child must be inserted via <c>db.Add</c>, else EF marks it Modified).
/// </para>
/// </remarks>
[Table("document_render")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class DocumentRender : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The finalized document this render belongs to (FK → sales_documents.id, provenance only).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The rendered PDF blob (bytea). The stored, byte-identical artifact.</summary>
    public required byte[] PdfBytes { get; set; }

    /// <summary>Denormalized document number for the download filename (e.g. RE-2026-00001.pdf).</summary>
    public required string DocumentNumber { get; set; }

    /// <summary>The language ("de"/"en") the PDF was rendered in.</summary>
    public required string Language { get; set; }

    /// <summary>Size of <see cref="PdfBytes"/> in bytes (denormalized for listings without loading the blob).</summary>
    public long ByteSize { get; set; }

    /// <summary>When the PDF was rendered.</summary>
    public DateTimeOffset RenderedAt { get; set; }
}
