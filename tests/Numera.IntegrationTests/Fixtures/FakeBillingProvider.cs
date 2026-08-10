using Numera.Modules.Billing;

namespace Numera.IntegrationTests.Fixtures;

/// <summary>Scriptable in-process billing provider so integration tests never contact Stripe.</summary>
public sealed class FakeBillingProvider : IBillingProvider
{
    /// <summary>Checkout session returned by the next Checkout call.</summary>
    public CheckoutSession NextCheckout { get; set; } =
        new("https://checkout.stripe.invalid/fake", "cs_test_fake");

    /// <summary>Portal session returned by the next Portal call.</summary>
    public PortalSession NextPortal { get; set; } =
        new("https://billing.stripe.invalid/fake");

    /// <summary>Subscription returned by the next subscription lookup.</summary>
    public SubscriptionSnapshot? NextSubscription { get; set; }

    /// <summary>The most recent Checkout request, for assertions.</summary>
    public CheckoutRequest? LastCheckoutRequest { get; private set; }

    /// <summary>The most recent Portal customer id, for assertions.</summary>
    public string? LastPortalCustomerId { get; private set; }

    /// <summary>The most recent Portal return URL, for assertions.</summary>
    public string? LastPortalReturnUrl { get; private set; }

    /// <summary>The most recent subscription id requested, for assertions.</summary>
    public string? LastSubscriptionId { get; private set; }

    /// <inheritdoc />
    public Task<CheckoutSession> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastCheckoutRequest = request;
        return Task.FromResult(NextCheckout);
    }

    /// <inheritdoc />
    public Task<PortalSession> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastPortalCustomerId = stripeCustomerId;
        LastPortalReturnUrl = returnUrl;
        return Task.FromResult(NextPortal);
    }

    /// <inheritdoc />
    public Task<SubscriptionSnapshot> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastSubscriptionId = subscriptionId;
        return Task.FromResult(
            NextSubscription
            ?? throw new InvalidOperationException("No subscription response has been scripted."));
    }
}
