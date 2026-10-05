namespace Numera.Modules.Ledger;

/// <summary>Soll Abschreibungsaufwand an Haben Anlagekonto, without VAT.</summary>
public sealed class AfaPostingSource(Guid tenantId, Guid expenseAccountId, Guid assetAccountId, decimal amount)
    : IPostingSource
{
    public IReadOnlyList<Posting> BuildPostings()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        return
        [
            new() { TenantId = tenantId, AccountId = expenseAccountId, Amount = amount, Direction = PostingDirection.Debit },
            new() { TenantId = tenantId, AccountId = assetAccountId, Amount = amount, Direction = PostingDirection.Credit },
        ];
    }
}
