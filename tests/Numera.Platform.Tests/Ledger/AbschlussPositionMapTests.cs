using Numera.Api.Reporting;
using Numera.Modules.Ledger;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class AbschlussPositionMapTests
{
    [Theory]
    [InlineData(ChartVariant.Skr03, AccountType.Revenue, "8400", "Umsatzerlöse")]
    [InlineData(ChartVariant.Skr03, AccountType.Revenue, "8600", "Sonstige betriebliche Erträge")]
    [InlineData(ChartVariant.Skr03, AccountType.Expense, "3400", "Materialaufwand")]
    [InlineData(ChartVariant.Skr03, AccountType.Expense, "4120", "Personalaufwand")]
    [InlineData(ChartVariant.Skr03, AccountType.Expense, "4830", "Abschreibungen")]
    [InlineData(ChartVariant.Skr03, AccountType.Expense, "4980", "Sonstige betriebliche Aufwendungen")]
    [InlineData(ChartVariant.Skr03, AccountType.Expense, "4320", "Steuern")]
    [InlineData(ChartVariant.Skr04, AccountType.Revenue, "4400", "Umsatzerlöse")]
    [InlineData(ChartVariant.Skr04, AccountType.Revenue, "4830", "Sonstige betriebliche Erträge")]
    [InlineData(ChartVariant.Skr04, AccountType.Expense, "5400", "Materialaufwand")]
    [InlineData(ChartVariant.Skr04, AccountType.Expense, "6020", "Personalaufwand")]
    [InlineData(ChartVariant.Skr04, AccountType.Expense, "6220", "Abschreibungen")]
    [InlineData(ChartVariant.Skr04, AccountType.Expense, "6300", "Sonstige betriebliche Aufwendungen")]
    [InlineData(ChartVariant.Skr04, AccountType.Expense, "7610", "Steuern")]
    public void Guv_ranges_resolve_common_accounts(ChartVariant chart, AccountType type, string number, string expected)
    {
        Assert.Equal(expected, Assert.Single(GuvPositionMap.ForFiscalYear(chart, 2026), definition => definition.Matches(type, number)).Bezeichnung);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, AccountType.Asset, "0400", "Anlagevermögen")]
    [InlineData(ChartVariant.Skr03, AccountType.Asset, "1200", "Umlaufvermögen")]
    [InlineData(ChartVariant.Skr03, AccountType.Equity, "0800", "Eigenkapital")]
    [InlineData(ChartVariant.Skr03, AccountType.Liability, "0970", "Rückstellungen")]
    [InlineData(ChartVariant.Skr03, AccountType.Liability, "1700", "Verbindlichkeiten")]
    [InlineData(ChartVariant.Skr04, AccountType.Asset, "0400", "Anlagevermögen")]
    [InlineData(ChartVariant.Skr04, AccountType.Asset, "1800", "Umlaufvermögen")]
    [InlineData(ChartVariant.Skr04, AccountType.Equity, "2900", "Eigenkapital")]
    [InlineData(ChartVariant.Skr04, AccountType.Liability, "3070", "Rückstellungen")]
    [InlineData(ChartVariant.Skr04, AccountType.Liability, "3500", "Verbindlichkeiten")]
    public void Bilanz_ranges_resolve_common_accounts(ChartVariant chart, AccountType type, string number, string expected)
    {
        Assert.Equal(expected, Assert.Single(BilanzPositionMap.ForFiscalYear(chart, 2026), definition => definition.Matches(type, number)).Gruppe);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03)]
    [InlineData(ChartVariant.Skr04)]
    public void Maps_use_latest_effective_year_and_have_non_overlapping_ranges(ChartVariant chart)
    {
        var guv = GuvPositionMap.ForFiscalYear(chart, 2024);
        var bilanz = BilanzPositionMap.ForFiscalYear(chart, 2024);
        Assert.Equal(guv, GuvPositionMap.ForFiscalYear(chart, 2030));
        Assert.Equal(bilanz, BilanzPositionMap.ForFiscalYear(chart, 2030));
        Assert.All(guv, definition => Assert.DoesNotContain(guv, other => other != definition
            && other.Type == definition.Type && other.FromNumber <= definition.ToNumber && other.ToNumber >= definition.FromNumber));
        Assert.All(bilanz, definition => Assert.DoesNotContain(bilanz, other => other != definition
            && other.Type == definition.Type && other.FromNumber <= definition.ToNumber && other.ToNumber >= definition.FromNumber));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuvPositionMap.ForFiscalYear(chart, 2023));
        Assert.Throws<ArgumentOutOfRangeException>(() => BilanzPositionMap.ForFiscalYear(chart, 2023));
    }
}
