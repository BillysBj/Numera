using Numera.Modules.Catalog;

using Xunit;

namespace Numera.Platform.Tests.Catalog;

/// <summary>
/// Unit tests for the curated UN/ECE Rec 20 <see cref="UnitOfMeasure"/> helper.
/// Everything here is PURE (no DB, no network) so the suite is deterministic and
/// instant — it just guards the allowed code set and the per-kind defaults.
/// </summary>
public sealed class UnitOfMeasureTests
{
    [Theory]
    [InlineData("C62")]
    [InlineData("HUR")]
    [InlineData("DAY")]
    [InlineData("KGM")]
    [InlineData("KWH")]
    public void IsValid_accepts_curated_codes(string code)
    {
        Assert.True(UnitOfMeasure.IsValid(code));
    }

    [Theory]
    [InlineData("BOGUS")]
    [InlineData("c62")] // case-sensitive: the canonical code is upper-case
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_rejects_unknown_or_malformed_codes(string? code)
    {
        Assert.False(UnitOfMeasure.IsValid(code));
    }

    [Fact]
    public void DefaultFor_product_is_piece_C62()
    {
        Assert.Equal("C62", UnitOfMeasure.DefaultFor(CatalogItemKind.Product));
    }

    [Fact]
    public void DefaultFor_service_is_hour_HUR()
    {
        Assert.Equal("HUR", UnitOfMeasure.DefaultFor(CatalogItemKind.Service));
    }

    [Fact]
    public void Codes_contains_the_curated_set()
    {
        var expected = new[] { "C62", "H87", "HUR", "DAY", "MON", "KGM", "MTR", "MTK", "LTR", "KWH" };
        Assert.Equal(expected, UnitOfMeasure.Codes);
    }
}
