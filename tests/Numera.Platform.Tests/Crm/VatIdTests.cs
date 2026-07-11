using Numera.Modules.Crm;

using Xunit;

namespace Numera.Platform.Tests.Crm;

/// <summary>
/// Unit tests for the offline USt-IdNr / VAT-ID validator. Everything here is a PURE
/// function — no network, no VIES — so the suite is deterministic and instant.
/// </summary>
public sealed class VatIdTests
{
    [Theory]
    [InlineData("DE136695976")] // well-known valid German VAT ID (checksum-correct)
    [InlineData("DE811907980")] // valid German VAT ID (checksum-correct)
    [InlineData(" de 811 907 980 ")] // normalisation: spaces + lowercase are tolerated
    public void IsValidDe_accepts_wellformed_checksum_correct_ids(string raw)
    {
        Assert.True(VatId.IsValidDe(raw));
    }

    [Fact]
    public void IsValidDe_rejects_wrong_length()
    {
        Assert.False(VatId.IsValidDe("DE12345678"));   // 8 digits
        Assert.False(VatId.IsValidDe("DE1234567890")); // 10 digits
    }

    [Fact]
    public void IsValidDe_rejects_leading_zero()
    {
        // Format requires the first digit after DE to be 1-9.
        Assert.False(VatId.IsValidDe("DE011907980"));
    }

    [Fact]
    public void IsValidDe_rejects_bad_checksum()
    {
        // Flip the check digit of a valid id (…980 -> …981).
        Assert.False(VatId.IsValidDe("DE811907981"));
    }

    [Fact]
    public void IsValidDe_rejects_non_DE_prefix()
    {
        Assert.False(VatId.IsValidDe("ATU13585627"));
        Assert.False(VatId.IsValidDe("FR40303265045"));
    }

    [Fact]
    public void IsValidDe_rejects_null_or_empty()
    {
        Assert.False(VatId.IsValidDe(null));
        Assert.False(VatId.IsValidDe(""));
        Assert.False(VatId.IsValidDe("   "));
    }

    [Theory]
    [InlineData("ATU13585627")]   // Austria: U + 8
    [InlineData("FR40303265045")] // France: 11
    [InlineData("NL123456789B01")] // Netherlands: 12
    [InlineData("DE811907980")]   // DE plausible by shape too
    public void IsPlausibleEu_accepts_known_prefixes_with_expected_length(string raw)
    {
        Assert.True(VatId.IsPlausibleEu(raw));
    }

    [Fact]
    public void IsPlausibleEu_rejects_unknown_country_prefix()
    {
        Assert.False(VatId.IsPlausibleEu("US123456789"));
        Assert.False(VatId.IsPlausibleEu("ZZ123456789"));
    }

    [Fact]
    public void IsPlausibleEu_rejects_wrong_length_for_country()
    {
        Assert.False(VatId.IsPlausibleEu("DE1234"));      // DE expects 9-digit body
        Assert.False(VatId.IsPlausibleEu("FR1"));          // FR expects 11
    }

    [Fact]
    public void Normalize_strips_whitespace_and_uppercases()
    {
        Assert.Equal("DE811907980", VatId.Normalize(" de 811 907 980 "));
        Assert.Equal(string.Empty, VatId.Normalize(null));
        Assert.Equal(string.Empty, VatId.Normalize("   "));
    }
}
