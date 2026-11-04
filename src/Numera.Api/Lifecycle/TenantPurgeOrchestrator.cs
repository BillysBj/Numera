using Microsoft.EntityFrameworkCore;

using Numera.Modules.Banking;
using Numera.Modules.Billing;
using Numera.Platform.Db;

namespace Numera.Api.Lifecycle;

/// <summary>
/// Drives the full hard-purge of one tenant (Phase B1 Scheibe 2): cancels external provider
/// state (Stripe, finAPI) and the Keycloak footprint as best-effort steps, then runs the
/// irreversible, GoBD-trigger-bypassing database purge and writes the append-only log.
/// </summary>
/// <remarks>
/// The database purge is the point of no return and always runs; the external steps are
/// best-effort and their outcome (success / skipped / failed) is recorded in the log so an
/// operator can follow up on any provider that could not be reached. The caller MUST have set the
/// tenant context (<see cref="Numera.Platform.Tenancy.ICurrentTenant.SetTenant"/>) before invoking
/// this so the tenant-scoped reads below resolve under the correct RLS scope.
/// </remarks>
public sealed class TenantPurgeOrchestrator
{
    private readonly NumeraDbContext _db;
    private readonly IBillingProvider _billing;
    private readonly IBankConnectionProvider _banking;
    private readonly KeycloakTenantPurger _keycloak;
    private readonly TenantPurgeService _purge;
    private readonly ILogger<TenantPurgeOrchestrator> _logger;

    /// <summary>Creates the orchestrator over its tenant-scoped and provider dependencies.</summary>
    public TenantPurgeOrchestrator(
        NumeraDbContext db,
        IBillingProvider billing,
        IBankConnectionProvider banking,
        KeycloakTenantPurger keycloak,
        TenantPurgeService purge,
        ILogger<TenantPurgeOrchestrator> logger)
    {
        _db = db;
        _billing = billing;
        _banking = banking;
        _keycloak = keycloak;
        _purge = purge;
        _logger = logger;
    }

    /// <summary>Purges the tenant whose context is already set. A no-op if it no longer exists.</summary>
    public async Task<TenantPurgeDbResult?> PurgeAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            _logger.LogInformation(
                "Tenant purge skipped for {TenantId}: already purged (no tenants row).", tenantId);
            return null;
        }

        var stripeOutcome = await CancelStripeAsync(tenant.StripeSubscriptionId, ct).ConfigureAwait(false);
        var finapiOutcome = await PurgeFinApiAsync(ct).ConfigureAwait(false);
        var keycloakOutcome = await PurgeKeycloakAsync(tenantId, ct).ConfigureAwait(false);

        var record = new TenantPurgeRecord(
            tenantId,
            tenant.Name,
            tenant.DeletionRequestedBy,
            tenant.DeletionRequestedAt,
            keycloakOutcome,
            finapiOutcome,
            stripeOutcome);

        var result = await _purge.PurgeDatabaseAndLogAsync(record, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tenant {TenantId} ({TenantName}) purged: {RowsDeleted} rows across {TableCount} tables. "
            + "Stripe={Stripe}, finAPI={FinApi}, Keycloak={Keycloak}.",
            tenantId,
            tenant.Name,
            result.RowsDeleted,
            result.RowCounts.Count,
            stripeOutcome,
            finapiOutcome,
            keycloakOutcome);

        return result;
    }

    private async Task<string> CancelStripeAsync(string? subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return "none";
        }

        try
        {
            await _billing.CancelSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
            return $"cancelled:{subscriptionId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Tenant purge could not cancel Stripe subscription {SubscriptionId}.", subscriptionId);
            return $"failed:{Truncate(ex.Message)}";
        }
    }

    // Deletes the tenant's finAPI sub-user(s). EnsureUserAsync reuses one sub-user across a tenant's
    // bank connections, so deleting that sub-user once cascades to all of its connections, accounts,
    // transactions and consents. We therefore de-duplicate by the stored (encrypted) sub-user id and
    // delete each distinct sub-user exactly once. Best-effort: a provider failure is logged and
    // recorded, but never blocks the irreversible DB purge that follows.
    private async Task<string> PurgeFinApiAsync(CancellationToken ct)
    {
        var connections = await _db.Set<BankConnection>()
            .AsNoTracking()
            .Where(connection => connection.FinApiUserId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (connections.Count == 0)
        {
            return "none";
        }

        var distinctSubUsers = connections
            .GroupBy(connection => connection.FinApiUserId!, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var deleted = 0;
        var failed = 0;
        foreach (var connection in distinctSubUsers)
        {
            try
            {
                if (await _banking.DeleteProviderDataAsync(connection, ct).ConfigureAwait(false))
                {
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(
                    ex,
                    "Tenant purge could not delete finAPI data for bank connection {ConnectionId}.",
                    connection.Id);
            }
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "Tenant purge: {Failed} finAPI sub-user(s) could not be deleted and require manual "
                + "cleanup at the provider ({Deleted} deleted).",
                failed,
                deleted);
            return $"deleted:{deleted};failed:{failed}";
        }

        return deleted == 0 ? "none" : $"deleted:{deleted}users";
    }

    private async Task<string> PurgeKeycloakAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_keycloak.IsConfigured)
        {
            return "skipped:not-configured";
        }

        try
        {
            var users = await _keycloak.PurgeAsync(tenantId, ct).ConfigureAwait(false);
            return $"purged:{users}users";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant purge could not fully remove Keycloak org {TenantId}.", tenantId);
            return $"failed:{Truncate(ex.Message)}";
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200];
}
