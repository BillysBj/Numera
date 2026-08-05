using System.Net;
using System.Runtime.CompilerServices;
using System.Globalization;

using Hangfire;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Banking;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Synchronizes one connected bank account inside its tenant's RLS scope.</summary>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class SyncBankTransactionsJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncBankTransactionsJob> _logger;

    /// <summary>Creates the tenant-scoped transaction synchronization job.</summary>
    public SyncBankTransactionsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SyncBankTransactionsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Pulls one account incrementally and advances its cursor after success.</summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid bankAccountId,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // This ordering is mandatory: resolving the DbContext first would create an
        // unbound RLS context and make both reads and writes invisible or invalid.
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NumeraDbContext>();
        var account = await db.Set<BankAccount>()
            .Include(candidate => candidate.BankConnection)
            .FirstOrDefaultAsync(candidate => candidate.Id == bankAccountId, ct)
            .ConfigureAwait(false);
        if (account?.BankConnection is not { } connection)
        {
            _logger.LogWarning(
                "Bank transaction sync skipped for account {BankAccountId} of tenant {TenantId}: connected account not found.",
                bankAccountId,
                tenantId);
            return;
        }

        if (connection.ConsentStatus is ConsentStatus.Expired or ConsentStatus.Revoked)
        {
            _logger.LogWarning(
                "Bank transaction sync skipped for account {BankAccountId} of tenant {TenantId}: consent is {ConsentStatus}.",
                bankAccountId,
                tenantId,
                connection.ConsentStatus);
            return;
        }

        var provider = services.GetRequiredService<IBankConnectionProvider>();
        var ingest = services.GetRequiredService<BankTransactionIngestService>();
        var cursor = new SyncCursorTracker();

        try
        {
            var drafts = provider.SyncTransactionsAsync(
                connection,
                account,
                account.SyncCursor,
                ct);
            var inserted = await ingest.IngestAsync(
                    account.Id,
                    ObserveAsync(drafts, cursor, ct),
                    ct)
                .ConfigureAwait(false);

            // The ISO value-date cursor intentionally overlaps the newest day. Providers
            // may re-send that day (or their full 89-day window); ingest dedupe is the guard.
            if (cursor.MaximumValueDate is { } maximumValueDate)
            {
                var hasExistingDate = DateOnly.TryParseExact(
                    account.SyncCursor,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var existingDate);
                if (!hasExistingDate || maximumValueDate > existingDate)
                {
                    account.SyncCursor = maximumValueDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture);
                }
            }

            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Synchronized bank account {BankAccountId} for tenant {TenantId}: {InsertedCount} new transactions, cursor {SyncCursor}.",
                bankAccountId,
                tenantId,
                inserted,
                account.SyncCursor);
        }
        catch (Exception exception) when (IsConsentError(exception))
        {
            _logger.LogWarning(
                exception,
                "Bank transaction sync paused for account {BankAccountId} of tenant {TenantId} because provider consent is unavailable.",
                bankAccountId,
                tenantId);
        }
    }

    private static async IAsyncEnumerable<BankTransactionDraft> ObserveAsync(
        IAsyncEnumerable<BankTransactionDraft> drafts,
        SyncCursorTracker cursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var draft in drafts.WithCancellation(ct).ConfigureAwait(false))
        {
            cursor.Observe(draft);
            yield return draft;
        }
    }

    private static bool IsConsentError(Exception exception)
    {
        if (exception is UnauthorizedAccessException
            || exception is HttpRequestException
            {
                StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            })
        {
            return true;
        }

        if (!string.Equals(exception.GetType().Name, "FinApiException", StringComparison.Ordinal))
        {
            return false;
        }

        var statusCode = exception.GetType().GetProperty("StatusCode")?.GetValue(exception);
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || statusCode is int numericStatusCode
                && numericStatusCode is (int)HttpStatusCode.Unauthorized
                    or (int)HttpStatusCode.Forbidden;
    }

    private sealed class SyncCursorTracker
    {
        public DateOnly? MaximumValueDate { get; private set; }

        public void Observe(BankTransactionDraft draft)
        {
            if (MaximumValueDate is null || draft.ValueDate > MaximumValueDate)
            {
                MaximumValueDate = draft.ValueDate;
            }
        }
    }
}

/// <summary>Discovers connected accounts and schedules staggered tenant-scoped sync jobs.</summary>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class SyncBankTransactionsFanOutJob
{
    private readonly IConfiguration _configuration;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<SyncBankTransactionsFanOutJob> _logger;

    /// <summary>Creates the tenant-agnostic account fan-out.</summary>
    public SyncBankTransactionsFanOutJob(
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobs,
        ILogger<SyncBankTransactionsFanOutJob> logger)
    {
        _configuration = configuration;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    /// <summary>Schedules one worker-queue sync per connected account.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var connectionString = _configuration.GetConnectionString("Hangfire")
            ?? _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Hangfire or ConnectionStrings:Default is required for banking fan-out.");
        var staggerSeconds = Math.Max(
            0,
            _configuration.GetValue<int?>("Banking:SyncStaggerSeconds") ?? 5);
        var accounts = new List<(Guid TenantId, Guid AccountId)>();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT tenant_id, id FROM bank_account WHERE bank_connection_id IS NOT NULL ORDER BY tenant_id, id";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                accounts.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            var delay = TimeSpan.FromSeconds((long)index * staggerSeconds);
            _backgroundJobs.Schedule<SyncBankTransactionsJob>(
                job => job.RunAsync(account.TenantId, account.AccountId, CancellationToken.None),
                delay);
        }

        _logger.LogInformation(
            "Scheduled {AccountCount} connected bank accounts with a {StaggerSeconds}-second stagger.",
            accounts.Count,
            staggerSeconds);
    }
}
