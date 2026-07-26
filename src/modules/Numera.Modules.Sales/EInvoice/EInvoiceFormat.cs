namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// The concrete e-invoice output targets Numera can produce from a single
/// <c>InvoiceDescriptor</c> (see <see cref="EInvoiceMapper"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XRechnungUbl"/> and <see cref="XRechnungCii"/> are the two legally-accepted
/// XRechnung syntaxes (UBL and CII) produced in this plan (05-01) by
/// <c>XRechnungGenerator</c>. <see cref="ZugferdPdfA3"/> is the hybrid PDF/A-3 carrier
/// produced later in 05-04; it is declared here now so the persistence layer (05-03) and
/// the download UI key on a STABLE enum rather than a magic string.
/// </para>
/// <para>
/// The numeric ordinals are load-bearing once persisted (they key the
/// <c>document_einvoice</c> artifact rows in 05-03) — only ever append new members.
/// </para>
/// </remarks>
public enum EInvoiceFormat
{
    /// <summary>XRechnung in UBL syntax (Universal Business Language, OASIS).</summary>
    XRechnungUbl = 0,

    /// <summary>XRechnung in CII syntax (UN/CEFACT Cross Industry Invoice).</summary>
    XRechnungCii = 1,

    /// <summary>ZUGFeRD / Factur-X PDF/A-3 with the CII XML embedded (produced in 05-04).</summary>
    ZugferdPdfA3 = 2,
}
