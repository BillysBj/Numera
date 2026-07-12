using System.Text.Json;

using Numera.Modules.Sales.Vat;
using Numera.Platform.Money;

using Xunit;

namespace Numera.Platform.Tests.Sales;

/// <summary>
/// Golden-file regression suite for <see cref="VatCalculationService"/> — the single
/// authority that turns invoice lines into EN 16931 BG-23 VAT breakdown rows.
///
/// The fixtures lock the whole arithmetic + legal-text core of INV-04:
/// per-(category, rate) bucketing, per-category half-away-from-zero rounding delegated
/// to <see cref="RoundingPolicy"/> (round each category, then SUM — never round the
/// grand total), zero-tax + exemption code (BT-121) + mandatory German Pflichttext
/// (BT-120) for §13b / intra-EU / export, and the document-wide Kleinunternehmer §19
/// override.
///
/// The exact VATEX-EU-* code strings are MEDIUM confidence (RESEARCH Open Q2) and are
/// re-verified against the official VATEX list in Phase 5; these fixtures are the
/// regression net guarding both the codes and the legally load-bearing note texts.
/// </summary>
public class VatCalculationServiceTests
{
    private static readonly string GoldenDir =
        Path.Combine(AppContext.BaseDirectory, "Sales", "goldenfiles");

    private static JsonElement LoadGolden(string fileName)
    {
        var path = Path.Combine(GoldenDir, fileName);
        Assert.True(File.Exists(path), $"Golden file missing: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static IReadOnlyList<VatLineInput> ReadLines(JsonElement golden)
    {
        var lines = new List<VatLineInput>();
        foreach (var l in golden.GetProperty("lines").EnumerateArray())
        {
            lines.Add(new VatLineInput(
                Enum.Parse<TaxCategory>(l.GetProperty("category").GetString()!),
                l.GetProperty("ratePercent").GetDecimal(),
                l.GetProperty("lineNetAmount").GetDecimal()));
        }

        return lines;
    }

    private static string? OptString(JsonElement row, string name)
    {
        var prop = row.GetProperty(name);
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
    }

    [Theory]
    [InlineData("single-standard-19.json")]
    [InlineData("mixed-rates-19-7.json")]
    [InlineData("midpoint-away-from-zero-19.json")]
    [InlineData("per-category-then-sum-19-7.json")]
    [InlineData("reverse-charge-ae.json")]
    [InlineData("intra-eu-k.json")]
    [InlineData("export-g.json")]
    [InlineData("kleinunternehmer.json")]
    public void Golden_ProducesExpectedBreakdownAndDocumentTotal(string fileName)
    {
        var golden = LoadGolden(fileName);
        var lines = ReadLines(golden);
        var isKleinunternehmer = golden.GetProperty("isKleinunternehmer").GetBoolean();

        var rows = VatCalculationService.Calculate(lines, isKleinunternehmer);

        var expectedRows = golden.GetProperty("expectedRows");
        Assert.Equal(expectedRows.GetArrayLength(), rows.Count);

        foreach (var expected in expectedRows.EnumerateArray())
        {
            var category = Enum.Parse<TaxCategory>(expected.GetProperty("category").GetString()!);
            var rate = expected.GetProperty("ratePercent").GetDecimal();

            var actual = rows.SingleOrDefault(r => r.Category == category && r.RatePercent == rate);
            Assert.True(
                actual != default,
                $"[{fileName}] no breakdown row for category {category} @ {rate}%");

            Assert.Equal(expected.GetProperty("taxableBase").GetDecimal(), actual.TaxableBase);
            Assert.Equal(expected.GetProperty("taxAmount").GetDecimal(), actual.TaxAmount);
            Assert.Equal(OptString(expected, "exemptionCode"), actual.ExemptionCode);
            Assert.Equal(OptString(expected, "exemptionText"), actual.ExemptionText);
        }

        var expectedTotal = golden.GetProperty("expectedDocumentVatTotal").GetDecimal();
        Assert.Equal(expectedTotal, VatCalculationService.DocumentVatTotal(rows));
    }

    // ---- Explicit half-away-from-zero divergence guard (per category) ------

    [Fact]
    public void Midpoint_RoundsCategoryTaxAwayFromZero_NotBankersToEven()
    {
        var golden = LoadGolden("midpoint-away-from-zero-19.json");
        var rows = VatCalculationService.Calculate(ReadLines(golden), false);

        var naiveToEven = golden.GetProperty("naiveToEvenTax").GetDecimal();
        var actual = Assert.Single(rows);

        // 1.50 @ 19% = 0.285 -> AwayFromZero 0.29, ToEven 0.28.
        Assert.Equal(0.29m, actual.TaxAmount);
        Assert.NotEqual(naiveToEven, actual.TaxAmount);
    }

    // ---- Document total = sum-of-rounded, never round(grand_total) ---------

    [Fact]
    public void DocumentTotal_IsSumOfRoundedCategories_NotRoundOfGrandTotal()
    {
        var golden = LoadGolden("per-category-then-sum-19-7.json");
        var rows = VatCalculationService.Calculate(ReadLines(golden), false);

        var expectedTotal = golden.GetProperty("expectedDocumentVatTotal").GetDecimal();
        var naive = golden.GetProperty("naiveRoundOfGrandTotal").GetDecimal();

        var total = VatCalculationService.DocumentVatTotal(rows);

        Assert.Equal(expectedTotal, total);        // 0.40 = 0.29 + 0.11
        Assert.NotEqual(naive, total);             // NOT round(0.285 + 0.105) = 0.39
    }

    // ---- Kleinunternehmer is a document-wide override ----------------------

    [Fact]
    public void Kleinunternehmer_CollapsesAllLinesIntoSingleExemptRow_RegardlessOfCategory()
    {
        // Two taxed lines at different rates; the §19 flag must override BOTH into one
        // category-E / rate-0 / zero-tax bucket carrying the sum of the nets.
        var lines = new[]
        {
            new VatLineInput(TaxCategory.S, 19m, 100.00m),
            new VatLineInput(TaxCategory.S, 7m, 50.00m),
        };

        var rows = VatCalculationService.Calculate(lines, isKleinunternehmer: true);

        var row = Assert.Single(rows);
        Assert.Equal(TaxCategory.E, row.Category);
        Assert.Equal(0m, row.RatePercent);
        Assert.Equal(150.00m, row.TaxableBase);
        Assert.Equal(0m, row.TaxAmount);
        Assert.Equal("VATEX-EU-D", row.ExemptionCode);
        Assert.Equal("Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG", row.ExemptionText);
        Assert.Equal(0m, VatCalculationService.DocumentVatTotal(rows));
    }
}
