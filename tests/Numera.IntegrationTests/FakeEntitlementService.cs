using Numera.Platform.Entitlements;

namespace Numera.IntegrationTests;

/// <summary>
/// Deterministic <see cref="IEntitlementService"/> test double for the phase-9 tarif-gate
/// tests (09-02) and for the pre-existing e-invoice/dunning tests whose endpoints/handlers
/// now take an entitlement dependency. Grants a fixed capability set with no DB round-trip.
/// </summary>
/// <remarks>
/// Use <see cref="Granting"/> for the L+ happy path (all capabilities granted) and
/// <see cref="Denying"/> for a plan that lacks the gated capability (deny-by-default).
/// </remarks>
internal sealed class FakeEntitlementService : IEntitlementService
{
    private readonly IReadOnlySet<Capability> _granted;

    private FakeEntitlementService(IReadOnlySet<Capability> granted) => _granted = granted;

    /// <summary>Grants every capability — the L/XL happy path.</summary>
    public static FakeEntitlementService Granting { get; } =
        new(new HashSet<Capability>(Enum.GetValues<Capability>()));

    /// <summary>Grants nothing — deny-by-default, as for a plan below the gated tier.</summary>
    public static FakeEntitlementService Denying { get; } =
        new(new HashSet<Capability>());

    /// <summary>Grants exactly the named capabilities — for proving a gate queries the RIGHT one.</summary>
    public static FakeEntitlementService GrantingOnly(params Capability[] capabilities) =>
        new(new HashSet<Capability>(capabilities));

    /// <summary>The most recent capability queried, for assertions.</summary>
    public Capability? RequestedCapability { get; private set; }

    public Task<bool> HasCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
    {
        RequestedCapability = capability;
        return Task.FromResult(_granted.Contains(capability));
    }

    public Task<IReadOnlySet<Capability>> CurrentCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_granted);
}
