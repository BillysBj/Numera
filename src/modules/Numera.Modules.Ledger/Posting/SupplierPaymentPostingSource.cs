namespace Numera.Modules.Ledger;

/// <summary>One payment allocation against a payable account.</summary>
public sealed record SupplierPaymentPostingAllocation(string? CreditorAccount, decimal Amount);

/// <summary>Immutable payment facts required to build its Buchungssatz.</summary>
public sealed record SupplierPaymentPostingInput(
    string? BankAccount,
    DateOnly EntryDate,
    IReadOnlyList<SupplierPaymentPostingAllocation> Allocations,
    bool IsReversal);

/// <summary>Builds Verbindlichkeiten / Bank posting legs for a payment or reversal.</summary>
public sealed class SupplierPaymentPostingSource(
    Guid tenantId,
    ChartVariant chartVariant,
    SupplierPaymentPostingInput input,
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
        var bankDirection = input.IsReversal ? PostingDirection.Debit : PostingDirection.Credit;
        var payableDirection = input.IsReversal ? PostingDirection.Credit : PostingDirection.Debit;
        var postings = new List<Posting>(1 + allocations.Count)
        {
            CreatePosting(bank.AccountId, allocations.Sum(allocation => allocation.Amount), bankDirection),
        };

        foreach (var allocation in allocations)
        {
            var payable = accounts.ResolveStandard(
                chartVariant,
                Seed.StandardAccountKind.Creditor,
                allocation.Allocation.CreditorAccount);
            postings.Add(CreatePosting(payable.AccountId, allocation.Amount, payableDirection));
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
