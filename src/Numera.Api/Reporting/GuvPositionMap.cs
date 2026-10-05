using System.Globalization;

using Numera.Modules.Ledger;

namespace Numera.Api.Reporting;

/// <summary>An inclusive account-number range within one account type.</summary>
public sealed record GuvPositionDefinition(
    AccountType Type, int FromNumber, int ToNumber, string Bezeichnung)
{
    internal bool Matches(AccountType type, string number) =>
        Type == type
        && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= FromNumber && value <= ToNumber;
}

/// <summary>Versioned, coarse Gesamtkostenverfahren positions for SKR03 and SKR04.</summary>
public static class GuvPositionMap
{
    internal const string OtherRevenue = "Sonstige betriebliche Erträge";
    internal const string OtherExpense = "Sonstige betriebliche Aufwendungen";

    // TODO(Steuerberater): Review exact §275-HGB grouping, including financial results,
    // tax subgroups and chart-specific exceptions. These are deliberately coarse ranges.
    private static readonly IReadOnlyDictionary<ChartVariant, IReadOnlyList<GuvPositionDefinition>> Positions2024 =
        new Dictionary<ChartVariant, IReadOnlyList<GuvPositionDefinition>>
        {
            [ChartVariant.Skr03] =
            [
                new(AccountType.Revenue, 8000, 8599, "Umsatzerlöse"),
                new(AccountType.Revenue, 8600, 8999, OtherRevenue),
                new(AccountType.Revenue, 2000, 2999, OtherRevenue),
                new(AccountType.Expense, 3000, 3999, "Materialaufwand"),
                new(AccountType.Expense, 4100, 4199, "Personalaufwand"),
                new(AccountType.Expense, 4800, 4899, "Abschreibungen"),
                new(AccountType.Expense, 4000, 4099, OtherExpense),
                new(AccountType.Expense, 4200, 4299, OtherExpense),
                new(AccountType.Expense, 4400, 4799, OtherExpense),
                new(AccountType.Expense, 4900, 4999, OtherExpense),
                new(AccountType.Expense, 2200, 2299, "Steuern"),
                new(AccountType.Expense, 4300, 4399, "Steuern"),
            ],
            [ChartVariant.Skr04] =
            [
                new(AccountType.Revenue, 4000, 4799, "Umsatzerlöse"),
                new(AccountType.Revenue, 4800, 4999, OtherRevenue),
                new(AccountType.Expense, 5000, 5999, "Materialaufwand"),
                new(AccountType.Expense, 6000, 6199, "Personalaufwand"),
                new(AccountType.Expense, 6200, 6299, "Abschreibungen"),
                new(AccountType.Expense, 6300, 6999, OtherExpense),
                new(AccountType.Expense, 7600, 7699, "Steuern"),
            ],
        };

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<ChartVariant, IReadOnlyList<GuvPositionDefinition>>> Versions =
        new Dictionary<int, IReadOnlyDictionary<ChartVariant, IReadOnlyList<GuvPositionDefinition>>>
        {
            [2024] = Positions2024,
        };

    /// <summary>Returns the newest mapping effective for the fiscal year and chart.</summary>
    public static IReadOnlyList<GuvPositionDefinition> ForFiscalYear(ChartVariant chart, int jahr)
    {
        var effectiveYear = Versions.Keys.Where(year => year <= jahr).DefaultIfEmpty().Max();
        if (effectiveYear == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jahr), jahr, "GuV positions are available starting with fiscal year 2024.");
        }

        return Versions[effectiveYear].TryGetValue(chart, out var positions)
            ? positions
            : throw new ArgumentOutOfRangeException(nameof(chart), chart, "Only SKR03 and SKR04 are supported.");
    }
}
