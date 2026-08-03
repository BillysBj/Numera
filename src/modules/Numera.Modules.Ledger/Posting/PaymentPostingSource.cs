namespace Numera.Modules.Ledger;

/// <summary>One payment allocation against a receivable account.</summary>
public sealed record PaymentPostingAllocation(string? ForderungAccount, decimal Amount);

/// <summary>Immutable payment facts required to build its Buchungssatz.</summary>
public sealed record PaymentPostingInput(
    string? BankAccount,
    DateOnly EntryDate,
    IReadOnlyList<PaymentPostingAllocation> Allocations,
    bool IsReversal);

/// <summary>Builds Bank / Forderungen posting legs for a payment or reversal.</summary>
public sealed class PaymentPostingSource(
    Guid tenantId,
    ChartVariant chartVariant,
    PaymentPostingInput input,
    AccountResolver accounts) : IPostingSource
{
    /// <inheritdoc />
    public IReadOnlyList<Posting> BuildPostings()
    {
        if (input.Allocations.Count == 0)
        {
            throw new ArgumentException("A payment posting requires at least one allocation.", nameof(input));
        }

        var allocations = input.Allocations
            .Select(allocation => (Allocation: allocation, Amount: decimal.Abs(allocation.Amount)))
            .ToList();
        if (allocations.Any(allocation => allocation.Amount == 0m))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Payment allocations must be non-zero.");
        }

        var bank = accounts.ResolveStandard(
            chartVariant,
            Seed.StandardAccountKind.Bank,
            input.BankAccount);
        var bankDirection = input.IsReversal ? PostingDirection.Credit : PostingDirection.Debit;
        var receivableDirection = input.IsReversal ? PostingDirection.Debit : PostingDirection.Credit;
        var postings = new List<Posting>(1 + allocations.Count)
        {
            CreatePosting(bank.AccountId, allocations.Sum(allocation => allocation.Amount), bankDirection),
        };

        foreach (var allocation in allocations)
        {
            var receivable = accounts.ResolveStandard(
                chartVariant,
                Seed.StandardAccountKind.Debtor,
                allocation.Allocation.ForderungAccount);
            postings.Add(CreatePosting(receivable.AccountId, allocation.Amount, receivableDirection));
        }

        return postings;
    }

    private Posting CreatePosting(Guid accountId, decimal amount, PostingDirection direction) =>
        new()
        {
            TenantId = tenantId,
            AccountId = accountId,
            Amount = amount,
            Direction = direction,
        };
}
