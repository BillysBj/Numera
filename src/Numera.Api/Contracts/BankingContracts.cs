using Numera.Modules.Sales.Payments;

namespace Numera.Api.Contracts;

/// <summary>Wire records for bank connections, statement import, and reconciliation.</summary>
public static class BankingContracts
{
    /// <summary>A provider Web Form used to connect or re-authorize a bank.</summary>
    public sealed record BankWebFormResponse(string WebFormId, string RedirectUrl);

    /// <summary>One tenant-scoped bank account with its persisted consent health.</summary>
    public sealed record BankAccountListItem(
        Guid Id,
        string Iban,
        string DisplayName,
        string Currency,
        Guid? ConnectionId,
        int? ConsentStatus,
        DateTimeOffset? LastSyncedAt,
        DateTimeOffset? ConsentExpiresAt);

    /// <summary>The current provider consent snapshot. Enums cross the wire as numbers.</summary>
    public sealed record BankConsentResponse(
        Guid ConnectionId,
        int Status,
        DateTimeOffset? ExpiresAt);

    /// <summary>The result of importing a statement into the shared transaction pipeline.</summary>
    public sealed record BankStatementImportResponse(Guid BankAccountId, int InsertedCount);

    /// <summary>One transaction in the paged human review queue. Enums cross the wire as numbers.</summary>
    public sealed record BankTransactionListItem(
        Guid Id,
        Guid BankAccountId,
        int Source,
        decimal Amount,
        DateOnly ValueDate,
        DateOnly? BookingDate,
        string? Purpose,
        string? CounterpartyName,
        string? CounterpartyIban,
        string? EndToEndId,
        int MatchStatus,
        decimal? ConfidenceScore,
        Guid? MatchedPaymentId,
        DateTimeOffset CreatedAt);

    /// <summary>Paged envelope for the reconciliation queue.</summary>
    public sealed record BankTransactionListResponse(
        IReadOnlyList<BankTransactionListItem> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>A ranked receivable candidate that has not been persisted or booked.</summary>
    public sealed record MatchCandidateResponse(
        Guid OpenItemId,
        string DocumentNumber,
        decimal OpenAmount,
        decimal SuggestedAllocation,
        decimal Score,
        int Tier,
        IReadOnlyList<string> Reasons);

    /// <summary>The human-confirmed allocation payload for one incoming bank transaction.</summary>
    public sealed record ConfirmBankTransactionRequest(
        IReadOnlyList<PaymentAllocationInput> Allocations,
        PaymentMethod? Method = null);

    /// <summary>The idempotent result of a confirmed or reversed reconciliation.</summary>
    public sealed record BankTransactionMatchResponse(
        Guid TransactionId,
        int MatchStatus,
        Guid? PaymentId,
        bool AlreadyConfirmed = false);
}
