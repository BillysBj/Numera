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

    /// <summary>
    /// Cancels the subscription immediately at the provider. Used by the irreversible
    /// tenant purge (Phase B1 Scheibe 2) so a deleted tenant stops being billed; it is
    /// called only at hard-purge time, never during the reversible grace window.
    /// </summary>
    Task CancelSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct);
}
