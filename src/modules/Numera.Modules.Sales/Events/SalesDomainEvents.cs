namespace Numera.Modules.Sales.Events;

/// <summary>
/// Raised AFTER a finalize transaction commits (INV-01/INV-02). Carries the frozen number
/// and totals so a downstream handler (Phase-6 ledger posting, Phase-4/5 rendering) can act
/// without re-reading the document. The open item + audit were written INSIDE the finalize
/// transaction, NOT via this event — the event is a side-effect seam, not the source of truth.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="DocumentId">The finalized document.</param>
/// <param name="DocumentNumber">The assigned legal number (e.g. RE-2026-00001).</param>
/// <param name="TotalNet">Sum of line nets (BT-106).</param>
/// <param name="TotalTax">Sum of the BG-23 breakdown tax (BT-110).</param>
/// <param name="TotalGross">Grand total (BT-112).</param>
/// <param name="DocumentDate">Issue date (BT-2).</param>
public sealed record InvoiceFinalized(
    Guid TenantId,
    Guid DocumentId,
    string DocumentNumber,
    decimal TotalNet,
    decimal TotalTax,
    decimal TotalGross,
    DateOnly DocumentDate);

/// <summary>
/// Raised after an invoice is cancelled by a Storno (INV-03, built in plan 03-06). The
/// original invoice's open item is closed inside that transaction; this is the post-commit seam.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="DocumentId">The original invoice that was cancelled.</param>
/// <param name="StornoDocumentId">The Storno document that cancels it.</param>
public sealed record InvoiceCancelled(Guid TenantId, Guid DocumentId, Guid StornoDocumentId);

/// <summary>
/// Raised after a commercial credit note (Gutschrift) is issued against an original invoice
/// (INV-03, plan 03-06). Post-commit side-effect seam.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="DocumentId">The issued credit note.</param>
/// <param name="OriginalDocumentId">The invoice it corrects.</param>
public sealed record CreditNoteIssued(Guid TenantId, Guid DocumentId, Guid OriginalDocumentId);
