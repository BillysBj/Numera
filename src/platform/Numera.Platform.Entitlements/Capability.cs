namespace Numera.Platform.Entitlements;

/// <summary>
/// A discrete server-side capability that a tenant's plan (<see cref="Plan"/>) may or
/// may not grant. Endpoints are gated on these via <see cref="IEntitlementService"/> /
/// the plan feature filter — the check is authoritative on the server, not
/// a client-supplied flag.
/// </summary>
/// <remarks>
/// This is an intentionally small, illustrative set for Phase 1 that proves the
/// mechanism. Real feature gates get mapped to these (or new) capabilities in later
/// phases. The plan &#8594; capability wiring is centralised in
/// <see cref="PlanCapabilityMap"/> (the single source of truth).
/// </remarks>
public enum Capability
{
    /// <summary>Multiple users / memberships per tenant (vs. a single seat).</summary>
    MultiUser = 1,

    /// <summary>Export tenant data (CSV/DATEV/etc.).</summary>
    DataExport = 2,

    /// <summary>Automated dunning / payment reminders (Mahnwesen).</summary>
    Dunning = 3,

    /// <summary>Legal e-invoicing (E-Rechnung, ZUGFeRD/XRechnung).</summary>
    EInvoicing = 4,

    /// <summary>Programmatic API access (public REST API / tokens).</summary>
    ApiAccess = 5,
}
