namespace Numera.Modules.Ledger;

/// <summary>Immutable supplier-expense facts required to build its Buchungssatz.</summary>
public sealed record ExpensePostingInput(
    string? ExpenseAccount,
    string? CreditorAccount,
    decimal RatePercent,
    decimal Net,
    decimal Tax,
    DateOnly EntryDate);

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
        EnsureNonNegative(input.Net, nameof(input.Net));
        EnsureNonNegative(input.Tax, nameof(input.Tax));

        var expense = accounts.ResolveExpense(chartVariant, input.RatePercent, input.ExpenseAccount);
        var creditor = accounts.ResolveStandard(
            chartVariant,
            Seed.StandardAccountKind.Creditor,
            input.CreditorAccount);
        var postings = new List<Posting>(3)
        {
            CreatePosting(
                expense.AccountId,
                input.Net,
                PostingDirection.Debit,
                expense.Key,
                input.RatePercent),
        };

        if (input.Tax > 0m)
        {
            var inputTax = accounts.ResolveInputTax(chartVariant, input.RatePercent);
            postings.Add(CreatePosting(
                inputTax.AccountId,
                input.Tax,
                PostingDirection.Debit,
                inputTax.Key,
                input.RatePercent));
        }

        postings.Add(CreatePosting(
            creditor.AccountId,
            input.Net + input.Tax,
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
