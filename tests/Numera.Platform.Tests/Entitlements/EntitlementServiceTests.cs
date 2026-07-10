using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Entitlements;

/// <summary>
/// Verifies server-authoritative tier entitlement resolution: a tenant's plan
/// (S/M/L/XL) column resolves to a fixed capability set via <see cref="PlanCapabilityMap"/>,
/// deny-by-default when no tenant is in scope, and that <see cref="PlanFeatureFilter"/>
/// delegates to <see cref="IEntitlementService"/> (grant vs. deny).
/// </summary>
public sealed class EntitlementServiceTests
{
    // --- Test doubles -------------------------------------------------------

    /// <summary>A directly-settable <see cref="ICurrentTenant"/> for tests.</summary>
    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }

        public void SetTenant(Guid tenantId) => TenantId = tenantId;

        public void Clear() => TenantId = null;
    }

    /// <summary>
    /// Builds a fresh in-memory <see cref="NumeraDbContext"/> (unique DB name per call)
    /// seeded with a single tenant on <paramref name="plan"/>, plus the fake tenant
    /// pointing at it. Returns the service under test and the seeded tenant id.
    /// </summary>
    private static (EntitlementService Service, FakeCurrentTenant Tenant, Guid TenantId) BuildOnPlan(TenantPlan plan)
    {
        var currentTenant = new FakeCurrentTenant();
        var options = new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new NumeraDbContext(options, currentTenant);
        var tenant = new Tenant { Name = "Test GmbH", Plan = plan };
        db.Tenants.Add(tenant);
        db.SaveChanges();

        currentTenant.SetTenant(tenant.Id);
        return (new EntitlementService(currentTenant, db), currentTenant, tenant.Id);
    }

    private static PlanFeatureFilter FilterFor(EntitlementService service) => new(service);

    private static FeatureFilterEvaluationContext GateContext(string capability)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Capability"] = capability,
            })
            .Build();

        return new FeatureFilterEvaluationContext
        {
            FeatureName = capability,
            Parameters = config,
        };
    }

    // --- Resolution ---------------------------------------------------------

    [Fact]
    public async Task S_plan_resolves_minimal_set_without_einvoicing_or_apiaccess()
    {
        var (service, _, _) = BuildOnPlan(TenantPlan.S);

        var caps = await service.CurrentCapabilitiesAsync();

        Assert.Contains(Capability.DataExport, caps);
        Assert.DoesNotContain(Capability.EInvoicing, caps);
        Assert.DoesNotContain(Capability.ApiAccess, caps);
        Assert.DoesNotContain(Capability.MultiUser, caps);
    }

    [Fact]
    public async Task XL_plan_resolves_full_capability_set()
    {
        var (service, _, _) = BuildOnPlan(TenantPlan.XL);

        var caps = await service.CurrentCapabilitiesAsync();

        Assert.Contains(Capability.DataExport, caps);
        Assert.Contains(Capability.MultiUser, caps);
        Assert.Contains(Capability.Dunning, caps);
        Assert.Contains(Capability.EInvoicing, caps);
        Assert.Contains(Capability.ApiAccess, caps);
        Assert.Equal(Enum.GetValues<Capability>().Length, caps.Count);
    }

    [Fact]
    public async Task Capability_sets_are_strictly_increasing_by_tier()
    {
        var s = PlanCapabilityMap.For(TenantPlan.S);
        var m = PlanCapabilityMap.For(TenantPlan.M);
        var l = PlanCapabilityMap.For(TenantPlan.L);
        var xl = PlanCapabilityMap.For(TenantPlan.XL);

        Assert.True(s.IsSubsetOf(m));
        Assert.True(m.IsSubsetOf(l));
        Assert.True(l.IsSubsetOf(xl));
        Assert.True(s.Count < xl.Count);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task No_tenant_yields_empty_capabilities_deny_by_default()
    {
        var (service, tenant, _) = BuildOnPlan(TenantPlan.XL);
        tenant.Clear(); // no tenant in scope

        var caps = await service.CurrentCapabilitiesAsync();

        Assert.Empty(caps);
        Assert.False(await service.HasCapabilityAsync(Capability.EInvoicing));
    }

    [Fact]
    public async Task Unknown_tenant_row_yields_empty_capabilities()
    {
        var (service, tenant, _) = BuildOnPlan(TenantPlan.XL);
        tenant.SetTenant(Guid.CreateVersion7()); // points at a non-existent tenant

        var caps = await service.CurrentCapabilitiesAsync();

        Assert.Empty(caps);
    }

    // --- Feature filter (gate) ---------------------------------------------

    [Fact]
    public async Task Filter_returns_true_for_capability_the_plan_grants()
    {
        var (service, _, _) = BuildOnPlan(TenantPlan.L); // L grants EInvoicing
        var filter = FilterFor(service);

        var enabled = await filter.EvaluateAsync(GateContext(nameof(Capability.EInvoicing)));

        Assert.True(enabled);
    }

    [Fact]
    public async Task Filter_returns_false_for_capability_the_plan_lacks()
    {
        var (service, _, _) = BuildOnPlan(TenantPlan.S); // S lacks EInvoicing
        var filter = FilterFor(service);

        var enabled = await filter.EvaluateAsync(GateContext(nameof(Capability.EInvoicing)));

        Assert.False(enabled);
    }

    [Fact]
    public async Task Filter_denies_when_no_tenant_in_scope()
    {
        var (service, tenant, _) = BuildOnPlan(TenantPlan.XL);
        tenant.Clear();
        var filter = FilterFor(service);

        var enabled = await filter.EvaluateAsync(GateContext(nameof(Capability.ApiAccess)));

        Assert.False(enabled);
    }

    [Fact]
    public async Task Filter_denies_when_capability_parameter_is_unknown()
    {
        var (service, _, _) = BuildOnPlan(TenantPlan.XL);
        var filter = FilterFor(service);

        var enabled = await filter.EvaluateAsync(GateContext("NotARealCapability"));

        Assert.False(enabled);
    }
}
