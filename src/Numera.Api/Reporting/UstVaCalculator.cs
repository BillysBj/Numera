using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>Computes the producible USt-VA Kennziffern from frozen recognition facts.</summary>
public sealed class UstVaCalculator(NumeraDbContext db, RecognitionReader recognitionReader)
{
    /// <summary>Computes one monthly or quarterly USt-VA review report.</summary>
    public async Task<UstVaReport> ComputeAsync(
        int jahr,
        string zeitraum,
        CancellationToken ct = default)
    {
        var (from, to) = ResolvePeriod(jahr, zeitraum);
        var definitions = UstVaKennzifferMap.ForFiscalYear(jahr);

        var besteuerungsartOrNull = await db.Set<LedgerSettings>()
            .AsNoTracking()
            .Select(settings => (Besteuerungsart?)settings.Besteuerungsart)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (besteuerungsartOrNull is null)
        {
            // The chart of accounts / ledger has not been set up yet (no LedgerSettings
            // row). Return a clean "setup required" report instead of throwing on
            // SingleAsync (which previously surfaced as an HTTP 500).
            return new UstVaReport(
                jahr,
                zeitraum,
                Besteuerungsart.Soll,
                IsFestgeschrieben: false,
                Lines: [],
                Zahllast: 0m,
                Hinweis: "Der Kontenrahmen ist noch nicht eingerichtet — die USt-Voranmeldung "
                    + "kann erst nach der einmaligen Ledger-Einrichtung berechnet werden.",
                IsKleinunternehmer: false);
        }

        var besteuerungsart = besteuerungsartOrNull.Value;
        var isKleinunternehmer = await db.Set<CompanyProfile>()
            .AsNoTracking()
            .Select(profile => profile.IsKleinunternehmer)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var isFestgeschrieben = await IsPeriodLockedAsync(from, to, ct).ConfigureAwait(false);

        if (isKleinunternehmer)
        {
            return new UstVaReport(
                jahr,
                zeitraum,
                besteuerungsart,
                isFestgeschrieben,
                [],
                Zahllast: 0m,
                Hinweis: null,
                IsKleinunternehmer: true);
        }

        var baseDefinitions = definitions
            .Where(definition => definition.FigureKind == UstVaFigureKind.Bemessungsgrundlage)
            .ToList();
        var taxDefinitions = definitions
            .Where(definition =>
                definition.FigureKind == UstVaFigureKind.Steuer
                && !definition.IsComputed)
            .ToList();
        var sollRows = await recognitionReader.ReadSollAsync(from, to, ct).ConfigureAwait(false);
        var untruncatedBases = besteuerungsart == Besteuerungsart.Soll
            ? ReadSollFigures(baseDefinitions, sollRows)
            : await ReadIstBasesAsync(baseDefinitions, from, to, ct).ConfigureAwait(false);
        var taxFigures = ReadSollFigures(taxDefinitions, sollRows)
            .ToDictionary(
                pair => pair.Key,
                pair => RoundingPolicy.RoundAmount(pair.Value),
                StringComparer.Ordinal);
        var accountNumbers = await ReadAccountNumbersAsync(
            definitions.Where(definition => !definition.IsComputed).ToList(), ct).ConfigureAwait(false);

        var zahllast = RoundingPolicy.RoundAmount(
            untruncatedBases["81"] * 0.19m
            + untruncatedBases["86"] * 0.07m
            - taxFigures["66"]);

        // Every definition below is producible, so a genuine zero is meaningful. Kz
        // without a posting/recognition source are absent from the versioned map entirely.
        var lines = definitions.Select(definition => definition switch
        {
            { IsComputed: true } => new UstVaLine(
                definition.Kz,
                definition.Bezeichnung,
                Bemessungsgrundlage: null,
                Steuer: zahllast,
                IsComputed: true)
            {
                ContributingAccountNumbers = accountNumbers
                    .Where(pair => pair.Key is "81" or "86" or "66")
                    .SelectMany(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList(),
            },
            { FigureKind: UstVaFigureKind.Steuer } => new UstVaLine(
                definition.Kz,
                definition.Bezeichnung,
                Bemessungsgrundlage: null,
                Steuer: taxFigures[definition.Kz],
                IsComputed: false)
            {
                ContributingAccountNumbers = accountNumbers[definition.Kz],
            },
            _ => new UstVaLine(
                definition.Kz,
                definition.Bezeichnung,
                Bemessungsgrundlage: decimal.Floor(untruncatedBases[definition.Kz]),
                Steuer: null,
                IsComputed: false)
            {
                ContributingAccountNumbers = accountNumbers[definition.Kz],
            },
        })
            .ToList();

        return new UstVaReport(
            jahr,
            zeitraum,
            besteuerungsart,
            isFestgeschrieben,
            lines,
            zahllast,
            Hinweis: null,
            IsKleinunternehmer: false);
    }

    private static Dictionary<string, decimal> ReadSollFigures(
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        IReadOnlyList<SollRecognitionRow> rows)
    {
        return definitions.ToDictionary(
            definition => definition.Kz,
            definition =>
            {
                var selector = definition.RecognitionSelector!;
                return rows
                    .Where(row =>
                        row.Kennziffer == definition.Kz
                        && (selector.AccountType is null || row.AccountType == selector.AccountType)
                        && (!selector.MatchTaxMetadata
                            || (row.TaxCategory == selector.TaxCategory
                                && row.TaxRatePercent == selector.TaxRatePercent)))
                    .Sum(row => row.Direction == selector.NaturalSide ? row.Amount : -row.Amount);
            },
            StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, decimal>> ReadIstBasesAsync(
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var rows = await recognitionReader.ReadCashRecognitionAsync(from, to, ct).ConfigureAwait(false);

        // Soll reversals are opposite-side postings and are signed above. Ist reversal
        // allocations already carry a negative pro-rata NetAmount, so they sum directly.
        return definitions.ToDictionary(
            definition => definition.Kz,
            definition =>
            {
                var selector = definition.RecognitionSelector!;
                return rows
                    .Where(row =>
                        row.TaxCategory == selector.TaxCategory
                        && row.VatRatePercent == selector.TaxRatePercent)
                    .Sum(row => row.NetAmount);
            },
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ReadAccountNumbersAsync(
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        CancellationToken ct)
    {
        var kennziffern = definitions.Select(definition => definition.Kz).ToList();
        var rows = await db.Set<Account>()
            .AsNoTracking()
            .Where(account => account.UstvaKennziffer != null && kennziffern.Contains(account.UstvaKennziffer))
            .Select(account => new { account.UstvaKennziffer, account.Number, account.Type })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return definitions.ToDictionary(
            definition => definition.Kz,
            definition => (IReadOnlyList<string>)rows
                .Where(row =>
                    row.UstvaKennziffer == definition.Kz
                    && (definition.RecognitionSelector!.AccountType is null
                        || row.Type == definition.RecognitionSelector.AccountType))
                .Select(row => row.Number)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList(),
            StringComparer.Ordinal);
    }

    private async Task<bool> IsPeriodLockedAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var months = Enumerable.Range(0, MonthCount(from, to))
            .Select(offset => from.AddMonths(offset))
            .Select(date => date.Month)
            .ToList();
        var lockedMonths = await db.Set<FiscalPeriod>()
            .AsNoTracking()
            .Where(period =>
                period.Year == from.Year
                && months.Contains(period.Month)
                && period.Status == FiscalPeriodStatus.Locked)
            .Select(period => period.Month)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);
        return lockedMonths == months.Count;
    }

    private static int MonthCount(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;

    private static (DateOnly From, DateOnly To) ResolvePeriod(int jahr, string zeitraum)
    {
        ArgumentNullException.ThrowIfNull(zeitraum);
        var (firstMonth, monthCount) = zeitraum switch
        {
            "01" => (1, 1),
            "02" => (2, 1),
            "03" => (3, 1),
            "04" => (4, 1),
            "05" => (5, 1),
            "06" => (6, 1),
            "07" => (7, 1),
            "08" => (8, 1),
            "09" => (9, 1),
            "10" => (10, 1),
            "11" => (11, 1),
            "12" => (12, 1),
            "41" => (1, 3),
            "42" => (4, 3),
            "43" => (7, 3),
            "44" => (10, 3),
            _ => throw new ArgumentOutOfRangeException(
                nameof(zeitraum),
                zeitraum,
                "ELSTER period code must be a month (01-12) or quarter (41-44)."),
        };

        var from = new DateOnly(jahr, firstMonth, 1);
        return (from, from.AddMonths(monthCount).AddDays(-1));
    }
}
