namespace Numera.Modules.Catalog;

/// <summary>
/// The curated subset of UN/ECE Recommendation 20 unit-of-measure codes (EN 16931
/// BT-130 "Invoiced quantity unit of measure") that Numera offers for catalog items
/// in v1. Only the CODE is stored on a <see cref="CatalogItem"/>; the German display
/// labels (Stück, Stunde, …) are a frontend concern (Phase 2 wave 07) and are
/// deliberately NOT modelled here as a full code table.
/// </summary>
/// <remarks>
/// Storing the standard code (not a free-text unit) means Phase-3/5 e-invoicing can
/// emit BT-130 directly with no lookup or mapping. The list is intentionally small
/// and business-driven (RESEARCH.md Q6): the common German trade/service units.
/// </remarks>
public static class UnitOfMeasure
{
    /// <summary>Piece / unit — "one" (Stück). The Product default.</summary>
    public const string Piece = "C62";

    /// <summary>Hour (Stunde). The Service default.</summary>
    public const string Hour = "HUR";

    private static readonly IReadOnlyList<string> _codes = new[]
    {
        "C62", // Stück (one)
        "H87", // piece
        "HUR", // Stunde (hour)
        "DAY", // Tag (day)
        "MON", // Monat (month)
        "KGM", // Kilogramm (kilogram)
        "MTR", // Meter (metre)
        "MTK", // Quadratmeter (square metre)
        "LTR", // Liter (litre)
        "KWH", // Kilowattstunde (kilowatt hour)
    };

    /// <summary>The allowed UN/ECE Rec 20 codes, in presentation order.</summary>
    public static IReadOnlyList<string> Codes => _codes;

    /// <summary>True if <paramref name="code"/> is one of the curated allowed codes (case-sensitive).</summary>
    public static bool IsValid(string? code) => code is not null && _codes.Contains(code);

    /// <summary>
    /// The sensible default unit for a given item kind: <see cref="Piece"/> (C62) for
    /// products, <see cref="Hour"/> (HUR) for services.
    /// </summary>
    public static string DefaultFor(CatalogItemKind kind) => kind switch
    {
        CatalogItemKind.Service => Hour,
        _ => Piece,
    };
}
