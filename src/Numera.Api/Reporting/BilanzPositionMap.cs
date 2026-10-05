using System.Globalization;

using Numera.Modules.Ledger;

namespace Numera.Api.Reporting;

/// <summary>An inclusive balance-sheet account range within one account type.</summary>
public sealed record BilanzPositionDefinition(
    AccountType Type, int FromNumber, int ToNumber, string Gruppe)
{
    internal bool Matches(AccountType type, string number) =>
        Type == type
        && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= FromNumber && value <= ToNumber;
}

/// <summary>Versioned, coarse balance-sheet positions for SKR03 and SKR04.</summary>
public static class BilanzPositionMap
{
    internal const string CurrentAssets = "Umlaufvermögen";
    internal const string Equity = "Eigenkapital";
    internal const string Liabilities = "Verbindlichkeiten";

    // TODO(Steuerberater): Refine §266-HGB granularity and chart-specific exceptions.
    // AccountType remains authoritative; custom numbers fall back within their own side.
    private static readonly IReadOnlyDictionary<ChartVariant, IReadOnlyList<BilanzPositionDefinition>> Positions2024 =
        new Dictionary<ChartVariant, IReadOnlyList<BilanzPositionDefinition>>
        {
            [ChartVariant.Skr03] =
            [
                new(AccountType.Asset, 0, 999, "Anlagevermögen"),
                new(AccountType.Asset, 1000, 1999, CurrentAssets),
                new(AccountType.Equity, 800, 899, Equity),
                new(AccountType.Liability, 970, 979, "Rückstellungen"),
                new(AccountType.Liability, 600, 799, Liabilities),
                new(AccountType.Liability, 1600, 1999, Liabilities),
            ],
            [ChartVariant.Skr04] =
            [
                new(AccountType.Asset, 0, 999, "Anlagevermögen"),
                new(AccountType.Asset, 1000, 1999, CurrentAssets),
                new(AccountType.Equity, 2000, 2999, Equity),
                new(AccountType.Liability, 3000, 3099, "Rückstellungen"),
                new(AccountType.Liability, 3100, 3999, Liabilities),
            ],
        };

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<ChartVariant, IReadOnlyList<BilanzPositionDefinition>>> Versions =
        new Dictionary<int, IReadOnlyDictionary<ChartVariant, IReadOnlyList<BilanzPositionDefinition>>>
        {
            [2024] = Positions2024,
        };

    /// <summary>Returns the newest mapping effective for the fiscal year and chart.</summary>
    public static IReadOnlyList<BilanzPositionDefinition> ForFiscalYear(ChartVariant chart, int jahr)
    {
        var effectiveYear = Versions.Keys.Where(year => year <= jahr).DefaultIfEmpty().Max();
        if (effectiveYear == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jahr), jahr, "Bilanz positions are available starting with fiscal year 2024.");
        }

        return Versions[effectiveYear].TryGetValue(chart, out var positions)
            ? positions
            : throw new ArgumentOutOfRangeException(nameof(chart), chart, "Only SKR03 and SKR04 are supported.");
    }
}
