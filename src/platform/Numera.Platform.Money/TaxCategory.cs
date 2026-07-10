namespace Numera.Platform.Money;

/// <summary>
/// EN 16931 VAT category codes (BT-118 / UNTDID 5305 subset) used to bucket
/// taxable amounts before per-category rounding.
/// </summary>
/// <remarks>
/// These are the legally load-bearing category identifiers referenced by the
/// German e-invoicing profiles (XRechnung / ZUGFeRD). Category-specific
/// <c>pflichttext</c> handling (e.g. the §13b reverse-charge note) is out of
/// scope here — that lands in Phase 3. This enum only carries the arithmetic
/// bucketing identity.
/// </remarks>
public enum TaxCategory
{
    /// <summary>Standard rate (Regelsteuersatz / ermäßigt).</summary>
    S,

    /// <summary>VAT reverse charge (§13b UStG / Steuerschuldnerschaft des Leistungsempfängers).</summary>
    AE,

    /// <summary>Intra-community supply of goods and services (innergemeinschaftliche Lieferung).</summary>
    K,

    /// <summary>Exempt from tax (steuerbefreit).</summary>
    E,

    /// <summary>Zero-rated goods (Nullsatz).</summary>
    Z,

    /// <summary>Free export item, tax not charged (Ausfuhr).</summary>
    G,

    /// <summary>Services outside the scope of tax (nicht steuerbar).</summary>
    O
}
