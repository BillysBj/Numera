using Numera.Platform.Db.Entities;
using Numera.Platform.Entitlements;

using Xunit;

namespace Numera.Platform.Tests.Entitlements;

public sealed class PlanCapabilityMapTests
{
    private static readonly Capability[] AdvancedInvoiceCapabilities =
    [
        Capability.ForeignCurrencyInvoicing,
        Capability.RecurringInvoices,
        Capability.DownPaymentInvoices,
    ];

    [Fact]
    public void Advanced_invoice_capabilities_are_granted_only_to_l_and_xl()
    {
        Assert.All(AdvancedInvoiceCapabilities, capability =>
        {
            Assert.DoesNotContain(capability, PlanCapabilityMap.For(TenantPlan.S));
            Assert.DoesNotContain(capability, PlanCapabilityMap.For(TenantPlan.M));
            Assert.Contains(capability, PlanCapabilityMap.For(TenantPlan.L));
            Assert.Contains(capability, PlanCapabilityMap.For(TenantPlan.XL));
        });
    }

    [Fact]
    public void Capability_sets_are_strictly_increasing_by_tier()
    {
        var s = PlanCapabilityMap.For(TenantPlan.S);
        var m = PlanCapabilityMap.For(TenantPlan.M);
        var l = PlanCapabilityMap.For(TenantPlan.L);
        var xl = PlanCapabilityMap.For(TenantPlan.XL);

        Assert.True(s.IsProperSubsetOf(m));
        Assert.True(m.IsProperSubsetOf(l));
        Assert.True(l.IsProperSubsetOf(xl));
    }
}
