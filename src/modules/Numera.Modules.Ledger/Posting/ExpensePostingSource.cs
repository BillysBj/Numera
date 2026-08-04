namespace Numera.Modules.Ledger;

/// <summary>A frozen per-rate expense VAT breakdown used for posting.</summary>
public sealed record ExpensePostingBreakdown(decimal RatePercent, decimal Net, decimal Tax);

/// <summary>Immutable supplier-expense facts required to build its Buchungssatz.</summary>
public sealed record ExpensePostingInput(
    string? ExpenseAccount,
    string? CreditorAccount,
    IReadOnlyList<ExpensePostingBreakdown> Breakdowns,
    DateOnly EntryDate)
{
    /// <summary>Creates a single-rate expense posting input.</summary>
    public ExpensePostingInput(
        string? ExpenseAccount,
        string? CreditorAccount,
        decimal RatePercent,
        decimal Net,
        decimal Tax,
        DateOnly EntryDate)
        : this(
            ExpenseAccount,
            CreditorAccount,
            [new ExpensePostingBreakdown(RatePercent, Net, Tax)],
            EntryDate)
    {
    }
}

/// <summary>Builds Aufwand / Vorsteuer / Verbindlichkeiten posting legs.</summary>
public sealed class ExpensePostingSource(
    Guid tenantId,
    ChartVariant chartVariant,
    ExpensePostingInput input,
    AccountResolver accounts) : IPostingSource
{
    /// <inheritdoc />
    public IReadOnlyList<Posting> BuildPostings()
    {
        if (input.Breakdowns.Count == 0)
        {
            throw new ArgumentException("An expense posting requires at least one frozen tax breakdown.", nameof(input));
        }

        var creditor = accounts.ResolveStandard(
            chartVariant,
            Seed.StandardAccountKind.Creditor,
            input.CreditorAccount);
        var postings = new List<Posting>(1 + (input.Breakdowns.Count * 2));

        foreach (var breakdown in input.Breakdowns)
        {
            EnsureNonNegative(breakdown.Net, nameof(breakdown.Net));
            EnsureNonNegative(breakdown.Tax, nameof(breakdown.Tax));

            var expense = accounts.ResolveExpense(
                chartVariant,
                breakdown.RatePercent,
                input.ExpenseAccount);
            postings.Add(CreatePosting(
                expense.AccountId,
                breakdown.Net,
                PostingDirection.Debit,
                expense.Key,
                breakdown.RatePercent));

            if (breakdown.Tax > 0m)
            {
                var inputTax = accounts.ResolveInputTax(chartVariant, breakdown.RatePercent);
                postings.Add(CreatePosting(
                    inputTax.AccountId,
                    breakdown.Tax,
                    PostingDirection.Debit,
                    inputTax.Key,
                    breakdown.RatePercent));
            }
        }

        postings.Add(CreatePosting(
            creditor.AccountId,
            input.Breakdowns.Sum(breakdown => breakdown.Net + breakdown.Tax),
            PostingDirection.Credit));
        return postings;
    }

    private Posting CreatePosting(
        Guid accountId,
        decimal amount,
        PostingDirection direction,
        Steuerschluessel? key = null,
        decimal? taxRatePercent = null) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Amount = amount,
            Direction = direction,
            Steuerschluessel = key,
            TaxRatePercent = taxRatePercent,
        };

    private static void EnsureNonNegative(decimal amount, string parameterName)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Posting amounts must be non-negative.");
        }
    }
}
