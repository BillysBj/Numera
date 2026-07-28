using System.Collections.Frozen;

namespace Numera.Platform.Entitlements;

/// <summary>
/// The single source of truth mapping each subscription <see cref="Plan"/> (S/M/L/XL)
/// to the fixed set of <see cref="Capability"/> values it grants. Capability sets are
/// strictly increasing with tier (S &#8838; M &#8838; L &#8838; XL), so an upgrade only ever
/// adds capabilities.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONLY place capabilities-per-plan are declared in code. Everything else
/// (<see cref="EntitlementService"/>, the plan feature filter) resolves through
/// here, keyed off the tenant's DB plan column. A future billing webhook flips one
/// column (<c>tenants.plan</c>) and the entire capability surface follows from this map.
/// </para>
/// <para>
/// No payment/Stripe logic lives anywhere in this assembly — entitlements are purely a
/// projection of the persisted plan.
/// </para>
/// </remarks>
public static class PlanCapabilityMap
{
    private static readonly IReadOnlySet<Capability> Empty =
        FrozenSet<Capability>.Empty;

    // S: smallest tier — a single seat with the ability to get its data back out.
    private static readonly IReadOnlySet<Capability> SmallCaps = FrozenSet.ToFrozenSet(
    [
        Capability.DataExport,
    ]);

    // M: adds collaboration (multiple users).
    private static readonly IReadOnlySet<Capability> MediumCaps = FrozenSet.ToFrozenSet(
    [
        Capability.DataExport,
        Capability.MultiUser,
    ]);

    // L: adds dunning and legal e-invoicing.
    private static readonly IReadOnlySet<Capability> LargeCaps = FrozenSet.ToFrozenSet(
    [
        Capability.DataExport,
        Capability.MultiUser,
        Capability.Dunning,
        Capability.EInvoicing,
        Capability.ForeignCurrencyInvoicing,
        Capability.RecurringInvoices,
        Capability.DownPaymentInvoices,
    ]);

    // XL: full set including programmatic API access.
    private static readonly IReadOnlySet<Capability> ExtraLargeCaps = FrozenSet.ToFrozenSet(
    [
        Capability.DataExport,
        Capability.MultiUser,
        Capability.Dunning,
        Capability.EInvoicing,
        Capability.ApiAccess,
        Capability.ForeignCurrencyInvoicing,
        Capability.RecurringInvoices,
        Capability.DownPaymentInvoices,
    ]);

    /// <summary>
    /// The plan &#8594; capability matrix. Keyed by <see cref="Plan"/>; each value is the
    /// immutable capability set granted by that plan.
    /// </summary>
    public static readonly IReadOnlyDictionary<Plan, IReadOnlySet<Capability>> Matrix =
        new Dictionary<Plan, IReadOnlySet<Capability>>
        {
            [Plan.S] = SmallCaps,
            [Plan.M] = MediumCaps,
            [Plan.L] = LargeCaps,
            [Plan.XL] = ExtraLargeCaps,
        }.ToFrozenDictionary();

    /// <summary>
    /// Returns the capability set granted by <paramref name="plan"/>. Falls back to the
    /// empty set (deny-by-default) for any plan value not present in the matrix.
    /// </summary>
    public static IReadOnlySet<Capability> For(Plan plan) =>
        Matrix.TryGetValue(plan, out var caps) ? caps : Empty;
}
