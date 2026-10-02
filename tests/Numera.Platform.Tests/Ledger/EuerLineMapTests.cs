using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Platform.Money;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class EuerLineMapTests
{
    [Theory]
    [InlineData(ChartVariant.Skr03, 2024)]
    [InlineData(ChartVariant.Skr03, 2025)]
    [InlineData(ChartVariant.Skr04, 2024)]
    [InlineData(ChartVariant.Skr04, 2025)]
    public void Official_form_lines_and_wording_are_used_for_both_charts(ChartVariant chart, int jahr)
    {
        var lines = EuerLineMap.ForFiscalYear(chart, jahr);
        Assert.Equal(8, lines.Count);
        AssertLine("12", "Betriebseinnahmen als umsatzsteuerlicher Kleinunternehmer", EuerAmountKind.Revenue);
        AssertLine("15", "Umsatzsteuerpflichtige Betriebseinnahmen (netto)", EuerAmountKind.Revenue);
        AssertLine("16", "Umsatzsteuerfreie, nicht umsatzsteuerbare Betriebseinnahmen", EuerAmountKind.Revenue);
        AssertLine("17", "Vereinnahmte Umsatzsteuer", EuerAmountKind.CollectedVat);
        AssertLine("27", "Waren, Rohstoffe und Hilfsstoffe einschließlich Nebenkosten", EuerAmountKind.Expense);
        AssertLine("57", "Gezahlte Vorsteuerbeträge", EuerAmountKind.PaidInputVat);
        AssertLine("58", "An das Finanzamt gezahlte Umsatzsteuer", EuerAmountKind.PaidOutputVat);
        AssertLine("60", "Übrige unbeschränkt abziehbare Betriebsausgaben", EuerAmountKind.Expense);

        void AssertLine(string zeile, string wording, EuerAmountKind kind)
        {
            var line = Assert.Single(lines, candidate => candidate.Zeile == zeile);
            Assert.Equal(wording, line.Bezeichnung);
            Assert.Equal(kind, line.AmountKind);
            Assert.Equal(kind is EuerAmountKind.Revenue or EuerAmountKind.CollectedVat
                ? EuerSection.Betriebseinnahmen : EuerSection.Betriebsausgaben, line.Section);
        }
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, 2024)]
    [InlineData(ChartVariant.Skr03, 2025)]
    [InlineData(ChartVariant.Skr04, 2024)]
    [InlineData(ChartVariant.Skr04, 2025)]
    public void Revenue_routes_by_tax_status_without_overlapping_selectors(ChartVariant chart, int jahr)
    {
        foreach (var rate in new[] { 7m, 19m })
        {
            Assert.Equal("15", EuerLineMap.RevenueLineFor(chart, jahr, TaxCategory.S, rate, false).Zeile);
            Assert.Equal("12", EuerLineMap.RevenueLineFor(chart, jahr, TaxCategory.S, rate, true).Zeile);
        }

        foreach (var category in new[] { TaxCategory.AE, TaxCategory.K, TaxCategory.E, TaxCategory.Z, TaxCategory.G, TaxCategory.O })
        {
            Assert.Equal("16", EuerLineMap.RevenueLineFor(chart, jahr, category, 0m, false).Zeile);
            Assert.Equal("12", EuerLineMap.RevenueLineFor(chart, jahr, category, 0m, true).Zeile);
        }
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, "3400", "4980", "1576", "1776", "8400", "8125")]
    [InlineData(ChartVariant.Skr04, "5400", "6300", "1406", "3806", "4400", "4125")]
    public void Account_groups_are_stable_across_form_years(
        ChartVariant chart, string goods, string other, string inputVat, string outputVat, string taxable, string taxFree)
    {
        foreach (var jahr in new[] { 2024, 2025, 2026 })
        {
            Assert.Equal("27", Assert.Single(EuerLineMap.ForAccount(chart, jahr, goods)).Zeile);
            Assert.Equal("60", Assert.Single(EuerLineMap.ForAccount(chart, jahr, other)).Zeile);
            Assert.Equal("57", Assert.Single(EuerLineMap.ForAccount(chart, jahr, inputVat)).Zeile);
            Assert.Equal(new[] { "17", "58" }, EuerLineMap.ForAccount(chart, jahr, outputVat).Select(line => line.Zeile));
            Assert.Equal("15", Assert.Single(EuerLineMap.ForAccount(chart, jahr, taxable)).Zeile);
            Assert.Equal("16", Assert.Single(EuerLineMap.ForAccount(chart, jahr, taxFree)).Zeile);
            Assert.Empty(EuerLineMap.ForAccount(chart, jahr, "9999"));
        }
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, 2026)]
    [InlineData(ChartVariant.Skr04, 2030)]
    public void Later_years_use_the_newest_effective_form(ChartVariant chart, int jahr)
    {
        Assert.Equal(EuerLineMap.ForFiscalYear(chart, 2025), EuerLineMap.ForFiscalYear(chart, jahr));
    }

    [Theory]
    [InlineData(2023)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Years_before_the_first_form_throw_from_all_lookup_paths(int jahr)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EuerLineMap.ForFiscalYear(ChartVariant.Skr03, jahr));
        Assert.Throws<ArgumentOutOfRangeException>(() => EuerLineMap.ForAccount(ChartVariant.Skr03, jahr, "4980"));
        Assert.Throws<ArgumentOutOfRangeException>(() => EuerLineMap.RevenueLineFor(ChartVariant.Skr03, jahr, TaxCategory.S, 19m, true));
    }
}
