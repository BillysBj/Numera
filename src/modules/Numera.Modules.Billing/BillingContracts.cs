namespace Numera.Modules.Billing;

/// <summary>Provider-neutral request for a hosted subscription Checkout session.</summary>
public sealed record CheckoutRequest(
    Guid TenantId,
    string TargetPriceId,
    string? ExistingCustomerId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl);

/// <summary>Hosted Checkout session returned by the billing provider.</summary>
public sealed record CheckoutSession(string Url, string? SessionId);

/// <summary>Hosted Customer Portal session returned by the billing provider.</summary>
public sealed record PortalSession(string Url);

/// <summary>Provider-neutral snapshot of the current subscription state.</summary>
public sealed record SubscriptionSnapshot(
    string SubscriptionId,
    string Status,
    string PriceId,
    DateTimeOffset? CurrentPeriodEnd,
    string? CustomerId,
    Guid? TenantId);
