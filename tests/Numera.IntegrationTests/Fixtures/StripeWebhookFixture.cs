using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Numera.IntegrationTests.Fixtures;

/// <summary>Builds canonical Stripe webhook JSON and valid local signatures without network access.</summary>
public static class StripeWebhookFixture
{
    /// <summary>Signs the exact raw JSON body using Stripe's webhook signature format.</summary>
    public static string CreateSignatureHeader(string json, string webhookSecret)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{t}.{json}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var v1 = Convert.ToHexString(
                h.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)))
            .ToLowerInvariant();
        return $"t={t},v1={v1}";
    }

    /// <summary>Builds a <c>checkout.session.completed</c> event.</summary>
    public static string CheckoutSessionCompleted(
        Guid tenantId,
        string priceId,
        string customerId,
        string subscriptionId,
        string eventId) =>
        SerializeEvent(
            eventId,
            "checkout.session.completed",
            new
            {
                id = "cs_test_fixture",
                @object = "checkout.session",
                client_reference_id = tenantId.ToString(),
                customer = customerId,
                subscription = subscriptionId,
                line_items = new
                {
                    @object = "list",
                    data = new[]
                    {
                        new
                        {
                            @object = "item",
                            price = new { id = priceId, @object = "price" },
                        },
                    },
                },
            });

    /// <summary>Builds a <c>customer.subscription.updated</c> event with past_due status.</summary>
    public static string SubscriptionPastDue(
        Guid tenantId,
        string priceId,
        string customerId,
        string subscriptionId,
        string eventId) =>
        SubscriptionEvent(
            "customer.subscription.updated",
            "past_due",
            tenantId,
            priceId,
            customerId,
            subscriptionId,
            eventId);

    /// <summary>Builds a <c>customer.subscription.deleted</c> event.</summary>
    public static string SubscriptionDeleted(
        Guid tenantId,
        string priceId,
        string customerId,
        string subscriptionId,
        string eventId) =>
        SubscriptionEvent(
            "customer.subscription.deleted",
            "canceled",
            tenantId,
            priceId,
            customerId,
            subscriptionId,
            eventId);

    /// <summary>Builds a <c>customer.subscription.updated</c> event with active status.</summary>
    public static string SubscriptionActive(
        Guid tenantId,
        string priceId,
        string customerId,
        string subscriptionId,
        string eventId) =>
        SubscriptionEvent(
            "customer.subscription.updated",
            "active",
            tenantId,
            priceId,
            customerId,
            subscriptionId,
            eventId);

    private static string SubscriptionEvent(
        string eventType,
        string status,
        Guid tenantId,
        string priceId,
        string customerId,
        string subscriptionId,
        string eventId) =>
        SerializeEvent(
            eventId,
            eventType,
            new
            {
                id = subscriptionId,
                @object = "subscription",
                customer = customerId,
                status,
                current_period_end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                metadata = new Dictionary<string, string>
                {
                    ["tenant_id"] = tenantId.ToString(),
                },
                items = new
                {
                    @object = "list",
                    data = new[]
                    {
                        new
                        {
                            @object = "subscription_item",
                            price = new { id = priceId, @object = "price" },
                        },
                    },
                },
            });

    private static string SerializeEvent(string eventId, string eventType, object dataObject) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            type = eventType,
            data = new { @object = dataObject },
        });
}
