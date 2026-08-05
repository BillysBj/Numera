using System.Runtime.CompilerServices;

using Numera.Modules.Banking;

namespace Numera.IntegrationTests.Fixtures;

/// <summary>Scriptable in-process bank provider used so integration tests never contact finAPI.</summary>
public sealed class FakeBankConnectionProvider : IBankConnectionProvider
{
    /// <summary>Transactions returned by each synchronization call.</summary>
    public IReadOnlyList<BankTransactionDraft> Transactions { get; set; } = [];

    /// <summary>Accounts returned by account discovery.</summary>
    public IReadOnlyList<BankAccountDraft> Accounts { get; set; } = [];

    /// <summary>Consent returned by the consent-status endpoint.</summary>
    public ConsentSnapshot Consent { get; set; } =
        new(ConsentStatus.Active, DateTimeOffset.UtcNow.AddDays(89));

    /// <summary>Web-form session returned by connect and re-auth calls.</summary>
    public WebFormSession WebForm { get; set; } =
        new("fake-web-form", "https://finapi.invalid/fake-web-form");

    /// <summary>Cursors received by synchronization calls, in call order.</summary>
    public List<string?> ReceivedCursors { get; } = [];

    /// <inheritdoc />
    public Task<WebFormSession> StartImportAsync(BankConnection connection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(WebForm);
    }

    /// <inheritdoc />
    public Task<WebFormSession> StartReauthAsync(BankConnection connection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(WebForm);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BankAccountDraft>> ListAccountsAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Accounts);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BankTransactionDraft> SyncTransactionsAsync(
        BankConnection connection,
        BankAccount account,
        string? cursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ReceivedCursors.Add(cursor);
        var scripted = Transactions.ToArray();
        foreach (var draft in scripted)
        {
            ct.ThrowIfCancellationRequested();
            yield return draft;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ConsentSnapshot> GetConsentStatusAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Consent);
    }
}
