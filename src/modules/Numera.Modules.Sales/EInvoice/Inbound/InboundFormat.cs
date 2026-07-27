namespace Numera.Modules.Sales.EInvoice.Inbound;

/// <summary>
/// The detected input format of an uploaded (received) e-invoice — what the
/// <see cref="InboundParser"/> recognized the raw upload as (Phase-5 EINV-04).
/// </summary>
/// <remarks>
/// Distinct from the outbound <see cref="EInvoiceFormat"/> (which names an OUTPUT target we
/// generate): this enum names how the INBOUND artifact arrived so the read-model + UI can label
/// it. Inbound e-invoices arrive either as a raw XRechnung XML (UBL or CII syntax) or as a
/// ZUGFeRD / Factur-X PDF/A-3 carrying the CII XML as an embedded file. The numeric ordinals are
/// persisted on <c>inbound_document.detected_format</c>; only ever append new members.
/// </remarks>
public enum InboundFormat
{
    /// <summary>A raw XRechnung UBL (OASIS Universal Business Language) XML upload.</summary>
    XmlUbl = 0,

    /// <summary>A raw XRechnung CII (UN/CEFACT Cross Industry Invoice) XML upload.</summary>
    XmlCii = 1,

    /// <summary>A ZUGFeRD / Factur-X PDF/A-3 with the CII e-invoice XML embedded.</summary>
    ZugferdPdf = 2,
}
