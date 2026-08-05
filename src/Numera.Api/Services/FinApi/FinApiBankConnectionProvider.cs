using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Banking;
using Numera.Platform.Db;

namespace Numera.Api.Services.FinApi;

/// <summary>Live finAPI sandbox implementation of the provider-neutral banking port.</summary>
public sealed class FinApiBankConnectionProvider : IBankConnectionProvider
{
    private readonly FinApiClient _client;
    private readonly IBankCredentialProtector _credentials;
    private readonly NumeraDbContext _db;

    /// <summary>Creates a provider over the narrow finAPI client and encrypted credential store.</summary>
    public FinApiBankConnectionProvider(
        FinApiClient client,
        IBankCredentialProtector credentials,
        NumeraDbContext db)
    {
        _client = client;
        _credentials = credentials;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<WebFormSession> StartImportAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        var userToken = await GetUserTokenAsync(connection, ct).ConfigureAwait(false);
        var webForm = await _client.StartWebFormImportAsync(userToken, ct).ConfigureAwait(false);

        connection.Provider = BankProvider.FinApi;
        connection.ConsentStatus = ConsentStatus.Pending;
        connection.ConsentExpiresAt = null;
        connection.WebFormId = webForm.WebFormId;
        connection.WebFormStatus = "PENDING";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new WebFormSession(webForm.WebFormId, webForm.RedirectUrl);
    }

    /// <inheritdoc />
    public async Task<WebFormSession> StartReauthAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        var userToken = await GetUserTokenAsync(connection, ct).ConfigureAwait(false);
        var bankConnectionId = await ResolveBankConnectionIdAsync(
                connection,
                userToken,
                ct)
            .ConfigureAwait(false);
        var webForm = await _client
            .StartWebFormUpdateAsync(userToken, bankConnectionId, ct)
            .ConfigureAwait(false);

        connection.ConsentStatus = ConsentStatus.Pending;
        connection.WebFormId = webForm.WebFormId;
        connection.WebFormStatus = "PENDING";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new WebFormSession(webForm.WebFormId, webForm.RedirectUrl);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankAccountDraft>> ListAccountsAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        var userToken = await GetUserTokenAsync(connection, ct).ConfigureAwait(false);
        var accounts = await _client.GetAccountsAsync(userToken, ct).ConfigureAwait(false);
        return accounts
            .Select(account => new BankAccountDraft(
                account.Id,
                account.Iban,
                account.DisplayName,
                account.Currency))
            .ToArray();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BankTransactionDraft> SyncTransactionsAsync(
        BankConnection connection,
        BankAccount account,
        string? cursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.FinApiAccountId))
        {
            throw new InvalidOperationException(
                "A finAPI-backed bank account requires a provider account id.");
        }

        var userToken = await GetUserTokenAsync(connection, ct).ConfigureAwait(false);
        var transactions = await _client.GetTransactionsAsync(
                userToken,
                account.FinApiAccountId,
                cursor,
                ct)
            .ConfigureAwait(false);
        foreach (var transaction in transactions)
        {
            ct.ThrowIfCancellationRequested();
            yield return new BankTransactionDraft(
                transaction.ProviderId,
                transaction.Amount,
                transaction.ValueDate,
                transaction.BookingDate,
                transaction.Purpose,
                transaction.CounterpartyName,
                transaction.CounterpartyIban,
                transaction.EndToEndId,
                BankTransactionSource.FinApi);
        }
    }

    /// <inheritdoc />
    public async Task<ConsentSnapshot> GetConsentStatusAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        var userToken = await GetUserTokenAsync(connection, ct).ConfigureAwait(false);
        var bankConnectionId = await ResolveBankConnectionIdAsync(
                connection,
                userToken,
                ct)
            .ConfigureAwait(false);
        var consent = await _client
            .GetConsentStatusAsync(userToken, bankConnectionId, ct)
            .ConfigureAwait(false);
        return new ConsentSnapshot(MapConsentStatus(consent), consent.ExpiresAt);
    }

    private async Task<string> GetUserTokenAsync(
        BankConnection connection,
        CancellationToken ct)
    {
        await EnsureUserAsync(connection, ct).ConfigureAwait(false);
        var userId = _credentials.Unprotect(connection.FinApiUserId!);
        var secret = _credentials.Unprotect(connection.FinApiUserSecret!);
        var userToken = await _client
            .GetUserTokenAsync(userId, secret, ct)
            .ConfigureAwait(false);

        // Tokens are deliberately refreshed at the boundary because the current model
        // has no token-expiry column. Persist only the encrypted value for diagnostics/
        // continuity; plaintext lives solely in this call scope.
        connection.AccessTokenCipher = _credentials.Protect(userToken);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return userToken;
    }

    private async Task EnsureUserAsync(BankConnection connection, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(connection.FinApiUserId)
            && !string.IsNullOrWhiteSpace(connection.FinApiUserSecret))
        {
            return;
        }

        // Reuse an already provisioned encrypted sub-user inside the same RLS tenant.
        // The query cannot see another tenant's credential columns.
        var existing = await _db.Set<BankConnection>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id != connection.Id
                && candidate.TenantId == connection.TenantId
                && candidate.FinApiUserId != null
                && candidate.FinApiUserSecret != null)
            .Select(candidate => new
            {
                candidate.FinApiUserId,
                candidate.FinApiUserSecret,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            connection.FinApiUserId = existing.FinApiUserId;
            connection.FinApiUserSecret = existing.FinApiUserSecret;
        }
        else
        {
            var created = await _client.CreateUserAsync(ct).ConfigureAwait(false);
            connection.FinApiUserId = _credentials.Protect(created.UserId);
            connection.FinApiUserSecret = _credentials.Protect(created.Secret);
        }

        connection.Provider = BankProvider.FinApi;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ResolveBankConnectionIdAsync(
        BankConnection connection,
        string userToken,
        CancellationToken ct)
    {
        var persistedAccountIds = await _db.Set<BankAccount>()
            .AsNoTracking()
            .Where(account =>
                account.BankConnectionId == connection.Id
                && account.FinApiAccountId != null)
            .Select(account => account.FinApiAccountId!)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var accounts = await _client.GetAccountsAsync(userToken, ct).ConfigureAwait(false);

        var matchingIds = accounts
            .Where(account => persistedAccountIds.Contains(account.Id, StringComparer.Ordinal))
            .Select(account => account.BankConnectionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matchingIds.Length == 1)
        {
            return matchingIds[0]!;
        }

        var allIds = accounts
            .Select(account => account.BankConnectionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (allIds.Length == 1)
        {
            return allIds[0]!;
        }

        throw new InvalidOperationException(
            "The finAPI bank connection could not be identified unambiguously from its accounts.");
    }

    private static ConsentStatus MapConsentStatus(FinApiConsent consent)
    {
        if (consent.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return ConsentStatus.Expired;
        }

        return consent.Status.Trim().ToUpperInvariant() switch
        {
            "ACTIVE" or "READY" => ConsentStatus.Active,
            "EXPIRED" => ConsentStatus.Expired,
            "REVOKED" or "DISABLED" or "DELETED" => ConsentStatus.Revoked,
            _ => ConsentStatus.Pending,
        };
    }
}
