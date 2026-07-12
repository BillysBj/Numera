namespace Numera.Modules.Sales;

/// <summary>
/// The kind of sales document, the discriminator of the ONE polymorphic
/// <see cref="SalesDocument"/> table (RESEARCH.md Pattern 1). Upstream types
/// (Angebot → Auftragsbestätigung → Lieferschein) are lighter and mutable;
/// only Rechnung/Storno/Gutschrift are legally numbered + DB-immutable.
/// </summary>
/// <remarks>
/// Ordinals serialize on the wire as numbers (the web client mirrors them).
/// Correction documents map to EN 16931 BT-3 type codes at XML time (Phase 5):
/// Rechnung=380, Gutschrift=381, Storno=384 (RESEARCH.md Pattern 6).
/// </remarks>
public enum DocumentType
{
    /// <summary>Angebot — a non-binding quote (mutable, no legal number).</summary>
    Angebot,

    /// <summary>Auftragsbestätigung — order confirmation (mutable).</summary>
    Auftragsbestaetigung,

    /// <summary>Lieferschein — delivery note (mutable; renders without prices).</summary>
    Lieferschein,

    /// <summary>Rechnung — commercial invoice (EN 16931 BT-3 code 380); numbered + immutable.</summary>
    Rechnung,

    /// <summary>Storno — cancellation of a specific invoice (code 384); mirrors with negative amounts.</summary>
    Storno,

    /// <summary>Gutschrift — commercial credit note / Rechnungskorrektur (code 381).</summary>
    Gutschrift,
}

/// <summary>
/// The lifecycle state of a sales document. <see cref="Draft"/> MUST be 0 — the
/// DB immutability trigger keys off <c>status = 0</c> to decide whether a row is
/// still freely mutable (RESEARCH.md Pattern 2).
/// </summary>
public enum DocumentStatus
{
    /// <summary>Draft — fully mutable, no number; may be edited/deleted freely. MUST be 0.</summary>
    Draft = 0,

    /// <summary>Finalized — numbered, snapshotted, frozen. GoBD Unveränderbarkeit begins here.</summary>
    Finalized = 1,

    /// <summary>Sent — the finalized document was dispatched (whitelisted lifecycle transition).</summary>
    Sent = 2,

    /// <summary>Cancelled — superseded by a Storno (back-linked via cancelled_by_document_id).</summary>
    Cancelled = 3,

    /// <summary>Paid — the receivable is settled (Phase 6 payment recording seam).</summary>
    Paid = 4,
}

/// <summary>
/// The settlement state of an <see cref="OpenItem"/> (offener Posten). Payments
/// (Phase 6) reduce <see cref="OpenItem.OpenAmount"/> and flip this; v1 only ever
/// creates <see cref="Open"/> items (and <see cref="Cancelled"/> on Storno).
/// </summary>
public enum OpenItemStatus
{
    /// <summary>Open — nothing paid yet; open_amount = original_amount.</summary>
    Open,

    /// <summary>PartiallyPaid — some payment recorded (Phase 6).</summary>
    PartiallyPaid,

    /// <summary>Paid — fully settled (Phase 6).</summary>
    Paid,

    /// <summary>Cancelled — the underlying invoice was cancelled by a Storno.</summary>
    Cancelled,
}
