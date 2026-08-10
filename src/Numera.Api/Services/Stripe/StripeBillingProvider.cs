using Numera.Modules.Billing;

namespace Numera.Api.Services.Stripe;

/// <summary>Stripe.net adapter for hosted Checkout, Customer Portal, and subscription reads.</summary>
public sealed class StripeBillingProvider : IBillingProvider
{
    private readonly global::Stripe.IStripeClient _client;

    /// <summary>Creates the adapter over an injected, instance-scoped Stripe client.</summary>
    public StripeBillingProvider(global::Stripe.IStripeClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<CheckoutSession> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken ct)
    {
        var options = new global::Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            ClientReferenceId = request.TenantId.ToString(),
            Customer = request.ExistingCustomerId,
            CustomerEmail = request.ExistingCustomerId is null ? request.CustomerEmail : null,
            LineItems =
            [
                new global::Stripe.Checkout.SessionLineItemOptions
                {
                    Price = request.TargetPriceId,
                    Quantity = 1,
                },
            ],
            SubscriptionData = new global::Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata =
                {
                    ["tenant_id"] = request.TenantId.ToString(),
                },
            },
            AutomaticTax = new global::Stripe.Checkout.SessionAutomaticTaxOptions
            {
                Enabled = true,
            },
            TaxIdCollection = new global::Stripe.Checkout.SessionTaxIdCollectionOptions
            {
                Enabled = true,
            },
            CustomerUpdate = new global::Stripe.Checkout.SessionCustomerUpdateOptions
            {
                Address = "auto",
                Name = "auto",
            },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
        };

        var service = new global::Stripe.Checkout.SessionService(_client);
        var session = await service.CreateAsync(options, cancellationToken: ct)
            .ConfigureAwait(false);
        return new CheckoutSession(session.Url, session.Id);
    }

    /// <inheritdoc />
    public async Task<PortalSession> CreatePortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken ct)
    {
        var service = new global::Stripe.BillingPortal.SessionService(_client);
        var portal = await service.CreateAsync(
                new global::Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = stripeCustomerId,
                    ReturnUrl = returnUrl,
                },
                cancellationToken: ct)
            .ConfigureAwait(false);
        return new PortalSession(portal.Url);
    }

    /// <inheritdoc />
    public async Task<SubscriptionSnapshot> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct)
    {
        var service = new global::Stripe.SubscriptionService(_client);
        var subscription = await service.GetAsync(subscriptionId, cancellationToken: ct)
            .ConfigureAwait(false);
        var firstItem = subscription.Items.Data.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Stripe subscription '{subscriptionId}' has no subscription items.");

        Guid? tenantId = null;
        if (subscription.Metadata.TryGetValue("tenant_id", out var tenantIdValue)
            && Guid.TryParse(tenantIdValue, out var parsedTenantId))
        {
            tenantId = parsedTenantId;
        }

        return new SubscriptionSnapshot(
            subscription.Id,
            subscription.Status,
            firstItem.Price.Id,
            firstItem.CurrentPeriodEnd,
            subscription.CustomerId,
            tenantId);
    }
}
