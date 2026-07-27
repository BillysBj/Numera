using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.EInvoice.Inbound;

/// <summary>
/// A received (inbound) e-invoice filed as an Eingangsbeleg (Phase-5 EINV-04/EINV-05). A user
/// uploads an XRechnung (UBL/CII XML) or a ZUGFeRD PDF; the ingest pipeline detects the format,
/// extracts the embedded XML from a PDF, parses it via <c>InvoiceDescriptor.Load</c>, validates it
/// against KoSIT, and matches the supplier by VAT id — storing the IMMUTABLE original bytes plus a
/// human-readable read-model + the validation verdict + the matched partner here.
/// </summary>
/// <remarks>
/// <para>
/// GoBD (§ 147 AO): <see cref="OriginalBytes"/> is the received artifact preserved byte-for-byte
/// and NEVER mutated — everything else on this row is a DERIVED projection (a read-model, a
/// validation verdict, a match). Re-processing replaces the derived fields, never the original.
/// </para>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy (hand-written in the migration —
/// reflective discovery never emits policies) + the tenant query filter; the entity self-describes
/// via attributes so <c>NumeraDbContext</c> is not edited and no DbSet is added (mirrors
/// <c>DocumentRender</c> / <c>EInvoiceArtifact</c> EXACTLY). <see cref="MatchedPartnerId"/> is
/// provenance only (a plain Guid FK to <c>partners.id</c>) — NO navigation; persist via
/// <c>db.Add(...)</c>, never a collection-navigation add (a client-set UUIDv7 PK child would
/// otherwise be tracked Modified → a 0-row UPDATE).
/// </para>
/// </remarks>
[Table("inbound_document")]
[Index(nameof(TenantId), nameof(UploadedAt))]
public sealed class InboundDocument : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    // --- The immutable received artifact (GoBD) ------------------------------

    /// <summary>The received file bytes (bytea), preserved byte-for-byte — NEVER mutated (GoBD).</summary>
    public required byte[] OriginalBytes { get; init; }

    /// <summary>The original upload file name (e.g. RE-2026-00001-ubl.xml or invoice.pdf).</summary>
    public required string OriginalFileName { get; init; }

    /// <summary>The original upload content-type (application/xml, text/xml or application/pdf).</summary>
    public required string OriginalContentType { get; init; }

    /// <summary>Size of <see cref="OriginalBytes"/> in bytes (denormalized for listings).</summary>
    public long ByteSize { get; init; }

    // --- Derived: the detected format + parsed read-model --------------------

    /// <summary>The detected input format (raw UBL/CII XML, or an embedded-XML ZUGFeRD PDF).</summary>
    public InboundFormat DetectedFormat { get; set; }

    /// <summary>The human-readable parsed projection serialized as JSON (jsonb) — the read-model.</summary>
    [Column(TypeName = "jsonb")]
    public string? ReadModel { get; set; }

    // --- Derived: the KoSIT validation verdict -------------------------------

    /// <summary>The KoSIT verdict for the extracted XML (Accepted / Rejected / Unavailable).</summary>
    public EInvoiceValidationStatus ValidationStatus { get; set; }

    /// <summary>The structured KoSIT findings serialized as JSON (jsonb), or null when unavailable.</summary>
    [Column(TypeName = "jsonb")]
    public string? ValidationReport { get; set; }

    // --- Derived: the matched supplier (EINV-05) -----------------------------

    /// <summary>The matched supplier (FK → partners.id, provenance only), or null when unmatched.</summary>
    public Guid? MatchedPartnerId { get; set; }

    // --- Denormalized summary fields (for the list without parsing jsonb) ----

    /// <summary>The seller / supplier name (BT-27), denormalized for the list.</summary>
    public string? SellerName { get; set; }

    /// <summary>The seller VAT id (BT-31), denormalized — the supplier-match key.</summary>
    public string? SellerVatId { get; set; }

    /// <summary>The invoice number (BT-1), denormalized for the list.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>The grand total (BT-112), denormalized for the list.</summary>
    [Precision(19, 4)]
    public decimal? TotalGross { get; set; }

    /// <summary>The ISO 4217 currency (BT-5), denormalized for the list.</summary>
    public string? Currency { get; set; }

    /// <summary>The invoice date (BT-2), denormalized for the list.</summary>
    public DateOnly? InvoiceDate { get; set; }

    // --- Upload metadata -----------------------------------------------------

    /// <summary>When the e-invoice was uploaded / ingested.</summary>
    public DateTimeOffset UploadedAt { get; set; }
}
