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

    /// <summary>
    /// Best-effort, irreversible deletion of all provider-side data for <paramref name="connection"/>
    /// (Phase B1 Scheibe 2). For finAPI this deletes the sub-user, which cascades to every bank
    /// connection, account, transaction and consent that sub-user owns. Returns <c>true</c> when a
    /// provider-side user was deleted, <c>false</c> when there was nothing to delete (e.g. no
    /// sub-user was ever provisioned, or it is already gone).
    /// </summary>
    Task<bool> DeleteProviderDataAsync(BankConnection connection, CancellationToken ct);
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
