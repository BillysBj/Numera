using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Services.Stripe;
using Numera.Modules.Billing;

namespace Numera.Api.Services;

/// <summary>Central registration seam for the provider-neutral Billing module.</summary>
public static class BillingModuleServiceCollectionExtensions
{
    /// <summary>
    /// Adds the billing port, selecting Stripe only when both API and webhook
    /// secrets are configured; otherwise the no-network stub remains active.
    /// </summary>
    public static IServiceCollection AddBillingProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(StripeOptions.SectionName);
        services.Configure<StripeOptions>(section);

        var secretKey = section[nameof(StripeOptions.SecretKey)];
        var webhookSecret = section[nameof(StripeOptions.WebhookSecret)];
        if (!string.IsNullOrWhiteSpace(secretKey)
            && !string.IsNullOrWhiteSpace(webhookSecret))
        {
            services.AddSingleton<global::Stripe.IStripeClient>(
                _ => new global::Stripe.StripeClient(secretKey));
            services.AddScoped<IBillingProvider, StripeBillingProvider>();
        }
        else
        {
            services.AddScoped<IBillingProvider, StubBillingProvider>();
        }

        return services;
    }
}
