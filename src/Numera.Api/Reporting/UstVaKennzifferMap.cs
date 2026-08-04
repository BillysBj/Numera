using Numera.Modules.Ledger;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>Identifies whether a USt-VA figure is a base or a tax amount.</summary>
public enum UstVaFigureKind
{
    /// <summary>Bemessungsgrundlage, rendered in whole euros.</summary>
    Bemessungsgrundlage,

    /// <summary>Tax amount, rendered with two decimal places.</summary>
    Steuer,
}

/// <summary>Frozen posting/tax characteristics selecting a recognized revenue base.</summary>
public sealed record UstVaRecognitionSelector(
    TaxCategory TaxCategory,
    decimal TaxRatePercent,
    PostingDirection NaturalSide);

/// <summary>One Kennziffer supported by a fiscal-year form version.</summary>
public sealed record UstVaKennzifferDefinition(
    string Kz,
    string Bezeichnung,
    UstVaFigureKind FigureKind,
    UstVaRecognitionSelector? RecognitionSelector,
    bool IsComputed);

/// <summary>
/// Fiscal-year-versioned USt-VA Kennziffer definitions. Missing Kennziffern have no
/// data source in this milestone and are intentionally absent rather than zero-filled.
/// </summary>
public static class UstVaKennzifferMap
{
    /// <summary>Notice attached to the output-VAT-only Zahllast in Phase 11.</summary>
    public const string ZahllastHinweis = "Ohne Vorsteuerabzug (Belegerfassung ab Phase 12).";

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<UstVaKennzifferDefinition>> Versions =
        new Dictionary<int, IReadOnlyList<UstVaKennzifferDefinition>>
        {
            [2026] =
            [
                new(
                    "81",
                    "Steuerpflichtige Umsätze 19 %",
                    UstVaFigureKind.Bemessungsgrundlage,
                    new UstVaRecognitionSelector(TaxCategory.S, 19m, PostingDirection.Credit),
                    IsComputed: false),
                new(
                    "86",
                    "Steuerpflichtige Umsätze 7 %",
                    UstVaFigureKind.Bemessungsgrundlage,
                    new UstVaRecognitionSelector(TaxCategory.S, 7m, PostingDirection.Credit),
                    IsComputed: false),
                new(
                    "41",
                    "Innergemeinschaftliche Lieferungen (§4 Nr. 1b)",
                    UstVaFigureKind.Bemessungsgrundlage,
                    new UstVaRecognitionSelector(TaxCategory.K, 0m, PostingDirection.Credit),
                    IsComputed: false),
                new(
                    "83",
                    "Verbleibende USt-Vorauszahlung (Zahllast)",
                    UstVaFigureKind.Steuer,
                    RecognitionSelector: null,
                    IsComputed: true),
            ],
        };

    /// <summary>
    /// Returns the newest Kennziffer version effective for <paramref name="fiscalYear"/>.
    /// A future form change is added as another version without changing calculator code.
    /// </summary>
    public static IReadOnlyList<UstVaKennzifferDefinition> ForFiscalYear(int fiscalYear)
    {
        var effectiveYear = Versions.Keys
            .Where(versionYear => versionYear <= fiscalYear)
            .DefaultIfEmpty()
            .Max();
        if (effectiveYear == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fiscalYear),
                fiscalYear,
                "USt-VA Kennziffern are available starting with fiscal year 2026.");
        }

        return Versions[effectiveYear];
    }
}
