using NodaMoney;

namespace Numera.Modules.Sales.Money;

/// <summary>Restricts foreign-currency invoicing to ISO 4217 currencies with two minor units.</summary>
public static class CurrencyScope
{
    /// <summary>The validation reason returned for currencies outside the supported scope.</summary>
    public const string UnsupportedReason = "Währung nicht unterstützt (nur 2-Nachkommastellen-Währungen)";

    /// <summary>Returns whether <paramref name="currencyCode"/> is known and has exactly two decimal digits.</summary>
    public static bool IsSupported(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return false;
        }

        try
        {
            return Currency.FromCode(currencyCode.Trim().ToUpperInvariant()).MinimalAmount == 0.01m;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
