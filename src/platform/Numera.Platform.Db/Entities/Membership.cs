namespace Numera.Platform.Db.Entities;

/// <summary>
/// The role a member holds within a tenant. Mirrors the Keycloak organization role
/// and is the seam for the Phase 8 authorization model; default is
/// <see cref="MembershipRole.Owner"/>.
/// </summary>
public enum MembershipRole
{
    /// <summary>Owner/Inhaber — full control of the tenant.</summary>
    Owner = 1,

    /// <summary>Employee/Mitarbeiter — scoped operational access.</summary>
    Employee = 2,

    /// <summary>External tax advisor (Steuerberater) — read/export focused access.</summary>
    TaxAdvisor = 3,
}

/// <summary>
/// A user's membership in a tenant — the local mirror of a Keycloak organization
/// membership. Lets the app resolve "which tenants may this user act in" and the
/// user's role there without a round-trip to Keycloak on every request.
/// </summary>
/// <remarks>
/// Tenant-scoped (<see cref="ITenantEntity"/>): subject to the same RLS
/// <c>tenant_isolation</c> policy and tenant-leading composite index as every other
/// tenant table.
/// </remarks>
public sealed class Membership : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The Keycloak user id (the <c>sub</c> claim of the user's token).</summary>
    public Guid UserId { get; init; }

    /// <summary>The member's role in this tenant. Default Owner.</summary>
    public MembershipRole Role { get; set; } = MembershipRole.Owner;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation to the owning <see cref="Tenant"/>.</summary>
    public Tenant? Tenant { get; init; }
}
