using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales;

/// <summary>
/// The polymorphic sales-document aggregate root (RESEARCH.md Pattern 1) — ONE table
/// carrying every document kind (<see cref="DocumentType"/> discriminator): Angebot,
/// Auftragsbestätigung, Lieferschein, Rechnung, Storno, Gutschrift. The chain
/// conversion (quote → order → delivery → invoice) is a header+lines row-copy, and
/// immutability + numbering apply conditionally on <see cref="DocumentType"/> AND
/// <see cref="Status"/>.
/// </summary>
/// <remarks>
/// <para>
/// GoBD immutability is enforced at the DB level by the status-guarded
/// <c>sales_document_immutable</c> trigger (see the <c>SalesDocuments</c> migration):
/// while <see cref="Status"/> is <see cref="DocumentStatus.Draft"/> the row is freely
/// mutable; once finalized, the business columns are frozen and only a whitelist of
/// lifecycle columns (status, sent_at, finalized_at, cancelled_by_document_id, due_date,
/// amount_due) may still change. Deletion of a non-draft row is blocked outright.
/// </para>
/// <para>
/// It is deliberately NOT <see cref="IArchivable"/>: a finalized document must never be
/// archived-away, and a draft delete is a hard delete guarded by the trigger.
/// The issuer/recipient are stored as frozen <c>jsonb</c> snapshots (BG-4/BG-5), not
/// live FKs, so a later master-data edit cannot mutate an issued invoice.
/// </para>
/// </remarks>
[Table("sales_documents")]
[Index(nameof(TenantId), nameof(DocumentType), nameof(Status))]
public sealed class SalesDocument : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    // --- Discriminator + lifecycle -------------------------------------------

    /// <summary>The document kind discriminator (BT-3 seam).</summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>Lifecycle state; defaults to <see cref="DocumentStatus.Draft"/> (0 — the trigger keys off it).</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    /// <summary>
    /// The legal document number (fortlaufend, einmalig). Assigned ONLY at finalize;
    /// NULL while draft. Unique per (tenant, doc_type) among non-null values (partial
    /// unique index in the migration — number reuse is impossible).
    /// </summary>
    public string? DocumentNumber { get; set; }

    // --- Chain / provenance (self-referencing, plain Guid? columns) ----------

    /// <summary>The predecessor this document was copied-forward from (Angebot→AB→LS→Rechnung).</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>For Storno/Gutschrift: the original invoice being corrected (EN 16931 BG-3 preceding reference).</summary>
    public Guid? CorrectsDocumentId { get; set; }

    /// <summary>Back-link on an original invoice to its Storno (whitelisted, set when cancelled).</summary>
    public Guid? CancelledByDocumentId { get; set; }

    // --- Partner reference + FROZEN snapshots (GoBD) -------------------------

    /// <summary>Partner reference for navigation only; the authoritative recipient data is the snapshot.</summary>
    public Guid? PartnerId { get; set; }

    /// <summary>Frozen buyer name/address/VatId captured at finalize (BG-7). JSON.</summary>
    [Column(TypeName = "jsonb")]
    public string? RecipientSnapshot { get; set; }

    /// <summary>Frozen issuer (company_profile) data captured at finalize (BG-4). JSON.</summary>
    [Column(TypeName = "jsonb")]
    public string? IssuerSnapshot { get; set; }

    // --- Dates ---------------------------------------------------------------

    /// <summary>Rechnungsdatum / Ausstellungsdatum (BT-2).</summary>
    public DateOnly DocumentDate { get; set; }

    /// <summary>Leistungsdatum (BT-72) or service-period start. Nullable.</summary>
    public DateOnly? ServiceDate { get; set; }

    /// <summary>Leistungszeitraum end (BG-14). Nullable.</summary>
    public DateOnly? ServicePeriodEnd { get; set; }

    /// <summary>Payment due date (BT-9), computed from partner terms at finalize. Nullable, whitelisted.</summary>
    public DateOnly? DueDate { get; set; }

    // --- Money (persisted, authoritative, frozen at finalize) ----------------

    /// <summary>ISO 4217 currency (BT-5). Defaults to EUR.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// Frozen exchange rate using the convention: units of foreign currency per 1 EUR —
    /// i.e. amountEur = amountForeign / ExchangeRate.
    /// </summary>
    [Precision(19, 6)]
    public decimal? ExchangeRate { get; set; }

    /// <summary>Reference date of the frozen exchange rate (GoBD provenance).</summary>
    public DateOnly? ExchangeRateDate { get; set; }

    /// <summary>The frozen document VAT total in EUR (EN 16931 BT-111).</summary>
    [Precision(19, 4)]
    public decimal? TotalTaxEur { get; set; }

    /// <summary>Sum of line nets = tax basis (BT-106 / BT-109).</summary>
    [Precision(19, 4)]
    public decimal TotalNet { get; set; }

    /// <summary>Sum of breakdown tax amounts (BT-110).</summary>
    [Precision(19, 4)]
    public decimal TotalTax { get; set; }

    /// <summary>Grand total (BT-112).</summary>
    [Precision(19, 4)]
    public decimal TotalGross { get; set; }

    /// <summary>Amount due (BT-115; = gross in v1). Whitelisted (Phase 6 payments reduce it).</summary>
    [Precision(19, 4)]
    public decimal AmountDue { get; set; }

    // --- Flags captured at finalize ------------------------------------------

    /// <summary>Snapshot of the issuer §19 status; when true the document carries no VAT.</summary>
    public bool IsKleinunternehmer { get; set; }

    /// <summary>True when any line is reverse-charge (AE / §13b).</summary>
    public bool ReverseCharge { get; set; }

    /// <summary>Leitweg-ID / buyer reference (BT-10, B2G seam). Nullable.</summary>
    public string? BuyerReference { get; set; }

    /// <summary>Free document note (BT-22). Nullable.</summary>
    public string? Notes { get; set; }

    // --- Lifecycle timestamps (whitelisted) ----------------------------------

    /// <summary>When the document was finalized (set once, whitelisted).</summary>
    public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>When the document was sent (whitelisted).</summary>
    public DateTimeOffset? SentAt { get; set; }

    // --- Navigation ----------------------------------------------------------

    // [ForeignKey] on the collection navigation binds it to the dependent's
    // DocumentId FK column; without it EF convention invents a shadow
    // sales_document_id column (the convention looks for {PrincipalType}Id, which
    // DocumentId does not match). This keeps the schema on the intended FK with no
    // NumeraDbContext edit.

    /// <summary>The document's line items (BG-25).</summary>
    [ForeignKey(nameof(SalesDocumentLine.DocumentId))]
    public List<SalesDocumentLine> Lines { get; set; } = [];

    /// <summary>The EN 16931 VAT breakdown rows (BG-23), one per (category, rate).</summary>
    [ForeignKey(nameof(SalesDocumentTaxBreakdown.DocumentId))]
    public List<SalesDocumentTaxBreakdown> TaxBreakdown { get; set; } = [];
}
