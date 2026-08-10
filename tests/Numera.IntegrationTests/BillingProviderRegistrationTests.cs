using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Services;
using Numera.Api.Services.Stripe;
using Numera.Modules.Billing;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>DI contract for Stripe opt-in and the safe, no-network billing default.</summary>
public sealed class BillingProviderRegistrationTests
{
    [Fact]
    public void Missing_stripe_configuration_resolves_stub_provider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddBillingProvider(configuration);

        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = serviceProvider.CreateScope();

        Assert.IsType<StubBillingProvider>(
            scope.ServiceProvider.GetRequiredService<IBillingProvider>());
        Assert.Null(serviceProvider.GetService<global::Stripe.IStripeClient>());
    }

    [Fact]
    public void Complete_stripe_configuration_resolves_real_provider_and_singleton_client()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_dummy",
                ["Stripe:WebhookSecret"] = "whsec_dummy",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBillingProvider(configuration);

        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = serviceProvider.CreateScope();

        Assert.IsType<StripeBillingProvider>(
            scope.ServiceProvider.GetRequiredService<IBillingProvider>());
        var firstClient = serviceProvider.GetRequiredService<global::Stripe.IStripeClient>();
        var secondClient = serviceProvider.GetRequiredService<global::Stripe.IStripeClient>();
        Assert.IsType<global::Stripe.StripeClient>(firstClient);
        Assert.Same(firstClient, secondClient);
    }
}
