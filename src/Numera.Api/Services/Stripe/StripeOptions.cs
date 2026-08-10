using Numera.Platform.Db.Entities;

namespace Numera.Api.Services.Stripe;

/// <summary>Configuration for the opt-in Stripe TEST-mode billing adapter.</summary>
public sealed class StripeOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Stripe";

    /// <summary>Stripe secret API key.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Stripe webhook signing secret.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Stripe recurring price ids keyed by paid plan name (S/M/L/XL).</summary>
    public Dictionary<string, string> PriceIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the configured recurring Stripe price id for a paid plan.</summary>
    public string? PriceIdFor(TenantPlan plan)
    {
        if (plan == TenantPlan.Free
            || !PriceIds.TryGetValue(plan.ToString(), out var priceId)
            || string.IsNullOrWhiteSpace(priceId))
        {
            return null;
        }

        return priceId;
    }

    /// <summary>Returns the paid plan mapped to a recurring Stripe price id.</summary>
    public TenantPlan? PlanForPriceId(string priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            return null;
        }

        foreach (var pair in PriceIds)
        {
            if (!string.Equals(pair.Value, priceId, StringComparison.Ordinal)
                || !Enum.TryParse<TenantPlan>(pair.Key, ignoreCase: true, out var plan)
                || plan == TenantPlan.Free)
            {
                continue;
            }

            return plan;
        }

        return null;
    }
}
