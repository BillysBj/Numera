namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// The canonical outcome of validating an e-invoice XML against the government-authoritative
/// KoSIT validator (Phase-5 EINV-03). This is the SINGLE definition of the validation status —
/// the Api validation-result record, 05-03's stored <c>document_einvoice</c> artifacts, and the
/// 05-05 inbound store + frontend all import THIS enum. There is no per-plan re-declaration.
/// </summary>
/// <remarks>
/// The ordinals are a FIXED, append-only contract: the 05-05 frontend hard-codes these numbers
/// and 05-03's persisted artifacts key on them. NEVER insert a value between existing ones and
/// NEVER reorder — only ever APPEND a new value with the next ordinal. Reordering would silently
/// re-interpret every already-stored status (an accepted invoice reading as rejected, or worse).
/// </remarks>
public enum EInvoiceValidationStatus
{
    /// <summary>The KoSIT validator ACCEPTED the e-invoice: it is conformant and may be sent.</summary>
    Accepted = 0,

    /// <summary>
    /// The KoSIT validator REJECTED the e-invoice: it violates one or more rules (the findings
    /// explain what and why). The send is blocked until the document is corrected.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// The validation could NOT be performed because the validator service was unreachable /
    /// timed out. This is explicitly DISTINCT from <see cref="Rejected"/>: the invoice is not
    /// known to be bad, the check simply did not run — retry when the service is back (a
    /// transient outage must never surface as a rejection). RESEARCH Pitfall 6.
    /// </summary>
    Unavailable = 2,
}
