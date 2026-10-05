using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class UgRuecklageCalculatorTests
{
    [Theory]
    [InlineData(20000, 4000, true, 16000, 4000)]
    [InlineData(-1000, 0, true, 0, 0)]
    [InlineData(4000, 4000, true, 0, 0)]
    [InlineData(1000, 4000, true, 0, 0)]
    [InlineData(20000, 4000, false, 16000, 0)]
    [InlineData(100000, 0, true, 100000, 25000)]
    [InlineData(1.02, 0, true, 1.02, 0.26)]
    [InlineData(1.0199, 0, true, 1.0199, 0.25)]
    public void Computes_statutory_allocation(decimal profit, decimal loss, bool active, decimal basis, decimal reserve)
    {
        Assert.Equal(new UgRuecklageResult(basis, reserve), new UgRuecklageCalculator().Compute(profit, loss, active));
    }

    [Fact]
    public void Negative_loss_carryforward_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UgRuecklageCalculator().Compute(20000m, -1m, true));

    [Theory]
    [InlineData(ChartVariant.Skr03, "0860", "0846")]
    [InlineData(ChartVariant.Skr04, "2970", "2929")]
    public void Mapping_and_posting_use_two_balanced_equity_legs(ChartVariant chart, string debitNumber, string creditNumber)
    {
        Assert.Equal(debitNumber, SkrMapping.GewinnvortragAccount(chart));
        Assert.Equal(creditNumber, SkrMapping.UgRuecklageAccount(chart));
        var tenant = Guid.CreateVersion7();
        var debit = Guid.CreateVersion7();
        var credit = Guid.CreateVersion7();
        var postings = new UgRuecklagePostingSource(tenant, debit, credit, 4000m).BuildPostings();
        Assert.Equal(2, postings.Count);
        Assert.All(postings, posting =>
        {
            Assert.Equal(tenant, posting.TenantId);
            Assert.Equal(4000m, posting.Amount);
            Assert.Null(posting.TaxCategory);
        });
        Assert.Equal(debit, Assert.Single(postings, posting => posting.Direction == PostingDirection.Debit).AccountId);
        Assert.Equal(credit, Assert.Single(postings, posting => posting.Direction == PostingDirection.Credit).AccountId);
    }
}
