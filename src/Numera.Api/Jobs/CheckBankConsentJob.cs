using Hangfire;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Npgsql;

using Numera.Api.Services.FinApi;
using Numera.Modules.Banking;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Refreshes one tenant bank connection's PSD2 consent health.</summary>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class CheckBankConsentJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly FinApiOptions _options;
    private readonly ILogger<CheckBankConsentJob> _logger;

    /// <summary>Creates the worker-queue consent health job.</summary>
    public CheckBankConsentJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<FinApiOptions> options,
        ILogger<CheckBankConsentJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Discovers every connection and refreshes it in its own RLS tenant scope.</summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var connectionString = _configuration.GetConnectionString("Hangfire")
            ?? _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Hangfire or ConnectionStrings:Default is required for consent checks.");
        var connections = new List<(Guid TenantId, Guid ConnectionId)>();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT tenant_id, id FROM bank_connection ORDER BY tenant_id, id";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                connections.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        foreach (var connection in connections)
        {
            await RunAsync(connection.TenantId, connection.ConnectionId, ct)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Refreshed consent for {ConnectionCount} bank connections.",
            connections.Count);
    }

    /// <summary>Sets tenant context, refreshes consent, and flags upcoming expiry.</summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid bankConnectionId,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Establish the RLS tenant before resolving or using NumeraDbContext.
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NumeraDbContext>();
        var connection = await db.Set<BankConnection>()
            .FirstOrDefaultAsync(candidate => candidate.Id == bankConnectionId, ct)
            .ConfigureAwait(false);
        if (connection is null)
        {
            _logger.LogWarning(
                "Consent check skipped for bank connection {BankConnectionId} of tenant {TenantId}: connection not found.",
                bankConnectionId,
                tenantId);
            return;
        }

        var provider = services.GetRequiredService<IBankConnectionProvider>();
        var snapshot = await provider.GetConsentStatusAsync(connection, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var warnAt = now.AddDays(Math.Max(0, _options.ConsentWarnDays));

        connection.ConsentExpiresAt = snapshot.ExpiresAt;
        connection.ConsentStatus = snapshot.Status;
        if (snapshot.ExpiresAt is { } expiresAt)
        {
            if (expiresAt <= now)
            {
                connection.ConsentStatus = ConsentStatus.Expired;
            }
            else if (snapshot.Status == ConsentStatus.Active && expiresAt <= warnAt)
            {
                // The existing phase-13 model has no separate Expiring enum/flag. Pending
                // plus the retained expiry date is the explicit UI signal for one-click
                // re-consent without changing a 13-01-owned entity or migration.
                connection.ConsentStatus = ConsentStatus.Pending;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Refreshed consent for bank connection {BankConnectionId} of tenant {TenantId}: {ConsentStatus}, expires {ConsentExpiresAt}.",
            bankConnectionId,
            tenantId,
            connection.ConsentStatus,
            connection.ConsentExpiresAt);
    }
}
