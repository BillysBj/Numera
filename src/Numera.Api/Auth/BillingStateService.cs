using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Auth;

/// <summary>Resolves the current tenant's request-time billing access state.</summary>
public interface IBillingState
{
    /// <summary>Returns whether the current tenant must be restricted to read-only access.</summary>
    Task<bool> IsDegradedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the advisory effective plan for billing status and presentation.</summary>
    Task<TenantPlan> EffectivePlanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Computes billing degradation from the tenant's subscription status and app-managed
/// <c>trial_ends_at</c> deadline at request time. No recurring trial-sweep job is required.
/// </summary>
/// <remarks>
/// Registered scoped. The RLS-scoped tenant billing row is loaded at most once per scope.
/// The write guard is authoritative; the effective plan is advisory and must not replace
/// the existing entitlement service's direct <c>tenants.plan</c> lookup.
/// </remarks>
public sealed class BillingStateService : IBillingState
{
    private readonly ICurrentTenant _currentTenant;
    private readonly NumeraDbContext _db;

    private BillingStateSnapshot? _cached;

    /// <summary>Creates the scoped billing-state resolver.</summary>
    public BillingStateService(ICurrentTenant currentTenant, NumeraDbContext db)
    {
        _currentTenant = currentTenant;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<bool> IsDegradedAsync(CancellationToken cancellationToken = default) =>
        (await CurrentStateAsync(cancellationToken).ConfigureAwait(false)).IsDegraded;

    /// <inheritdoc />
    public async Task<TenantPlan> EffectivePlanAsync(CancellationToken cancellationToken = default)
    {
        var state = await CurrentStateAsync(cancellationToken).ConfigureAwait(false);
        return state.IsDegraded ? TenantPlan.Free : state.StoredPlan;
    }

    private async Task<BillingStateSnapshot> CurrentStateAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var tenantId = _currentTenant.TenantId;
        if (tenantId is null)
        {
            // Billing degradation is not applicable without a tenant in scope.
            return _cached = new BillingStateSnapshot(TenantPlan.Free, IsDegraded: false);
        }

        var billing = await _db.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId.Value)
            .Select(tenant => new
            {
                tenant.Plan,
                tenant.SubscriptionStatus,
                tenant.TrialEndsAt,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (billing is null)
        {
            return _cached = new BillingStateSnapshot(TenantPlan.Free, IsDegraded: false);
        }

        var subscriptionAllowsAccess = billing.SubscriptionStatus is
            "trialing" or "active" or "past_due";
        var trialAllowsAccess = billing.TrialEndsAt is { } trialEndsAt
            && trialEndsAt > DateTimeOffset.UtcNow;

        return _cached = new BillingStateSnapshot(
            billing.Plan,
            IsDegraded: !(subscriptionAllowsAccess || trialAllowsAccess));
    }

    private sealed record BillingStateSnapshot(TenantPlan StoredPlan, bool IsDegraded);
}
