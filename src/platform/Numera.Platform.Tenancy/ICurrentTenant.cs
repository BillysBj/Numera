namespace Numera.Platform.Tenancy;

/// <summary>
/// Ambient access to the tenant scoping the current unit of work.
/// Resolved per-request by the tenancy middleware (plan 01-05) and consumed by
/// the <see cref="TenantConnectionInterceptor"/> and EF global query filters.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>The active tenant id, or <c>null</c> when no tenant is in scope
    /// (e.g. design-time, health checks, or pre-authentication).</summary>
    Guid? TenantId { get; }

    /// <summary>Binds the current scope to <paramref name="tenantId"/>.
    /// Called by middleware in production; usable directly in tests.</summary>
    void SetTenant(Guid tenantId);
}
