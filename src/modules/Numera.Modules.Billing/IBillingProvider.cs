namespace Numera.Modules.Billing;

/// <summary>Provider-neutral port for subscription Checkout, Portal, and status access.</summary>
public interface IBillingProvider
{
    /// <summary>Creates a hosted subscription Checkout session.</summary>
    Task<CheckoutSession> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken ct);

    /// <summary>Creates a hosted Customer Portal session.</summary>
    Task<PortalSession> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct);

    /// <summary>Gets the provider's current subscription state.</summary>
    Task<SubscriptionSnapshot> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct);
}
