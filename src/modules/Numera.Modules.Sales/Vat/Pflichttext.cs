using Numera.Platform.Money;

namespace Numera.Modules.Sales.Vat;

/// <summary>
/// Maps a zero-rate EN 16931 VAT category to its exemption reason code (BT-121) and the
/// mandatory German note (Pflichttext, BT-120) that MUST appear on the invoice.
/// </summary>
/// <remarks>
/// <para>
/// The <b>note text</b> is the legally load-bearing part for the Phase-4 PDF — an invoice
/// that omits e.g. the §13b Steuerschuldnerschaft note is formally deficient. The
/// <c>VATEX-EU-*</c> <b>code strings</b> are the Phase-5 XML concern and carry MEDIUM
/// confidence (RESEARCH Open Q2): they are re-verified against the official EN 16931 VATEX
/// code list during e-invoice generation. In particular the Kleinunternehmer §19 code
/// (<c>VATEX-EU-D</c>) is flagged to re-check the exact string in Phase 5. The golden-file
/// suite pins both today so any change is a deliberate, reviewed data fill.
/// </para>
/// <para>
/// Categories S (standard, taxed) and Z (zero-rated) carry no exemption reason; O
/// (out-of-scope) has no rate and no note. They map to <c>(null, null)</c>.
/// </para>
/// </remarks>
public static class Pflichttext
{
    /// <summary>
    /// Returns the (BT-121 exemption code, BT-120 German note) for a category, or
    /// <c>(null, null)</c> when the category needs neither.
    /// </summary>
    public static (string? Code, string? Text) For(TaxCategory category) => category switch
    {
        // §13b UStG reverse charge (incl. Bauleistungen).
        TaxCategory.AE => ("VATEX-EU-AE", "Steuerschuldnerschaft des Leistungsempfängers (§13b UStG)"),

        // Innergemeinschaftliche Lieferung (requires recipient USt-IdNr).
        TaxCategory.K => ("VATEX-EU-IC", "Steuerfreie innergemeinschaftliche Lieferung (§4 Nr. 1b i.V.m. §6a UStG)"),

        // Kleinunternehmer §19. VATEX-EU-D re-verify against the official list in Phase 5.
        TaxCategory.E => ("VATEX-EU-D", "Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG"),

        // Ausfuhrlieferung (export outside the EU).
        TaxCategory.G => ("VATEX-EU-G", "Steuerfreie Ausfuhrlieferung (§4 Nr. 1a i.V.m. §6 UStG)"),

        // S (taxed), Z (zero-rated), O (out-of-scope) need no exemption reason.
        _ => (null, null),
    };
}
