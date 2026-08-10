using Numera.Platform.Db.Entities;

namespace Numera.Api.Services.Stripe;

/// <summary>Pure Stripe subscription-state to tenant-plan mapping.</summary>
public sealed class SubscriptionPlanMapper(StripeOptions options)
{
    /// <summary>Maps a subscription status while preserving paid access during Smart Retries.</summary>
    public TenantPlan? ForSubscriptionStatus(
        string? status,
        string? priceId,
        TenantPlan currentPlan) =>
        status switch
        {
            "trialing" or "active" when priceId is not null => options.PlanForPriceId(priceId),
            "past_due" => currentPlan,
            "canceled" or "unpaid" => TenantPlan.Free,
            _ => null,
        };

    /// <summary>A deleted subscription is terminal and always degrades to Free.</summary>
    public static TenantPlan ForSubscriptionDeleted() => TenantPlan.Free;

    /// <summary>A paid invoice re-activates the paid plan represented by its Stripe price.</summary>
    public TenantPlan? ForInvoicePaid(string? priceId) =>
        priceId is null ? null : options.PlanForPriceId(priceId);
}
