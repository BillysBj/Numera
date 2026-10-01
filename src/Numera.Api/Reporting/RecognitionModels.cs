using Numera.Modules.Ledger;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

/// <summary>A grouped tax-bearing posting recognized on its journal entry date.</summary>
public record SollRecognitionRow(
    string? Kennziffer,
    TaxCategory? TaxCategory,
    decimal? TaxRatePercent,
    PostingDirection Direction,
    decimal Amount)
{
    /// <summary>The chart classification used to distinguish input-tax from expense accounts.</summary>
    public AccountType AccountType { get; init; }
}

/// <summary>A frozen invoice tax bucket recognized pro rata on a payment value date.</summary>
public record CashRecognitionRow(
    TaxCategory TaxCategory,
    decimal VatRatePercent,
    decimal NetAmount,
    decimal VatAmount,
    DateOnly RecognizedOn);

/// <summary>A receipt's pro-rata expense and VAT recognized when its supplier is paid.</summary>
public record ExpenseCashRecognitionRow(
    decimal? VatRatePercent,
    decimal NetAmount,
    decimal VatAmount,
    string AccountNumber,
    DateOnly RecognizedOn);
