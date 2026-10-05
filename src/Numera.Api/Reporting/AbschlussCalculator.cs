using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>Read-only fiscal-year GuV and closing-date Bilanz from the tenant's ledger.</summary>
public sealed class AbschlussCalculator(NumeraDbContext db)
{
    /// <summary>Computes the GuV for the fiscal year beginning in <paramref name="jahr"/>.</summary>
    public async Task<GuvReport> ComputeGuvAsync(int jahr, CancellationToken ct)
    {
        var settings = await ReadSettingsAsync(ct).ConfigureAwait(false);
        var (from, to) = FiscalYear(jahr, settings);
        var hinweis = Gate(settings);
        if (hinweis is not null)
        {
            return new GuvReport(jahr, from, to, [], [], 0m, hinweis);
        }

        var balances = await ReadBalancesAsync(from, to, includeOpeningBalances: false, ct).ConfigureAwait(false);
        return BuildGuv(jahr, from, to, settings!.ChartVariant, balances);
    }

    /// <summary>Computes balances through fiscal year end, with the GuV result in equity.</summary>
    public async Task<BilanzReport> ComputeBilanzAsync(int jahr, CancellationToken ct)
    {
        var settings = await ReadSettingsAsync(ct).ConfigureAwait(false);
        var (from, to) = FiscalYear(jahr, settings);
        var hinweis = Gate(settings);
        if (hinweis is not null)
        {
            return new BilanzReport(jahr, to, [], 0m, [], 0m, 0m, hinweis);
        }

        // One query keeps the stock balances and current-year result on the same snapshot.
        // Earlier balance-sheet postings remain in the closing-date stock; earlier GuV
        // postings do not become this year's profit. Missing year-end transfers remain
        // visible as BilanzDifferenz instead of inventing postings or retained earnings.
        var balances = await ReadBalancesAsync(from, to, includeOpeningBalances: true, ct).ConfigureAwait(false);
        var guv = BuildGuv(jahr, from, to, settings!.ChartVariant, balances);
        var definitions = BilanzPositionMap.ForFiscalYear(settings.ChartVariant, jahr);
        var grouped = balances
            .Where(balance => balance.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity)
            .GroupBy(balance => new
            {
                IsAsset = balance.Type == AccountType.Asset,
                Gruppe = definitions.FirstOrDefault(definition => definition.Matches(balance.Type, balance.Number))?.Gruppe
                    ?? balance.Type switch
                    {
                        AccountType.Asset => BilanzPositionMap.CurrentAssets,
                        AccountType.Equity => BilanzPositionMap.Equity,
                        _ => BilanzPositionMap.Liabilities,
                    },
            })
            .Select(group => new
            {
                group.Key.IsAsset,
                Position = new BilanzPosition(group.Key.Gruppe, group.Key.Gruppe,
                    RoundingPolicy.RoundAmount(group.Sum(NaturalBalance))),
            })
            .ToList();
        var groupOrder = definitions.Select(definition => definition.Gruppe).Distinct().ToList();
        var aktiva = grouped.Where(group => group.IsAsset).Select(group => group.Position)
            .OrderBy(position => groupOrder.IndexOf(position.Gruppe)).ToList();
        var passiva = grouped.Where(group => !group.IsAsset).Select(group => group.Position).ToList();
        passiva.Add(new BilanzPosition(BilanzPositionMap.Equity, "Jahresüberschuss", guv.Jahresueberschuss));
        passiva = passiva.OrderBy(position => groupOrder.IndexOf(position.Gruppe)).ToList();
        var summeAktiva = RoundingPolicy.RoundAmount(aktiva.Sum(position => position.Betrag));
        var summePassiva = RoundingPolicy.RoundAmount(passiva.Sum(position => position.Betrag));

        return new BilanzReport(jahr, to, aktiva, summeAktiva, passiva, summePassiva,
            RoundingPolicy.RoundAmount(summeAktiva - summePassiva), Hinweis: null);
    }

