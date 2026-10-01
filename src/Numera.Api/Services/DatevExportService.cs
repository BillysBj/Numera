using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Services;

/// <summary>Reads journal entries and posting accounts under the request's tenant/RLS scope.</summary>
public sealed class DatevExportService(NumeraDbContext db)
{
    public async Task<byte[]> ExportAsync(LedgerSettings settings, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var entries = await db.Set<JournalEntry>().AsNoTracking()
            .Where(x => x.EntryDate >= from && x.EntryDate <= to)
            .Include(x => x.Postings)
            .OrderBy(x => x.EntryDate).ThenBy(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var accounts = await db.Set<Account>().AsNoTracking()
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var sourceIds = entries.Select(x => Guid.TryParse(x.SourceRef, out var id) ? id : Guid.Empty)
            .Where(x => x != Guid.Empty).Distinct().ToArray();
        var documents = await db.Set<SalesDocument>().AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DocumentNumber })
            .ToDictionaryAsync(x => x.Id, x => x.DocumentNumber, ct).ConfigureAwait(false);
        var receipts = await db.Set<Receipt>().AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.InvoiceNumber })
            .ToDictionaryAsync(x => x.Id, x => x.InvoiceNumber, ct).ConfigureAwait(false);
        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Reference })
            .ToDictionaryAsync(x => x.Id, x => x.Reference, ct).ConfigureAwait(false);

        var lines = new List<DatevPostingLine>();
        foreach (var entry in entries)
        {
            var sourceId = Guid.TryParse(entry.SourceRef, out var id) ? id : Guid.Empty;
            var documentNumber = entry.SourceType switch
            {
                LedgerSourceType.Invoice => documents.GetValueOrDefault(sourceId),
                LedgerSourceType.Expense => receipts.GetValueOrDefault(sourceId),
                LedgerSourceType.Payment => payments.GetValueOrDefault(sourceId),
                _ => null,
            };
            lines.AddRange(MapEntry(entry, accounts, documentNumber ?? entry.JournalNumber ?? entry.SourceRef));
        }

        return DatevExport.Render(settings, from, to, lines, DateTimeOffset.UtcNow);
    }

    /// <summary>Maps double-entry legs to DATEV bookings, folding explicit VAT into gross amounts.</summary>
    public static IReadOnlyList<DatevPostingLine> MapEntry(
        JournalEntry entry, IReadOnlyDictionary<Guid, Account> accounts, string documentNumber)
    {
        var postings = entry.Postings.OrderBy(x => x.Id).ToList();
        var taxes = postings.Where(x => IsVatAccount(accounts[x.AccountId])).ToList();
        var nonVat = postings.Except(taxes).ToList();
        var revenue = nonVat.Where(x => accounts[x.AccountId].Type == AccountType.Revenue).ToList();
        var expenses = nonVat.Where(x => accounts[x.AccountId].Type == AccountType.Expense).ToList();

        if (TryMapTaxedBooking(revenue, AccountType.Asset, sales: true, out var salesRows))
        {
            return salesRows;
        }

        if (TryMapTaxedBooking(expenses, AccountType.Liability, sales: false, out var expenseRows))
        {
            return expenseRows;
        }

        // Two-leg payments naturally produce one row, without a tax key. For other
        // shapes use the requested largest-credit fallback, excluding pure VAT accounts.
        // VAT that cannot be folded through a known BU key is omitted; arbitrary manual
        // tax adjustments cannot be reconstructed as automatic-VAT bookings here.
        var credit = nonVat.Where(x => x.Direction == PostingDirection.Credit)
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => accounts[x.AccountId].Number, StringComparer.Ordinal)
            .ThenBy(x => x.Id).FirstOrDefault();
        return credit is null ? [] : nonVat.Where(x => x.Direction == PostingDirection.Debit)
            .Select(debit => Row(debit, credit, debit.Amount, null)).ToList();

        bool TryMapTaxedBooking(
            List<Posting> netLegs, AccountType counterType, bool sales,
            out IReadOnlyList<DatevPostingLine> rows)
        {
            rows = [];
            if (netLegs.Count == 0)
            {
                return false;
            }

            var counters = nonVat.Except(netLegs).ToList();
            if (counters.Count != 1 || accounts[counters[0].AccountId].Type != counterType
                || netLegs.Any(x => x.Direction == counters[0].Direction))
            {
                return false;
            }

            var counter = counters[0];
            var result = new List<DatevPostingLine>();
            foreach (var group in netLegs.GroupBy(x => new
                     { x.Direction, x.TaxRatePercent, x.TaxCategory, x.Steuerschluessel }))
            {
                var netTotal = group.Sum(x => x.Amount);
                // Output-VAT legs have no BU key in InvoicePostingSource. Match the
                // frozen rate/category and account kind instead of matching tax keys.
                // Unmatched VAT or VAT without a supported BU key is skipped: there
                // is no reliable automatic-VAT reconstruction for those adjustments.
                var taxTotal = CanFoldVat(group.Key.Steuerschluessel, group.Key.TaxRatePercent, sales)
                    ? taxes.Where(x => x.Direction == group.Key.Direction
                        && x.TaxRatePercent == group.Key.TaxRatePercent
                        && x.TaxCategory == group.Key.TaxCategory
                        && accounts[x.AccountId].Type == (sales ? AccountType.Liability : AccountType.Asset))
                        .Sum(x => x.Amount)
                    : 0m;
                decimal netAllocated = 0m;
                decimal taxAllocated = 0m;
                var legs = group.ToList();
                for (var i = 0; i < legs.Count; i++)
                {
                    var net = legs[i];
                    netAllocated += net.Amount;
                    // Repeated legs at one rate share the recorded tax once. Cumulative
                    // allocation preserves cents and assigns the exact remainder last.
                    var cumulativeTax = i == legs.Count - 1 ? taxTotal
                        : netTotal == 0m ? 0m
                        : decimal.Round(taxTotal * netAllocated / netTotal, 2, MidpointRounding.AwayFromZero);
                    var gross = net.Amount + cumulativeTax - taxAllocated;
                    taxAllocated = cumulativeTax;
                    result.Add(sales
                        ? Row(counter, net, gross, net.Steuerschluessel)
                        : Row(net, counter, gross, net.Steuerschluessel));
                }
            }

            rows = result;
            return true;
        }

        DatevPostingLine Row(Posting account, Posting opposite, decimal amount, Steuerschluessel? key) =>
            new(amount, account.Direction,
                // Both supported charts use four-digit Sachkonten. Preserve longer
                // debtor/creditor account overrides, including their leading zeroes.
                accounts[account.AccountId].Number.PadLeft(4, '0'),
                accounts[opposite.AccountId].Number.PadLeft(4, '0'), key,
                entry.EntryDate, documentNumber, entry.Description);
    }

    private static bool CanFoldVat(Steuerschluessel? key, decimal? rate, bool sales) =>
        (sales, key, rate) is
            (true, Steuerschluessel.Ust19, 19m) or (true, Steuerschluessel.Ust7, 7m)
            or (false, Steuerschluessel.Vst19, 19m) or (false, Steuerschluessel.Vst7, 7m);

    private static bool IsVatAccount(Account account)
    {
        // AccountType alone is insufficient: input VAT and debtors are both assets,
        // output VAT and creditors both liabilities. Reuse the posting sources' SKR
        // mappings to distinguish the pure tax accounts without relying on their names.
        return account.Type switch
        {
            AccountType.Asset => account.Number == SkrMapping.ExpenseMapping(account.ChartVariant, 7m).VorsteuerAccount
                || account.Number == SkrMapping.ExpenseMapping(account.ChartVariant, 19m).VorsteuerAccount,
            AccountType.Liability => account.Number == SkrMapping.RevenueMapping(account.ChartVariant, TaxCategory.S, 7m).UstAccount
                || account.Number == SkrMapping.RevenueMapping(account.ChartVariant, TaxCategory.S, 19m).UstAccount,
            _ => false,
        };
    }
}
