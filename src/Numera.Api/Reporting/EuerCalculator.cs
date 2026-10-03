using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>Computes an EÜR from payment-date cash-recognition facts.</summary>
public sealed class EuerCalculator(NumeraDbContext db, RecognitionReader recognitionReader)
{
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

        var definitions = EuerLineMap.ForFiscalYear(chartVariant, jahr);
        var unroundedRevenueByLine = definitions
            .Where(definition =>
                definition.Section == EuerSection.Betriebseinnahmen
                && definition.AmountKind == EuerAmountKind.Revenue)
            .ToDictionary(definition => definition.Zeile, _ => 0m, StringComparer.Ordinal);

        foreach (var row in recognitionRows)
        {
            var definition = EuerLineMap.RevenueLineFor(
                chartVariant,
                jahr,
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

        var expenseRows = await recognitionReader
            .ReadExpenseCashRecognitionAsync(from, to, ct)
            .ConfigureAwait(false);
        var unroundedExpenseByLine = definitions
            .Where(definition => definition.Section == EuerSection.Betriebsausgaben)
            .ToDictionary(definition => definition.Zeile, _ => 0m, StringComparer.Ordinal);
        // Accounts outside the explicit SKR→Zeile map (e.g. a manual expense-account
        // override on a receipt) are routed to the catch-all "Übrige Betriebsausgaben"
        // line so the report never 500s and the profit/total stay correct — the booked
        // default accounts (4980/6300) already resolve to that same line. Line-level
        // granularity for other accounts is a Steuerberater refinement.
        var otherExpenseDefinition = EuerLineMap.OtherExpenseLineFor(chartVariant, jahr);
        var paidInputVatDefinition = definitions.Single(definition =>
            definition.AmountKind == EuerAmountKind.PaidInputVat);
        foreach (var row in expenseRows)
        {
            var definition = EuerLineMap.ForAccount(chartVariant, jahr, row.AccountNumber)
                .SingleOrDefault(candidate => candidate.AmountKind == EuerAmountKind.Expense)
                ?? otherExpenseDefinition;
            unroundedExpenseByLine[definition.Zeile] +=
                row.NetAmount + (isKleinunternehmer ? row.VatAmount : 0m);
            if (!isKleinunternehmer)
            {
                unroundedExpenseByLine[paidInputVatDefinition.Zeile] += row.VatAmount;
            }
        }

        if (!isKleinunternehmer)
        {
            unroundedExpenseByLine[EuerLineMap.PaidOutputVatZeile] = await recognitionReader
                .ReadVatFinanzamtRecognitionAsync(from, to, ct).ConfigureAwait(false);
        }

        var expenseLines = definitions
            .Where(definition =>
                definition.Section == EuerSection.Betriebsausgaben
                && (!isKleinunternehmer || definition.AmountKind is not
                    (EuerAmountKind.PaidInputVat or EuerAmountKind.PaidOutputVat)))
            .Select(definition => new EuerLine(
                definition.Zeile,
                definition.Bezeichnung,
                RoundingPolicy.RoundAmount(unroundedExpenseByLine[definition.Zeile])))
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
            IsExpenseDataIncomplete: false,
            Hinweis: null);
    }
}
