using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>Computes an EÜR from payment-date cash-recognition facts.</summary>
public sealed class EuerCalculator(NumeraDbContext db, RecognitionReader recognitionReader)
{
    /// <summary>
    /// The mandatory caveat while supplier receipts and outgoing supplier payments have
    /// no runtime entry path.
    /// </summary>
    public const string ExpenseIncompleteHinweis =
        "Betriebsausgaben unvollständig — Belegerfassung ab Phase 12.";

    /// <summary>Computes a cash-basis EÜR for an inclusive date range.</summary>
    public async Task<EuerReport> ComputeAsync(
        int jahr,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "The EÜR start date must be on or before the end date.");
        }

        var chartVariantOrNull = await db.Set<LedgerSettings>()
            .AsNoTracking()
            .Select(settings => (ChartVariant?)settings.ChartVariant)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (chartVariantOrNull is null)
        {
            // The chart of accounts / ledger has not been set up yet (no LedgerSettings
            // row). Return a clean "setup required" report instead of throwing on
            // SingleAsync (which previously surfaced as an HTTP 500).
            return new EuerReport(
                jahr,
                from,
                to,
                IsKleinunternehmer: false,
                Betriebseinnahmen: [],
                SummeEinnahmen: 0m,
                Betriebsausgaben: [],
                SummeAusgaben: 0m,
                Gewinn: 0m,
                IsExpenseDataIncomplete: true,
                Hinweis: "Der Kontenrahmen ist noch nicht eingerichtet — die EÜR kann erst "
                    + "nach der einmaligen Ledger-Einrichtung berechnet werden.");
        }

        var chartVariant = chartVariantOrNull.Value;
        var isKleinunternehmer = await db.Set<CompanyProfile>()
            .AsNoTracking()
            .Select(profile => profile.IsKleinunternehmer)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var recognitionRows = await recognitionReader
            .ReadCashRecognitionAsync(from, to, ct)
            .ConfigureAwait(false);

        var definitions = EuerLineMap.ForChart(chartVariant);
        var unroundedRevenueByLine = definitions
            .Where(definition =>
                definition.Section == EuerSection.Betriebseinnahmen
                && definition.AmountKind == EuerAmountKind.Revenue)
            .ToDictionary(definition => definition.Zeile, _ => 0m, StringComparer.Ordinal);

        foreach (var row in recognitionRows)
        {
            var definition = EuerLineMap.RevenueLineFor(
                chartVariant,
                row.TaxCategory,
                row.VatRatePercent,
                isKleinunternehmer);

            // §19 receipts are presented gross and have no separate Zeile-17 split.
            // Regelunternehmer revenue remains net; collected VAT is aggregated below.
            unroundedRevenueByLine[definition.Zeile] +=
                row.NetAmount + (isKleinunternehmer ? row.VatAmount : 0m);
        }

        var incomeDefinitions = definitions
            .Where(definition =>
                definition.Section == EuerSection.Betriebseinnahmen
                && (!isKleinunternehmer || definition.AmountKind != EuerAmountKind.CollectedVat))
            .ToList();
        var unroundedCollectedVat = recognitionRows.Sum(row => row.VatAmount);
        var incomeLines = incomeDefinitions
            .Select(definition => new EuerLine(
                definition.Zeile,
                definition.Bezeichnung,
                RoundingPolicy.RoundAmount(definition.AmountKind == EuerAmountKind.CollectedVat
                    ? unroundedCollectedVat
                    : unroundedRevenueByLine[definition.Zeile])))
            .ToList();

        // Phase 12 will supply supplier-payment recognition. Until then, these mapped
        // lines are honest zeros from the absence of a runtime expense data path; no
        // posting-derived or estimated amounts are fabricated here.
        var expenseLines = definitions
            .Where(definition =>
                definition.Section == EuerSection.Betriebsausgaben
                && (!isKleinunternehmer || definition.AmountKind != EuerAmountKind.PaidInputVat))
            .Select(definition => new EuerLine(
                definition.Zeile,
                definition.Bezeichnung,
                Betrag: 0m))
            .ToList();

        var summeEinnahmen = RoundingPolicy.RoundAmount(incomeLines.Sum(line => line.Betrag));
        var summeAusgaben = RoundingPolicy.RoundAmount(expenseLines.Sum(line => line.Betrag));
        var gewinn = RoundingPolicy.RoundAmount(summeEinnahmen - summeAusgaben);

        return new EuerReport(
            jahr,
            from,
            to,
            isKleinunternehmer,
            incomeLines,
            summeEinnahmen,
            expenseLines,
            summeAusgaben,
            gewinn,
            IsExpenseDataIncomplete: true,
            Hinweis: ExpenseIncompleteHinweis);
    }
}
