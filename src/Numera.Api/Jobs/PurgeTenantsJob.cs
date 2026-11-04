using Hangfire;

using Npgsql;

using Numera.Api.Lifecycle;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>
/// Daily sweep (Phase B1 Scheibe 2): discovers tenants whose deletion grace window has elapsed
/// (<c>deletion_scheduled_for &lt;= now()</c>) and schedules one hard-purge job per tenant.
/// </summary>
/// <remarks>
/// A sweep is more robust than a single delayed job per request: it survives worker restarts and
/// naturally re-attempts any tenant that is still due. It reads the <c>tenants</c> table on the
/// privileged maintenance connection (Postgres superuser, which bypasses RLS) so it can see every
/// tenant cross-scope — the only way to enumerate due tenants without a per-tenant context.
/// </remarks>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class PurgeDueTenantsFanOutJob
{
    private readonly IConfiguration _configuration;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<PurgeDueTenantsFanOutJob> _logger;

    /// <summary>Creates the tenant-agnostic purge fan-out.</summary>
    public PurgeDueTenantsFanOutJob(
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobs,
        ILogger<PurgeDueTenantsFanOutJob> logger)
    {
        _configuration = configuration;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    /// <summary>Schedules one worker-queue purge per tenant whose grace window has elapsed.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var connectionString = _configuration.GetConnectionString("Maintenance")
            ?? _configuration.GetConnectionString("Hangfire")
            ?? _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "A privileged connection (ConnectionStrings:Maintenance/Hangfire/Default) is required "
                + "for the tenant purge sweep.");

        var due = new List<Guid>();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id FROM tenants "
                + "WHERE deletion_scheduled_for IS NOT NULL AND deletion_scheduled_for <= now() "
                + "ORDER BY deletion_scheduled_for";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                due.Add(reader.GetGuid(0));
            }
        }

        foreach (var tenantId in due)
        {
            _backgroundJobs.Enqueue<PurgeTenantJob>(job => job.RunAsync(tenantId, CancellationToken.None));
        }

        _logger.LogInformation("Scheduled {TenantCount} tenant(s) due for hard purge.", due.Count);
    }
}

/// <summary>Runs the irreversible hard-purge of a single tenant inside its own RLS scope.</summary>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class PurgeTenantJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PurgeTenantJob> _logger;

    /// <summary>Creates the per-tenant purge job.</summary>
    public PurgeTenantJob(IServiceScopeFactory scopeFactory, ILogger<PurgeTenantJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Establishes the tenant context and runs the purge orchestration.</summary>
    public async Task RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Set the tenant BEFORE resolving the DbContext so the orchestrator's tenant-scoped reads
        // run under the correct RLS scope (mirrors SyncBankTransactionsJob).
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var orchestrator = scope.ServiceProvider.GetRequiredService<TenantPurgeOrchestrator>();
        var result = await orchestrator.PurgeAsync(tenantId, ct).ConfigureAwait(false);
        if (result is null)
        {
            _logger.LogInformation("Tenant {TenantId} was already purged.", tenantId);
        }
    }
}
