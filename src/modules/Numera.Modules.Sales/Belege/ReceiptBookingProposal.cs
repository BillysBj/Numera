using Numera.Modules.Ledger;

namespace Numera.Modules.Sales.Belege;

/// <summary>Maps human-confirmed receipt facts to one expense posting input.</summary>
public static class ReceiptBookingProposal
{
    /// <summary>
    /// Builds one posting input with one frozen row per VAT rate. An empty supplied breakdown means
    /// a single-rate receipt and is mapped from the receipt's confirmed amount fields.
    /// </summary>
    public static ExpensePostingInput Build(
        Receipt receipt,
        IReadOnlyList<(decimal RatePercent, decimal Net, decimal Tax)> breakdown,
        string? creditorAccount)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(breakdown);

        var rows = breakdown.Count == 0
            ? [new ExpensePostingBreakdown(
                receipt.VatRatePercent
                    ?? throw new InvalidOperationException("A single-rate receipt requires a confirmed VAT rate."),
                receipt.NetAmount
                    ?? throw new InvalidOperationException("A single-rate receipt requires a confirmed net amount."),
                receipt.VatAmount
                    ?? throw new InvalidOperationException("A single-rate receipt requires a confirmed VAT amount."))]
            : breakdown
                .Select(row => new ExpensePostingBreakdown(row.RatePercent, row.Net, row.Tax))
                .ToArray();

        var entryDate = receipt.ExpenseDate
            ?? receipt.InvoiceDate
            ?? throw new InvalidOperationException("A booking proposal requires an expense or invoice date.");

        return new ExpensePostingInput(
            receipt.ExpenseAccountOverride,
            creditorAccount,
            rows,
            entryDate);
    }
}
