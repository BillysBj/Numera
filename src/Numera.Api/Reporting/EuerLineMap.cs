using Numera.Modules.Ledger;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>The Anlage-EÜR section containing a mapped line.</summary>
public enum EuerSection
{
    /// <summary>Betriebseinnahmen.</summary>
    Betriebseinnahmen,

    /// <summary>Betriebsausgaben.</summary>
    Betriebsausgaben,
}

/// <summary>The accounting amount represented by an Anlage-EÜR line.</summary>
public enum EuerAmountKind
{
    /// <summary>Recognized revenue excluding VAT for a Regelunternehmer.</summary>
    Revenue,

    /// <summary>VAT collected with recognized revenue.</summary>
    CollectedVat,

    /// <summary>Recognized expense excluding deductible input VAT for a Regelunternehmer.</summary>
    Expense,

    /// <summary>Deductible input VAT paid to suppliers.</summary>
    PaidInputVat,

    /// <summary>VAT paid to the tax office.</summary>
    PaidOutputVat,
}

/// <summary>Frozen tax-breakdown characteristics selecting a revenue line.</summary>
public sealed record EuerRecognitionSelector(
    TaxCategory TaxCategory,
    decimal? TaxRatePercent)
{
    /// <summary>Whether a recognized tax bucket belongs to this selector.</summary>
    public bool Matches(TaxCategory taxCategory, decimal taxRatePercent) =>
        TaxCategory == taxCategory
        && (TaxRatePercent is null || TaxRatePercent == taxRatePercent);
}

/// <summary>One checked-in account-to-Anlage-EÜR line group.</summary>
public sealed record EuerLineDefinition(
    string Zeile,
    string Bezeichnung,
    EuerSection Section,
    EuerAmountKind AmountKind,
    IReadOnlyList<string> AccountNumbers,
    IReadOnlyList<EuerRecognitionSelector> RecognitionSelectors);

/// <summary>
/// Checked-in SKR03/SKR04 account mappings for the Anlage-EÜR line groups supported
/// by Numera's current booking paths.
/// </summary>
/// <remarks>
/// AfA/Abschreibungen are intentionally out of scope. Durable assets are depreciated
/// rather than expensed when paid; that non-cash-basis path is deferred beyond this MVP.
/// </remarks>
public static class EuerLineMap
{
    /// <summary>Taxable net operating revenue.</summary>
    public const string TaxableRevenueZeile = "14";

    /// <summary>Tax-exempt and non-taxable operating revenue.</summary>
    public const string TaxFreeRevenueZeile = "15";

    /// <summary>Collected VAT.</summary>
    public const string CollectedVatZeile = "17";

    /// <summary>Goods, raw materials and consumables.</summary>
    public const string GoodsExpenseZeile = "25";

    /// <summary>Paid deductible input VAT.</summary>
    public const string PaidInputVatZeile = "55";

    /// <summary>VAT paid to the tax office.</summary>
    public const string PaidOutputVatZeile = "56";

    /// <summary>Other fully deductible operating expenses.</summary>
    public const string OtherExpenseZeile = "57";

    private static readonly IReadOnlyDictionary<ChartVariant, IReadOnlyList<EuerLineDefinition>> Mappings =
        new Dictionary<ChartVariant, IReadOnlyList<EuerLineDefinition>>
        {
            [ChartVariant.Skr03] =
            [
                RevenueDefinition(["8400", "8300"]),
                TaxFreeRevenueDefinition(["8200", "8125"]),
                CollectedVatDefinition(["1776", "1771"]),
                GoodsExpenseDefinition(["3400"]),
                OtherExpenseDefinition(["4980"]),
                PaidInputVatDefinition(["1576", "1571"]),
                PaidOutputVatDefinition(["1776", "1771"]),
            ],
            [ChartVariant.Skr04] =
            [
                RevenueDefinition(["4400", "4300"]),
                TaxFreeRevenueDefinition(["4200", "4125"]),
                CollectedVatDefinition(["3806", "3801"]),
                GoodsExpenseDefinition(["5400"]),
                OtherExpenseDefinition(["6300"]),
                PaidInputVatDefinition(["1406", "1401"]),
                PaidOutputVatDefinition(["3806", "3801"]),
            ],
        };

    /// <summary>Returns the checked-in line definitions for one chart variant.</summary>
    public static IReadOnlyList<EuerLineDefinition> ForChart(ChartVariant chartVariant) =>
        Mappings.TryGetValue(chartVariant, out var definitions)
            ? definitions
            : throw new ArgumentOutOfRangeException(
                nameof(chartVariant),
                chartVariant,
                "Only SKR03 and SKR04 are supported.");

