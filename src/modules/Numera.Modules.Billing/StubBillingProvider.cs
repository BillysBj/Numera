namespace Numera.Modules.Billing;

/// <summary>Zero-dependency default provider used when Stripe is not configured.</summary>
public sealed class StubBillingProvider : IBillingProvider
{
    private const string NotConfiguredMessage =
        "Billing provider is not configured (no Stripe keys).";

    /// <inheritdoc />
    public Task<CheckoutSession> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken ct) =>
        throw new NotSupportedException(NotConfiguredMessage);

    /// <inheritdoc />
    public Task<PortalSession> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct) =>
        throw new NotSupportedException(NotConfiguredMessage);

    /// <inheritdoc />
    public Task<SubscriptionSnapshot> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct) =>
        throw new NotSupportedException(NotConfiguredMessage);
}
