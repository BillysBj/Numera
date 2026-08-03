using Numera.Platform.Money;

namespace Numera.Modules.Ledger;

/// <summary>A frozen per-category invoice VAT breakdown used for posting.</summary>
public sealed record InvoicePostingBreakdown(
    TaxCategory TaxCategory,
    decimal RatePercent,
    decimal TaxableBase,
    decimal TaxAmount);

/// <summary>Immutable invoice facts required to build its Buchungssatz.</summary>
public sealed record InvoicePostingInput(
    Guid TenantId,
    Guid DocumentId,
    string DocumentNumber,
    DateOnly EntryDate,
    ChartVariant ChartVariant,
    string? DebtorAccountOverride,
    IReadOnlyList<InvoicePostingBreakdown> Breakdowns,
    decimal TotalGross,
    bool IsKleinunternehmer,
    bool ReverseCharge);

/// <summary>Builds Debitor / revenue / output-VAT posting legs from frozen invoice facts.</summary>
public sealed class InvoicePostingSource(
    InvoicePostingInput input,
    AccountResolver accounts,
    bool reverseDirections = false) : IPostingSource
{
    /// <inheritdoc />
    public IReadOnlyList<Posting> BuildPostings()
    {
        EnsureNonNegative(input.TotalGross, nameof(input.TotalGross));
        if (input.Breakdowns.Count == 0)
        {
            throw new ArgumentException("An invoice posting requires at least one frozen tax breakdown.", nameof(input));
        }

        var debtor = accounts.ResolveStandard(
            input.ChartVariant,
            Seed.StandardAccountKind.Debtor,
            input.DebtorAccountOverride);
        var postings = new List<Posting>(1 + (input.Breakdowns.Count * 2))
        {
            CreatePosting(debtor.AccountId, input.TotalGross, Reverse(PostingDirection.Debit)),
        };

        foreach (var breakdown in input.Breakdowns)
        {
            EnsureNonNegative(breakdown.TaxableBase, nameof(breakdown.TaxableBase));
            EnsureNonNegative(breakdown.TaxAmount, nameof(breakdown.TaxAmount));

            var effectiveCategory = input.ReverseCharge ? TaxCategory.AE : breakdown.TaxCategory;
            var revenue = accounts.ResolveRevenue(
                input.ChartVariant,
                effectiveCategory,
                breakdown.RatePercent,
                input.IsKleinunternehmer);
            postings.Add(CreatePosting(
                revenue.AccountId,
                breakdown.TaxableBase,
                Reverse(PostingDirection.Credit),
                revenue.Key,
                breakdown.RatePercent,
                effectiveCategory));

            if (breakdown.TaxAmount == 0m || input.IsKleinunternehmer || input.ReverseCharge)
            {
                continue;
            }

            var outputTax = accounts.ResolveOutputTax(
                input.ChartVariant,
                effectiveCategory,
                breakdown.RatePercent,
                input.IsKleinunternehmer);
            if (outputTax is { } tax)
            {
                postings.Add(CreatePosting(
                    tax.AccountId,
                    breakdown.TaxAmount,
                    Reverse(PostingDirection.Credit),
                    tax.Key,
                    breakdown.RatePercent,
                    effectiveCategory));
            }
        }

        return postings;
    }

    private PostingDirection Reverse(PostingDirection direction) =>
        reverseDirections
            ? direction == PostingDirection.Debit ? PostingDirection.Credit : PostingDirection.Debit
            : direction;

    private Posting CreatePosting(
        Guid accountId,
        decimal amount,
        PostingDirection direction,
        Steuerschluessel? key = null,
        decimal? taxRatePercent = null,
        TaxCategory? taxCategory = null) =>
        new()
        {
            TenantId = input.TenantId,
            AccountId = accountId,
            Amount = amount,
            Direction = direction,
            Steuerschluessel = key,
            TaxRatePercent = taxRatePercent,
            TaxCategory = taxCategory,
        };

    private static void EnsureNonNegative(decimal amount, string parameterName)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Posting amounts must be non-negative.");
        }
    }
}