    /// <summary>Returns every EÜR group fed by an account number.</summary>
    public static IReadOnlyList<EuerLineDefinition> ForAccount(
        ChartVariant chartVariant,
        string accountNumber)
    {
        ArgumentNullException.ThrowIfNull(accountNumber);
        return ForChart(chartVariant)
            .Where(definition => definition.AccountNumbers.Contains(accountNumber, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Resolves a frozen cash-recognition bucket to its revenue line. A
    /// Kleinunternehmer's receipts use the chart's tax-free revenue group because the
    /// finalized invoice carries no output VAT.
    /// </summary>
    public static EuerLineDefinition RevenueLineFor(
        ChartVariant chartVariant,
        TaxCategory taxCategory,
        decimal taxRatePercent,
        bool isKleinunternehmer)
    {
        var definitions = ForChart(chartVariant);
        if (isKleinunternehmer)
        {
            return definitions.Single(definition =>
                definition.Section == EuerSection.Betriebseinnahmen
                && definition.Zeile == TaxFreeRevenueZeile);
        }

        return definitions.SingleOrDefault(definition =>
                definition.AmountKind == EuerAmountKind.Revenue
                && definition.RecognitionSelectors.Any(selector =>
                    selector.Matches(taxCategory, taxRatePercent)))
            ?? throw new InvalidOperationException(
                $"No EÜR revenue mapping exists for tax category {taxCategory} at {taxRatePercent}%.");
    }

    private static EuerLineDefinition RevenueDefinition(IReadOnlyList<string> accounts) =>
        new(
            TaxableRevenueZeile,
            "Umsatzsteuerpflichtige Betriebseinnahmen (netto)",
            EuerSection.Betriebseinnahmen,
            EuerAmountKind.Revenue,
            accounts,
            [
                new EuerRecognitionSelector(TaxCategory.S, 19m),
                new EuerRecognitionSelector(TaxCategory.S, 7m),
            ]);

    private static EuerLineDefinition TaxFreeRevenueDefinition(IReadOnlyList<string> accounts) =>
        new(
            TaxFreeRevenueZeile,
            "Steuerfreie und nicht steuerbare Betriebseinnahmen",
            EuerSection.Betriebseinnahmen,
            EuerAmountKind.Revenue,
            accounts,
            [
                new EuerRecognitionSelector(TaxCategory.AE, null),
                new EuerRecognitionSelector(TaxCategory.K, null),
                new EuerRecognitionSelector(TaxCategory.E, null),
                new EuerRecognitionSelector(TaxCategory.Z, null),
                new EuerRecognitionSelector(TaxCategory.G, null),
                new EuerRecognitionSelector(TaxCategory.O, null),
            ]);

    private static EuerLineDefinition CollectedVatDefinition(IReadOnlyList<string> accounts) =>
        new(
            CollectedVatZeile,
            "Vereinnahmte Umsatzsteuer",
            EuerSection.Betriebseinnahmen,
            EuerAmountKind.CollectedVat,
            accounts,
            []);

    private static EuerLineDefinition GoodsExpenseDefinition(IReadOnlyList<string> accounts) =>
        new(
            GoodsExpenseZeile,
            "Waren, Rohstoffe und Hilfsstoffe einschließlich Nebenkosten",
            EuerSection.Betriebsausgaben,
            EuerAmountKind.Expense,
            accounts,
            []);

    private static EuerLineDefinition OtherExpenseDefinition(IReadOnlyList<string> accounts) =>
        new(
            OtherExpenseZeile,
            "Sonstige unbeschränkt abziehbare Betriebsausgaben",
            EuerSection.Betriebsausgaben,
            EuerAmountKind.Expense,
            accounts,
            []);

    private static EuerLineDefinition PaidInputVatDefinition(IReadOnlyList<string> accounts) =>
        new(
            PaidInputVatZeile,
            "Gezahlte Vorsteuerbeträge",
            EuerSection.Betriebsausgaben,
            EuerAmountKind.PaidInputVat,
            accounts,
            []);

    private static EuerLineDefinition PaidOutputVatDefinition(IReadOnlyList<string> accounts) =>
        new(
            PaidOutputVatZeile,
            "An das Finanzamt gezahlte Umsatzsteuer",
            EuerSection.Betriebsausgaben,
            EuerAmountKind.PaidOutputVat,
            accounts,
            []);
}
