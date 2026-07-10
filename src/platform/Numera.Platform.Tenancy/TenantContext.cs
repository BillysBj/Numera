namespace Numera.Platform.Tenancy;

/// <summary>
/// Scoped, mutable holder for the current tenant id. One instance lives per
/// DI scope (per HTTP request / per job). The middleware in plan 01-05 sets the
/// tenant from the authenticated principal; tests set it explicitly.
/// </summary>
public sealed class TenantContext : ICurrentTenant
{
    /// <inheritdoc />
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        TenantId = tenantId;
    }
}
