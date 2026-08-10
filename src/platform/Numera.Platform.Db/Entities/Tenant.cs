namespace Numera.Platform.Db.Entities;

/// <summary>
/// The subscription plan (Tarif) a tenant is on. Drives the entitlement/limit
/// checks introduced in plan 01-04; the column is a stable seam only in this plan
/// (no entitlement logic here). Default is <see cref="TenantPlan.S"/>.
/// </summary>
public enum TenantPlan
{
    /// <summary>Smallest tier (default for new tenants).</summary>
    S = 1,

    /// <summary>Medium tier.</summary>
    M = 2,

    /// <summary>Large tier.</summary>
    L = 3,

    /// <summary>Extra-large tier.</summary>
    XL = 4,

    /// <summary>Degraded read-only tier used when trial or paid access has lapsed.</summary>
    Free = 5,
}

/// <summary>
/// A tenant (Mandant) — the top-level isolation boundary of the platform. Each
/// tenant maps 1:1 to a Keycloak <c>organization</c>; the primary key IS the
/// Keycloak organization id.
/// </summary>
/// <remarks>
/// <para>
/// The <c>tenants</c> table is special with respect to Row-Level Security: it IS
/// the tenant rather than being owned by one, so it does not carry a
/// <c>tenant_id</c> column and is not an <see cref="ITenantEntity"/>. Its RLS
/// policy (plan 01-02 migration) is <b>self-scoped</b>: a tenant may see and touch
/// only its own row via <c>id = current_setting('app.current_tenant')::uuid</c>.
/// </para>
/// <para>
/// RLS is still <c>ENABLE</c>d and <c>FORCE</c>d on this table so that even the
/// table owner is subject to the policy (defence against ownership-bypass).
/// </para>
/// </remarks>
public sealed class Tenant
{
    /// <summary>
    /// UUIDv7 primary key. This is the Keycloak organization id and the value that
    /// the <c>app.current_tenant</c> GUC is set to for every request.
    /// </summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Display name of the tenant (Firmenname).</summary>
    public required string Name { get; set; }

    /// <summary>The subscription plan; feeds plan 01-04 entitlements. Default S.</summary>
    public TenantPlan Plan { get; set; } = TenantPlan.S;

    /// <summary>Stripe customer id used for Checkout and Customer Portal linkage.</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>The tenant's current Stripe subscription id, when one exists.</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>The raw Stripe subscription status (for example active or past_due).</summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>End of the current paid Stripe subscription period, in UTC.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>End of the app-managed, no-card trial period, in UTC.</summary>
    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Memberships (user&lt;-&gt;tenant links) belonging to this tenant.</summary>
    public ICollection<Membership> Memberships { get; init; } = [];
}
