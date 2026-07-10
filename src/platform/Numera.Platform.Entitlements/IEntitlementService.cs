namespace Numera.Platform.Entitlements;

/// <summary>
/// Server-authoritative resolution of the current tenant's entitlements. Resolves the
/// tenant's persisted <see cref="Plan"/> (the <c>tenants.plan</c> column) to a
/// <see cref="Capability"/> set via <see cref="PlanCapabilityMap"/>.
/// </summary>
/// <remarks>
/// This is the trust boundary for tier gating: the check reads the tenant's DB plan,
/// never a client-supplied flag. Frontend visibility (plans 01-06/07) is cosmetic; this
/// service is the enforcement point consumed by the plan feature filter.
/// Deny-by-default: when no tenant is in scope, capabilities are empty.
/// </remarks>
public interface IEntitlementService
{
    /// <summary>
    /// Returns <c>true</c> if the current tenant's plan grants <paramref name="capability"/>.
    /// Returns <c>false</c> when no tenant/plan is in scope (deny by default).
    /// </summary>
    Task<bool> HasCapabilityAsync(Capability capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full set of capabilities granted by the current tenant's plan, or an
    /// empty set when no tenant/plan is in scope (deny by default).
    /// </summary>
    Task<IReadOnlySet<Capability>> CurrentCapabilitiesAsync(CancellationToken cancellationToken = default);
}
