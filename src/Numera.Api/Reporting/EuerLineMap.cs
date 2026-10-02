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
    /// <summary>Gross operating revenue under §19 UStG.</summary>
    public const string KleinunternehmerRevenueZeile = "12";

    /// <summary>Taxable net operating revenue.</summary>
    public const string TaxableRevenueZeile = "15";

    /// <summary>Tax-exempt and non-taxable operating revenue.</summary>
    public const string TaxFreeRevenueZeile = "16";

    /// <summary>Collected VAT.</summary>
    public const string CollectedVatZeile = "17";

    /// <summary>Goods, raw materials and consumables.</summary>
    public const string GoodsExpenseZeile = "27";

    /// <summary>Paid deductible input VAT.</summary>
    public const string PaidInputVatZeile = "57";

    /// <summary>VAT paid to the tax office.</summary>
    public const string PaidOutputVatZeile = "58";

    /// <summary>Other fully deductible operating expenses.</summary>
    public const string OtherExpenseZeile = "60";

    private enum LineGroup
    {
        KleinunternehmerRevenue,
        TaxableRevenue,
        TaxFreeRevenue,
        CollectedVat,
        GoodsExpense,
        OtherExpense,
        PaidInputVat,
        PaidOutputVat,
    }

    private sealed record AccountGroup(
        LineGroup Group,
        EuerAmountKind AmountKind,
        IReadOnlyList<string> AccountNumbers,
        IReadOnlyList<EuerRecognitionSelector> RecognitionSelectors);

    // Account groups are independent of the fiscal-year form's numbering and wording.
    private static readonly IReadOnlyList<EuerRecognitionSelector> TaxableSelectors =
    [
        new(TaxCategory.S, 19m),
        new(TaxCategory.S, 7m),
    ];

    private static readonly IReadOnlyList<EuerRecognitionSelector> TaxFreeSelectors =
    [
        new(TaxCategory.AE, null),
        new(TaxCategory.K, null),
        new(TaxCategory.E, null),
        new(TaxCategory.Z, null),
        new(TaxCategory.G, null),
        new(TaxCategory.O, null),
    ];

    private static readonly IReadOnlyDictionary<ChartVariant, IReadOnlyList<AccountGroup>> Mappings =
        new Dictionary<ChartVariant, IReadOnlyList<AccountGroup>>
        {
            [ChartVariant.Skr03] =
            [
                new(LineGroup.KleinunternehmerRevenue, EuerAmountKind.Revenue, ["8200"], []),
                new(LineGroup.TaxableRevenue, EuerAmountKind.Revenue, ["8400", "8300"], TaxableSelectors),
                new(LineGroup.TaxFreeRevenue, EuerAmountKind.Revenue, ["8200", "8125"], TaxFreeSelectors),
                new(LineGroup.CollectedVat, EuerAmountKind.CollectedVat, ["1776", "1771"], []),
                new(LineGroup.GoodsExpense, EuerAmountKind.Expense, ["3400"], []),
                new(LineGroup.OtherExpense, EuerAmountKind.Expense, ["4980"], []),
                new(LineGroup.PaidInputVat, EuerAmountKind.PaidInputVat, ["1576", "1571"], []),
                new(LineGroup.PaidOutputVat, EuerAmountKind.PaidOutputVat, ["1776", "1771"], []),
            ],
            [ChartVariant.Skr04] =
            [
                new(LineGroup.KleinunternehmerRevenue, EuerAmountKind.Revenue, ["4200"], []),
                new(LineGroup.TaxableRevenue, EuerAmountKind.Revenue, ["4400", "4300"], TaxableSelectors),
                new(LineGroup.TaxFreeRevenue, EuerAmountKind.Revenue, ["4200", "4125"], TaxFreeSelectors),
                new(LineGroup.CollectedVat, EuerAmountKind.CollectedVat, ["3806", "3801"], []),
                new(LineGroup.GoodsExpense, EuerAmountKind.Expense, ["5400"], []),
                new(LineGroup.OtherExpense, EuerAmountKind.Expense, ["6300"], []),
                new(LineGroup.PaidInputVat, EuerAmountKind.PaidInputVat, ["1406", "1401"], []),
                new(LineGroup.PaidOutputVat, EuerAmountKind.PaidOutputVat, ["3806", "3801"], []),
            ],
        };

    private static readonly IReadOnlyDictionary<LineGroup, (string Zeile, string Bezeichnung)> Form2024And2025 =
        new Dictionary<LineGroup, (string, string)>
        {
            [LineGroup.KleinunternehmerRevenue] =
                (KleinunternehmerRevenueZeile, "Betriebseinnahmen als umsatzsteuerlicher Kleinunternehmer"),
            [LineGroup.TaxableRevenue] =
                (TaxableRevenueZeile, "Umsatzsteuerpflichtige Betriebseinnahmen (netto)"),
            [LineGroup.TaxFreeRevenue] =
                (TaxFreeRevenueZeile, "Umsatzsteuerfreie, nicht umsatzsteuerbare Betriebseinnahmen"),
            [LineGroup.CollectedVat] = (CollectedVatZeile, "Vereinnahmte Umsatzsteuer"),
            [LineGroup.GoodsExpense] =
                (GoodsExpenseZeile, "Waren, Rohstoffe und Hilfsstoffe einschließlich Nebenkosten"),
            [LineGroup.OtherExpense] =
                (OtherExpenseZeile, "Übrige unbeschränkt abziehbare Betriebsausgaben"),
            [LineGroup.PaidInputVat] = (PaidInputVatZeile, "Gezahlte Vorsteuerbeträge"),
            [LineGroup.PaidOutputVat] = (PaidOutputVatZeile, "An das Finanzamt gezahlte Umsatzsteuer"),
        };

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<LineGroup, (string Zeile, string Bezeichnung)>> Versions =
        new Dictionary<int, IReadOnlyDictionary<LineGroup, (string, string)>>
        {
            [2024] = Form2024And2025,
            [2025] = Form2024And2025,
        };

    /// <summary>Returns the newest form version effective for the requested fiscal year and chart.</summary>
    public static IReadOnlyList<EuerLineDefinition> ForFiscalYear(ChartVariant chartVariant, int jahr)
    {
        var form = FormForFiscalYear(jahr);
        return GroupsForChart(chartVariant)
            .Select(group => Definition(group, form))
            .ToList();
    }

    /// <summary>Returns every EÜR group fed by an account number in the fiscal year's form.</summary>
    public static IReadOnlyList<EuerLineDefinition> ForAccount(
        ChartVariant chartVariant,
        int jahr,
        string accountNumber)
    {
        ArgumentNullException.ThrowIfNull(accountNumber);
        return ForFiscalYear(chartVariant, jahr)
            .Where(definition => definition.AccountNumbers.Contains(accountNumber, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Resolves a frozen cash-recognition bucket to its revenue line.
    /// Kleinunternehmer receipts use the dedicated §19 gross-revenue group.
    /// </summary>
    public static EuerLineDefinition RevenueLineFor(
        ChartVariant chartVariant,
        int jahr,
        TaxCategory taxCategory,
        decimal taxRatePercent,
        bool isKleinunternehmer)
    {
        var form = FormForFiscalYear(jahr);
        var groups = GroupsForChart(chartVariant);
        var group = isKleinunternehmer
            ? groups.Single(candidate => candidate.Group == LineGroup.KleinunternehmerRevenue)
            : groups.SingleOrDefault(candidate =>
                candidate.AmountKind == EuerAmountKind.Revenue
                && candidate.RecognitionSelectors.Any(selector => selector.Matches(taxCategory, taxRatePercent)))
                ?? throw new InvalidOperationException(
                    $"No EÜR revenue mapping exists for tax category {taxCategory} at {taxRatePercent}%.");
        return Definition(group, form);
    }

    internal static EuerLineDefinition OtherExpenseLineFor(ChartVariant chartVariant, int jahr) =>
        Definition(
            GroupsForChart(chartVariant).Single(group => group.Group == LineGroup.OtherExpense),
            FormForFiscalYear(jahr));

    private static IReadOnlyList<AccountGroup> GroupsForChart(ChartVariant chartVariant) =>
        Mappings.TryGetValue(chartVariant, out var groups)
            ? groups
            : throw new ArgumentOutOfRangeException(
                nameof(chartVariant),
                chartVariant,
                "Only SKR03 and SKR04 are supported.");

    private static IReadOnlyDictionary<LineGroup, (string Zeile, string Bezeichnung)> FormForFiscalYear(int jahr)
    {
        // Same effective-version policy as UstVaKennzifferMap.ForFiscalYear.
        var effectiveYear = Versions.Keys
            .Where(versionYear => versionYear <= jahr)
            .DefaultIfEmpty()
            .Max();
        if (effectiveYear == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jahr),
                jahr,
                "EÜR line definitions are available starting with fiscal year 2024.");
        }

        return Versions[effectiveYear];
    }

    private static EuerLineDefinition Definition(
        AccountGroup group,
        IReadOnlyDictionary<LineGroup, (string Zeile, string Bezeichnung)> form) =>
        new(
            form[group.Group].Zeile,
            form[group.Group].Bezeichnung,
            group.AmountKind is EuerAmountKind.Revenue or EuerAmountKind.CollectedVat
                ? EuerSection.Betriebseinnahmen
                : EuerSection.Betriebsausgaben,
            group.AmountKind,
            group.AccountNumbers,
            group.RecognitionSelectors);
}
