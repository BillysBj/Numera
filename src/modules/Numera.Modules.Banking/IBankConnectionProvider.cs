namespace Numera.Modules.Banking;

/// <summary>Provider-neutral port for bank connection, account, transaction, and consent access.</summary>
public interface IBankConnectionProvider
{
    Task<WebFormSession> StartImportAsync(BankConnection connection, CancellationToken ct);

    Task<WebFormSession> StartReauthAsync(BankConnection connection, CancellationToken ct);

    Task<IReadOnlyList<BankAccountDraft>> ListAccountsAsync(
        BankConnection connection,
        CancellationToken ct);

    IAsyncEnumerable<BankTransactionDraft> SyncTransactionsAsync(
        BankConnection connection,
        BankAccount account,
        string? cursor,
        CancellationToken ct);

    Task<ConsentSnapshot> GetConsentStatusAsync(
        BankConnection connection,
        CancellationToken ct);
}

/// <summary>finAPI Web Form session returned for import or re-authentication.</summary>
public sealed record WebFormSession(string WebFormId, string RedirectUrl);

/// <summary>Provider account data normalized before persistence.</summary>
public sealed record BankAccountDraft(
    string FinApiAccountId,
    string Iban,
    string DisplayName,
    string Currency);

/// <summary>Current provider consent state.</summary>
public sealed record ConsentSnapshot(ConsentStatus Status, DateTimeOffset? ExpiresAt);
