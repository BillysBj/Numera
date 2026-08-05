namespace Numera.Modules.Banking.Reconciliation;

/// <summary>A ranked, non-booking proposal for allocating an incoming transaction.</summary>
public sealed record MatchCandidate(
    Guid OpenItemId,
    string DocumentNumber,
    decimal OpenAmount,
    decimal SuggestedAllocation,
    decimal Score,
    MatchTier Tier,
    IReadOnlyList<string> Reasons);

/// <summary>Queue-ranking tier; neither value authorizes automatic booking.</summary>
public enum MatchTier
{
    High,
    Review,
}