    private Task<LedgerSettings?> ReadSettingsAsync(CancellationToken ct) =>
        db.Set<LedgerSettings>().AsNoTracking().SingleOrDefaultAsync(ct);

    private static string? Gate(LedgerSettings? settings)
    {
        if (settings is null || settings.ChartVariant is not (ChartVariant.Skr03 or ChartVariant.Skr04)
            || settings.FiscalYearStartMonth is < 1 or > 12)
        {
            return "Der Kontenrahmen ist noch nicht eingerichtet — Bilanz/GuV können erst "
                + "nach der einmaligen Ledger-Einrichtung berechnet werden.";
        }

        return settings.Gewinnermittlungsart != Gewinnermittlungsart.Bilanz
            ? "Bilanz/GuV stehen nur bei bilanzierender Gewinnermittlung zur Verfügung."
            : null;
    }

    private static (DateOnly From, DateOnly To) FiscalYear(int jahr, LedgerSettings? settings)
    {
        var month = settings?.FiscalYearStartMonth is >= 1 and <= 12 ? settings.FiscalYearStartMonth : 1;
        var from = new DateOnly(jahr, month, 1);
        return (from, from.AddMonths(12).AddDays(-1));
    }

    private async Task<List<AccountBalance>> ReadBalancesAsync(
        DateOnly fromDate, DateOnly toDate, bool includeOpeningBalances, CancellationToken ct) =>
        await (from posting in db.Set<Posting>().AsNoTracking()
               join account in db.Set<Account>().AsNoTracking() on posting.AccountId equals account.Id
               join entry in db.Set<JournalEntry>().AsNoTracking() on posting.JournalEntryId equals entry.Id
               where entry.EntryDate <= toDate
                   && (entry.EntryDate >= fromDate || (includeOpeningBalances
                       && (account.Type == AccountType.Asset || account.Type == AccountType.Liability
                           || account.Type == AccountType.Equity)))
               group posting by new { account.Id, account.Number, account.Type } into postings
               select new AccountBalance(postings.Key.Number, postings.Key.Type,
                   postings.Sum(posting => posting.Direction == PostingDirection.Debit ? posting.Amount : -posting.Amount)))
            .ToListAsync(ct).ConfigureAwait(false);

    private static GuvReport BuildGuv(
        int jahr, DateOnly from, DateOnly to, ChartVariant chart, IReadOnlyList<AccountBalance> balances)
    {
        var definitions = GuvPositionMap.ForFiscalYear(chart, jahr);
        var ertraege = Lines(AccountType.Revenue, GuvPositionMap.OtherRevenue);
        var aufwendungen = Lines(AccountType.Expense, GuvPositionMap.OtherExpense);
        var jahresueberschuss = RoundingPolicy.RoundAmount(
            ertraege.Sum(position => position.Betrag) - aufwendungen.Sum(position => position.Betrag));
        return new GuvReport(jahr, from, to, ertraege, aufwendungen, jahresueberschuss, Hinweis: null);

        List<GuvPosition> Lines(AccountType type, string fallback)
        {
            var order = definitions.Where(definition => definition.Type == type)
                .Select(definition => definition.Bezeichnung).Append(fallback).Distinct().ToList();
            return balances.Where(balance => balance.Type == type)
                .GroupBy(balance => definitions.FirstOrDefault(definition => definition.Matches(type, balance.Number))?.Bezeichnung
                    ?? fallback)
                .Select(group => new GuvPosition(group.Key, RoundingPolicy.RoundAmount(group.Sum(NaturalBalance))))
                .OrderBy(position => order.IndexOf(position.Bezeichnung))
                .ToList();
        }
    }

    private static decimal NaturalBalance(AccountBalance balance) =>
        balance.Type is AccountType.Asset or AccountType.Expense ? balance.Balance : -balance.Balance;

    private sealed record AccountBalance(string Number, AccountType Type, decimal Balance);
}
