using System.Collections.Frozen;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Platform.Entitlements;

/// <summary>
/// Default <see cref="IEntitlementService"/>. Resolves the current tenant via
/// <see cref="ICurrentTenant"/>, loads that tenant's <see cref="Plan"/> from
/// <see cref="NumeraDbContext"/>, and projects it to a capability set through
/// <see cref="PlanCapabilityMap"/> (the single source of truth).
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped (per request/job). The resolved capability set is memoised for the
/// lifetime of the scope so repeated <see cref="HasCapabilityAsync"/> checks within one
/// request incur a single DB read.
/// </para>
/// <para>
/// In subscription mode, if no tenant is in scope, or the tenant row cannot be found, the
/// capability set is empty. Reading <c>tenants.plan</c> is itself RLS-scoped — the tenant
/// can only see its own row — so this cannot be spoofed by the caller.
/// </para>
/// </remarks>
public sealed class EntitlementService : IEntitlementService
{
    private static readonly IReadOnlySet<Capability> AllCapabilities =
        Enum.GetValues<Capability>().ToFrozenSet();

    private readonly ICurrentTenant _currentTenant;
    private readonly NumeraDbContext _db;
    private readonly bool _selfHosted;

    private IReadOnlySet<Capability>? _cached;

    /// <summary>Creates the service for the current DI scope.</summary>
    public EntitlementService(ICurrentTenant currentTenant, NumeraDbContext db, IOptions<BillingOptions> options)
    {
        _currentTenant = currentTenant;
        _db = db;
        _selfHosted = options.Value.SelfHosted;
    }

    /// <inheritdoc />
    public async Task<bool> HasCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
    {
        if (_selfHosted)
        {
            return true;
        }

        var caps = await CurrentCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return caps.Contains(capability);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Capability>> CurrentCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_selfHosted)
        {
            return AllCapabilities;
        }

        if (_cached is not null)
        {
            return _cached;
        }

        var tenantId = _currentTenant.TenantId;
        if (tenantId is null)
        {
            // No tenant in scope -> deny by default.
            return _cached = System.Collections.Frozen.FrozenSet<Capability>.Empty;
        }

        // RLS self-scopes the tenants table to the current tenant, so this returns at
        // most one row (the caller's own). Projecting just the plan keeps it cheap.
        var plan = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId.Value)
            .Select(t => (Plan?)t.Plan)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        _cached = plan is { } p
            ? PlanCapabilityMap.For(p)
            : System.Collections.Frozen.FrozenSet<Capability>.Empty;

        return _cached;
    }
}
