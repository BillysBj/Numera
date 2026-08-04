using Numera.Platform.Money;

namespace Numera.Modules.Ledger.Seed;

/// <summary>Standard balance-sheet and cash accounts required by posting rules.</summary>
public enum StandardAccountKind
{
    /// <summary>Forderungen aus Lieferungen und Leistungen.</summary>
    Debtor,

    /// <summary>Verbindlichkeiten aus Lieferungen und Leistungen.</summary>
    Creditor,

    /// <summary>Bank account.</summary>
    Bank,

    /// <summary>Cash account.</summary>
    Kasse,
}

/// <summary>
/// Pure SKR03/SKR04 account resolution shared by chart setup and posting engines.
/// </summary>
public static class SkrMapping
{
    /// <summary>Maps an EN-16931 revenue tax category to its revenue, output-tax and BU accounts.</summary>
    public static (string RevenueAccount, string? UstAccount, Steuerschluessel Key) RevenueMapping(
        ChartVariant variant,
        TaxCategory taxCategory,
        decimal ratePercent)
    {
        if (taxCategory == TaxCategory.S)
        {
            return ratePercent switch
            {
                19m => variant switch
                {
                    ChartVariant.Skr03 => ("8400", "1776", Steuerschluessel.Ust19),
                    ChartVariant.Skr04 => ("4400", "3806", Steuerschluessel.Ust19),
                    _ => throw UnsupportedVariant(variant),
                },
                7m => variant switch
                {
                    ChartVariant.Skr03 => ("8300", "1771", Steuerschluessel.Ust7),
                    ChartVariant.Skr04 => ("4300", "3801", Steuerschluessel.Ust7),
                    _ => throw UnsupportedVariant(variant),
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(ratePercent), ratePercent, "Standard-rated revenue supports 7% or 19% VAT."),
            };
        }

        return taxCategory switch
        {
            TaxCategory.AE => TaxFreeRevenue(variant, Steuerschluessel.ReverseChargeNoTax),
            TaxCategory.K => variant switch
            {
                ChartVariant.Skr03 => ("8125", null, Steuerschluessel.TaxFreeWithInput),
                ChartVariant.Skr04 => ("4125", null, Steuerschluessel.TaxFreeWithInput),
                _ => throw UnsupportedVariant(variant),
            },
            TaxCategory.E or TaxCategory.Z or TaxCategory.G or TaxCategory.O =>
                TaxFreeRevenue(variant, Steuerschluessel.None),
            _ => throw new ArgumentOutOfRangeException(nameof(taxCategory), taxCategory, "Unsupported tax category."),
        };
    }

    /// <summary>
    /// Maps revenue while honoring the §19 Kleinunternehmer path, which always uses
    /// the tax-free revenue account without an output-tax leg.
    /// </summary>
    public static (string RevenueAccount, string? UstAccount, Steuerschluessel Key) RevenueMapping(
        ChartVariant variant,
        TaxCategory taxCategory,
        decimal ratePercent,
        bool isKleinunternehmer) =>
        isKleinunternehmer
            ? TaxFreeRevenue(variant, Steuerschluessel.None)
            : RevenueMapping(variant, taxCategory, ratePercent);

    /// <summary>Maps an expense VAT rate to its expense, input-tax and BU accounts.</summary>
    public static (string ExpenseAccount, string? VorsteuerAccount, Steuerschluessel Key) ExpenseMapping(
        ChartVariant variant,
        decimal ratePercent) =>
        (variant, ratePercent) switch
        {
            (ChartVariant.Skr03, 0m) => ("4980", null, Steuerschluessel.None),
            (ChartVariant.Skr03, 19m) => ("4980", "1576", Steuerschluessel.Vst19),
            (ChartVariant.Skr03, 7m) => ("4980", "1571", Steuerschluessel.Vst7),
            (ChartVariant.Skr04, 0m) => ("6300", null, Steuerschluessel.None),
            (ChartVariant.Skr04, 19m) => ("6300", "1406", Steuerschluessel.Vst19),
            (ChartVariant.Skr04, 7m) => ("6300", "1401", Steuerschluessel.Vst7),
            (ChartVariant.Skr03 or ChartVariant.Skr04, _) => throw new ArgumentOutOfRangeException(
                nameof(ratePercent), ratePercent, "Expenses support 0%, 7% or 19% VAT."),
            _ => throw UnsupportedVariant(variant),
        };

    /// <summary>Returns a standard debtor, creditor, bank or cash account.</summary>
    public static string StandardAccount(ChartVariant variant, StandardAccountKind kind) =>
        (variant, kind) switch
        {
            (ChartVariant.Skr03, StandardAccountKind.Debtor) => "1400",
            (ChartVariant.Skr03, StandardAccountKind.Creditor) => "1600",
            (ChartVariant.Skr03, StandardAccountKind.Bank) => "1200",
            (ChartVariant.Skr03, StandardAccountKind.Kasse) => "1000",
            (ChartVariant.Skr04, StandardAccountKind.Debtor) => "1200",
            (ChartVariant.Skr04, StandardAccountKind.Creditor) => "3300",
            (ChartVariant.Skr04, StandardAccountKind.Bank) => "1800",
            (ChartVariant.Skr04, StandardAccountKind.Kasse) => "1600",
            (ChartVariant.Skr03 or ChartVariant.Skr04, _) => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "Unsupported standard account kind."),
            _ => throw UnsupportedVariant(variant),
        };

    private static (string RevenueAccount, string? UstAccount, Steuerschluessel Key) TaxFreeRevenue(
        ChartVariant variant,
        Steuerschluessel key) =>
        variant switch
        {
            ChartVariant.Skr03 => ("8200", null, key),
            ChartVariant.Skr04 => ("4200", null, key),
            _ => throw UnsupportedVariant(variant),
        };

    private static ArgumentOutOfRangeException UnsupportedVariant(ChartVariant variant) =>
        new(nameof(variant), variant, "Only SKR03 and SKR04 are supported.");
}
