using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class AfaCalculatorTests
{
    private readonly AfaCalculator _calculator = new();

    [Fact]
    public void Linear_full_year_includes_incidental_costs()
    {
        var asset = Asset(1);
        asset.AnschaffungsnebenkostenNetto = 600m;
        Assert.Equal(new AfaResult(2200m, 4400m), _calculator.Compute(asset, 2026, []));
    }

    [Theory]
    [InlineData(7, 1000)]
    [InlineData(12, 166.67)]
    public void In_service_month_controls_first_year(int month, decimal expected)
    {
        var asset = Asset(month);
        Assert.Equal(expected, _calculator.Compute(asset, 2026, []).Betrag);
        Assert.Equal(0m, _calculator.Compute(asset, 2025, []).Betrag);
    }

    [Theory]
    [InlineData(1, 2028)]
    [InlineData(7, 2029)]
    public void Final_year_closes_exactly_despite_rounding(int month, int finalYear)
    {
        var asset = Asset(month);
        asset.AnschaffungskostenNetto = 1000m;
        var booked = new List<AfaBuchung>();
        for (var year = 2026; year <= finalYear; year++)
        {
            var result = _calculator.Compute(asset, year, booked);
            booked.Add(Booking(asset, year, result.Betrag));
            Assert.True(result.Restbuchwert >= 0m);
            if (year == finalYear)
            {
                Assert.Equal(0m, result.Restbuchwert);
            }
        }

        Assert.Equal(1000m, booked.Sum(entry => entry.Betrag));
        Assert.Equal(0m, _calculator.Compute(asset, finalYear + 1, booked).Betrag);
    }

    [Fact]
    public void Sub_cent_final_remainder_never_exceeds_cost_and_closes_exactly()
    {
        var asset = Asset(1);
        asset.AnschaffungskostenNetto = 1000.004m;
        var result = _calculator.Compute(asset, 2028, [Booking(asset, 2026, 333.33m), Booking(asset, 2027, 333.33m)]);
        Assert.Equal(333.344m, result.Betrag);
        Assert.Equal(0m, result.Restbuchwert);
    }

    [Fact]
    public void Gwg_is_full_in_service_year_only_and_repeat_is_noop()
    {
        var asset = Asset(12);
        asset.Methode = AfaMethode.GwgSofort;
        Assert.Equal(new AfaResult(6000m, 0m), _calculator.Compute(asset, 2026, []));
        Assert.Equal(0m, _calculator.Compute(asset, 2027, []).Betrag);
        Assert.Equal(new AfaResult(0m, 0m), _calculator.Compute(asset, 2026, [Booking(asset, 2026, 6000m)]));
    }

    [Theory]
    [InlineData(2026, 7, 10, 666.67)]
    [InlineData(2027, 7, 3, 500)]
    [InlineData(2029, 7, 3, 500)]
    public void Disposal_includes_only_months_in_service(int year, int startMonth, int disposalMonth, decimal expected)
    {
        var asset = Asset(startMonth);
        asset.AbgangsDatum = new(year, disposalMonth, 10);
        asset.AbgangsArt = AssetDisposal.Verkauf;
        Assert.Equal(expected, _calculator.Compute(asset, year, []).Betrag);
        Assert.Equal(0m, _calculator.Compute(asset, year + 1, []).Betrag);
    }

    [Fact]
    public void Already_booked_amount_caps_depreciation_and_foreign_rows_are_ignored()
    {
        var asset = Asset(1);
        Assert.Equal(new AfaResult(100m, 0m), _calculator.Compute(asset, 2027,
            [Booking(asset, 2026, 5900m), Booking(Asset(1), 2026, 9999m)]));
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, "4830")]
    [InlineData(ChartVariant.Skr04, "6220")]
    public void Account_mapping_and_posting_directions(ChartVariant chart, string number)
    {
        Assert.Equal(number, SkrMapping.DepreciationExpenseAccount(chart));
        var tenant = Guid.CreateVersion7();
        var expense = Guid.CreateVersion7();
        var asset = Guid.CreateVersion7();
        var postings = new AfaPostingSource(tenant, expense, asset, 1000m).BuildPostings();
        Assert.Equal(2, postings.Count);
        Assert.All(postings, posting => { Assert.Equal(tenant, posting.TenantId); Assert.Equal(1000m, posting.Amount); });
        Assert.Equal(expense, Assert.Single(postings, posting => posting.Direction == PostingDirection.Debit).AccountId);
        Assert.Equal(asset, Assert.Single(postings, posting => posting.Direction == PostingDirection.Credit).AccountId);
    }

    private static FixedAsset Asset(int month) => new()
    {
        TenantId = Guid.CreateVersion7(), Bezeichnung = "Maschine", AnlagekontoNumber = "0400", AbschreibungskontoNumber = "4830",
        AnschaffungsDatum = new(2026, 1, 1), InbetriebnahmeDatum = new(2026, month, 1),
        AnschaffungskostenNetto = 6000m, NutzungsdauerJahre = 3,
    };

    private static AfaBuchung Booking(FixedAsset asset, int year, decimal amount) => new()
    {
        TenantId = asset.TenantId, FixedAssetId = asset.Id, Jahr = year, Betrag = amount,
    };
}
