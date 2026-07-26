using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// A generated, KoSIT-validated e-invoice artifact of a finalized <see cref="SalesDocument"/>
/// (Phase-5 EINV-01/EINV-03). The generate job serializes the frozen
/// invoice snapshot to one XRechnung syntax (see <see cref="Format"/>), validates the bytes
/// against the government-authoritative KoSIT validator, and stores the XML + the
/// <see cref="ValidationStatus"/> + the structured report here, keyed to the immutable
/// document. Download reuses the stored bytes; the post-finalize send gate reads the stored
/// <see cref="ValidationStatus"/> (must be <c>Accepted</c> to dispatch). Because the source
/// document is immutable post-finalize, the generated XML is deterministic and cacheable.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy (hand-written in the migration —
/// reflective discovery never emits policies) + the tenant query filter; the entity
/// self-describes via attributes so <c>NumeraDbContext</c> is not edited and no DbSet is
/// added (mirrors <c>DocumentRender</c> EXACTLY). <see cref="DocumentId"/> is provenance only
/// (a plain Guid FK to <c>sales_documents.id</c>) — NO navigation; persist via
/// <c>db.Add(...)</c>, never a collection-navigation add (a client-set UUIDv7 PK child would
/// otherwise be tracked Modified → a 0-row UPDATE).
/// </para>
/// <para>
/// The flow that writes a row is synchronous generate → validate → persist, so a row is NEVER
/// written in a "pending" state — <see cref="ValidationStatus"/> is always one of the canonical
/// three (Accepted / Rejected / Unavailable). There is deliberately no <c>Pending</c> value.
/// </para>
/// </remarks>
[Table("document_einvoice")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class EInvoiceArtifact : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The finalized document this artifact belongs to (FK → sales_documents.id, provenance only).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Which e-invoice syntax these bytes are (UBL / CII / ZUGFeRD PDF-A3). The stable persistence key.</summary>
    public EInvoiceFormat Format { get; set; }

    /// <summary>The generated e-invoice XML blob (bytea). The stored, byte-identical artifact.</summary>
    public required byte[] Xml { get; set; }

    /// <summary>Denormalized document number for the download filename (e.g. RE-2026-00001).</summary>
    public required string DocumentNumber { get; set; }

    /// <summary>The KoSIT verdict for these exact bytes (Accepted / Rejected / Unavailable).</summary>
    public EInvoiceValidationStatus ValidationStatus { get; set; }

    /// <summary>The structured KoSIT findings serialized as JSON (jsonb), or null when unavailable.</summary>
    [Column(TypeName = "jsonb")]
    public string? ValidationReport { get; set; }

    /// <summary>Size of <see cref="Xml"/> in bytes (denormalized for listings without loading the blob).</summary>
    public long ByteSize { get; set; }

    /// <summary>When the XML was generated.</summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>When the XML was validated (null only if validation was skipped).</summary>
    public DateTimeOffset? ValidatedAt { get; set; }
}
