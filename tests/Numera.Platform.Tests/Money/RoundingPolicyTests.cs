using System.Text.Json;
using Numera.Platform.Money;
using Xunit;

namespace Numera.Platform.Tests.Money;

/// <summary>
/// Golden-file regression suite locking EN 16931 VAT rounding behavior:
/// half-away-from-zero (kaufmännisch) per VAT category, then the document
/// VAT total = sum of the rounded per-category amounts (never round(grand_total)).
///
/// These fixtures are the regression net the invoicing engine (Phase 3/5)
/// plugs into. The exact half-rounding direction is to be RE-LOCKED against
/// KoSIT-validated reference invoices in the e-invoicing phase
/// (RESEARCH Open Question 1) — AwayFromZero is the correct default here.
/// </summary>
public class RoundingPolicyTests
{
    private static readonly string GoldenDir =
        Path.Combine(AppContext.BaseDirectory, "Money", "goldenfiles");

    private static JsonElement LoadGolden(string fileName)
    {
        var path = Path.Combine(GoldenDir, fileName);
        Assert.True(File.Exists(path), $"Golden file missing: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    // ---- Case 1: Mixed 19% + 7%, per-category-round-then-sum -------------

    [Fact]
    public void MixedRates_EachCategoryRoundsAwayFromZero_ThenDocumentTotalIsSumOfRoundedAmounts()
    {
        var golden = LoadGolden("mixed-rates-19-7.json");

        var buckets = new List<TaxBucket>();
        foreach (var line in golden.GetProperty("lines").EnumerateArray())
        {
            var @base = line.GetProperty("taxableBase").GetDecimal();
            var rate = line.GetProperty("ratePercent").GetDecimal();
            var expectedTax = line.GetProperty("expectedTax").GetDecimal();

            Assert.Equal(expectedTax, RoundingPolicy.RoundTax(@base, rate));
            buckets.Add(new TaxBucket(@base, rate,
                Enum.Parse<TaxCategory>(line.GetProperty("category").GetString()!)));
        }

        var expectedTotal = golden.GetProperty("expectedDocumentVatTotal").GetDecimal();
        var naive = golden.GetProperty("naiveRoundOfGrandTotal").GetDecimal();

        var documentTotal = RoundingPolicy.DocumentVatTotal(buckets);

        Assert.Equal(expectedTotal, documentTotal);
        // Prove per-category-then-sum diverges from round(grand_total).
        Assert.NotEqual(naive, documentTotal);
    }

    // ---- Case 2: Allowance/discount reduces the base BEFORE tax ----------

    [Fact]
    public void Allowance_DiscountedBaseIsTaxedAndRounded()
    {
        var golden = LoadGolden("allowance-discount-19.json");

        var grossBase = golden.GetProperty("grossBase").GetDecimal();
        var allowance = golden.GetProperty("allowance").GetDecimal();
        var rate = golden.GetProperty("ratePercent").GetDecimal();
        var expectedTaxableBase = golden.GetProperty("expectedTaxableBase").GetDecimal();
        var expectedTax = golden.GetProperty("expectedTax").GetDecimal();

        var taxableBase = grossBase - allowance;
        Assert.Equal(expectedTaxableBase, taxableBase);
        Assert.Equal(expectedTax, RoundingPolicy.RoundTax(taxableBase, rate));
    }

    // ---- Case 3: Reverse-charge (AE) €0 line ----------------------------

    [Fact]
    public void ReverseCharge_ZeroRate_TaxIsZero_AndCategoryPreserved()
    {
        var golden = LoadGolden("reverse-charge-ae-zero.json");

        var @base = golden.GetProperty("taxableBase").GetDecimal();
        var rate = golden.GetProperty("ratePercent").GetDecimal();
        var expectedTax = golden.GetProperty("expectedTax").GetDecimal();
        var category = Enum.Parse<TaxCategory>(golden.GetProperty("category").GetString()!);

        Assert.Equal(TaxCategory.AE, category);
        Assert.Equal(expectedTax, RoundingPolicy.RoundTax(@base, rate));

        var total = RoundingPolicy.DocumentVatTotal(new[] { new TaxBucket(@base, rate, category) });
        Assert.Equal(0.00m, total);
    }

    // ---- Case 4: Midpoint proves AwayFromZero (not ToEven) --------------

    [Fact]
    public void Midpoint_RoundsAwayFromZero_NotBankersToEven()
    {
        var golden = LoadGolden("midpoint-away-from-zero.json");

        foreach (var c in golden.GetProperty("cases").EnumerateArray())
        {
            var @base = c.GetProperty("taxableBase").GetDecimal();
            var rate = c.GetProperty("ratePercent").GetDecimal();
            var expectedTax = c.GetProperty("expectedTax").GetDecimal();

            var actual = RoundingPolicy.RoundTax(@base, rate);
            Assert.Equal(expectedTax, actual);
        }

        // Explicit divergence guard: the canonical 0.125 midpoint must NOT be
        // banker's-rounded. AwayFromZero -> 0.13; ToEven -> 0.12.
        Assert.Equal(0.13m, RoundingPolicy.RoundTax(2.50m, 5m));
        Assert.NotEqual(0.12m, RoundingPolicy.RoundTax(2.50m, 5m));
    }

    // ---- Money value object: decimal-only, currency-aware ---------------

    [Fact]
    public void Money_DefaultsToEur_AndKeepsDecimalAmount()
    {
        var m = new Money.Money(19.99m);
        Assert.Equal(19.99m, m.Amount);
        Assert.Equal("EUR", m.CurrencyCode);
    }

    [Fact]
    public void Money_ArithmeticStaysExactInDecimal()
    {
        var a = new Money.Money(0.10m);
        var b = new Money.Money(0.20m);
        var sum = a + b;
        // 0.10 + 0.20 == 0.30 exactly in decimal (would drift in float/double).
        Assert.Equal(0.30m, sum.Amount);
    }

    [Fact]
    public void Money_AddingDifferentCurrencies_Throws()
    {
        var eur = new Money.Money(1m, "EUR");
        var usd = new Money.Money(1m, "USD");
        Assert.Throws<InvalidOperationException>(() => { var _ = eur + usd; });
    }
}
