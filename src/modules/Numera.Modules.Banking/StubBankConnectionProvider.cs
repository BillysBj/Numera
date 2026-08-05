using System.Runtime.CompilerServices;

namespace Numera.Modules.Banking;

/// <summary>Zero-dependency default provider used when live finAPI is not configured.</summary>
public sealed class StubBankConnectionProvider : IBankConnectionProvider
{
    private const string NotConfiguredMessage = "Live-Bankanbindung ist nicht konfiguriert.";

    /// <inheritdoc />
    public Task<WebFormSession> StartImportAsync(BankConnection connection, CancellationToken ct) =>
        throw new NotSupportedException(NotConfiguredMessage);

    /// <inheritdoc />
    public Task<WebFormSession> StartReauthAsync(BankConnection connection, CancellationToken ct) =>
        throw new NotSupportedException(NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<BankAccountDraft>> ListAccountsAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BankAccountDraft>>([]);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BankTransactionDraft> SyncTransactionsAsync(
        BankConnection connection,
        BankAccount account,
        string? cursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc />
    public Task<ConsentSnapshot> GetConsentStatusAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new ConsentSnapshot(ConsentStatus.Active, null));
    }
}
