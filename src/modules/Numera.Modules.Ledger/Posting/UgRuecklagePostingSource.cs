namespace Numera.Modules.Ledger;

/// <summary>Soll Ergebnisverwendung / Gewinnvortrag an Haben gesetzliche Rücklage, without VAT.</summary>
public sealed class UgRuecklagePostingSource(Guid tenantId, Guid gewinnvortragAccountId, Guid ruecklageAccountId, decimal amount)
    : IPostingSource
{
    public IReadOnlyList<Posting> BuildPostings()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        return
        [
            new() { TenantId = tenantId, AccountId = gewinnvortragAccountId, Amount = amount, Direction = PostingDirection.Debit },
            new() { TenantId = tenantId, AccountId = ruecklageAccountId, Amount = amount, Direction = PostingDirection.Credit },
        ];
    }
}
