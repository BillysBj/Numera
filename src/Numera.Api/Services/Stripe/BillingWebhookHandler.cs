using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Npgsql;

using Numera.Modules.Billing;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services.Stripe;

/// <summary>Handles a raw, signed Stripe webhook delivery.</summary>
public interface IBillingWebhookHandler
{
    /// <summary>Verifies, deduplicates, resolves, and applies one Stripe event.</summary>
    Task<IResult> HandleAsync(string json, string signatureHeader, CancellationToken ct);
}

/// <summary>
/// Stripe webhook source-of-truth pipeline with separate tenant-agnostic dedupe and
/// tenant-scoped mutation contexts.
/// </summary>
public sealed class BillingWebhookHandler : IBillingWebhookHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBillingProvider _billingProvider;
    private readonly StripeOptions _options;
    private readonly SubscriptionPlanMapper _planMapper;
    private readonly ILogger<BillingWebhookHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public BillingWebhookHandler(
        IServiceScopeFactory scopeFactory,
        IBillingProvider billingProvider,
        IOptions<StripeOptions> options,
        ILogger<BillingWebhookHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _billingProvider = billingProvider;
        _options = options.Value;
        _planMapper = new SubscriptionPlanMapper(_options);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IResult> HandleAsync(
        string json,
        string signatureHeader,
        CancellationToken ct)
    {
        global::Stripe.Event stripeEvent;
        try
        {
            stripeEvent = global::Stripe.EventUtility.ConstructEvent(
                json,
                signatureHeader,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (global::Stripe.StripeException exception)
        {
            _logger.LogWarning(exception, "Rejected Stripe webhook with an invalid signature.");
            return Results.BadRequest();
        }

        // This dedicated scope deliberately has no tenant. Its context/connection is
        // used only for the global, non-RLS processed_stripe_event reservation.
        using (var dedupeScope = _scopeFactory.CreateScope())
        {
            var dedupeDb = dedupeScope.ServiceProvider.GetRequiredService<NumeraDbContext>();
            dedupeDb.ProcessedStripeEvents.Add(new ProcessedStripeEvent
            {
                EventId = stripeEvent.Id,
                EventType = stripeEvent.Type,
            });

            try
            {
                await dedupeDb.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException
                      { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _logger.LogInformation(
                    "Ignoring duplicate Stripe webhook event {StripeEventId} ({StripeEventType}).",
                    stripeEvent.Id,
                    stripeEvent.Type);
                return Results.Ok();
            }
        }

        var resolved = await ResolveAsync(stripeEvent, ct).ConfigureAwait(false);
        if (resolved?.TenantId is not { } tenantId)
        {
            _logger.LogWarning(
                "Acknowledging Stripe webhook event {StripeEventId} ({StripeEventType}) without a resolvable tenant.",
                stripeEvent.Id,
                stripeEvent.Type);
            return Results.Ok();
        }

        // This is a fresh scope and therefore a fresh TenantContext + DbContext.
        // SetTenant MUST precede the first resolution of NumeraDbContext so its
        // connection interceptor sets app.current_tenant when the connection opens.
        using var tenantScope = _scopeFactory.CreateScope();
        tenantScope.ServiceProvider
            .GetRequiredService<ICurrentTenant>()
            .SetTenant(tenantId);
        var tenantDb = tenantScope.ServiceProvider.GetRequiredService<NumeraDbContext>();

        var tenant = await tenantDb.Tenants
            .SingleOrDefaultAsync(candidate => candidate.Id == tenantId, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Acknowledging Stripe webhook event {StripeEventId}: tenant {TenantId} was not visible under RLS.",
                stripeEvent.Id,
                tenantId);
            return Results.Ok();
        }

        if (resolved.IsPotentiallyStale
            && resolved.CurrentPeriodEnd is { } deliveredPeriodEnd
            && tenant.CurrentPeriodEnd is { } storedPeriodEnd
            && deliveredPeriodEnd < storedPeriodEnd)
        {
            _logger.LogInformation(
                "Ignoring stale Stripe webhook event {StripeEventId} for tenant {TenantId}: delivered period {DeliveredPeriodEnd} precedes stored period {StoredPeriodEnd}.",
                stripeEvent.Id,
                tenantId,
                deliveredPeriodEnd,
                storedPeriodEnd);
            return Results.Ok();
        }

        var oldPlan = tenant.Plan;
        var mappedPlan = resolved.Kind switch
        {
            ResolvedEventKind.SubscriptionDeleted =>
                SubscriptionPlanMapper.ForSubscriptionDeleted(),
            ResolvedEventKind.InvoicePaid =>
                _planMapper.ForInvoicePaid(resolved.PriceId),
            _ => _planMapper.ForSubscriptionStatus(
                resolved.Status,
                resolved.PriceId,
                tenant.Plan),
        };

        if (mappedPlan is { } plan)
        {
            tenant.Plan = plan;
        }

        if (!string.IsNullOrWhiteSpace(resolved.CustomerId))
        {
            tenant.StripeCustomerId = resolved.CustomerId;
        }

        if (!string.IsNullOrWhiteSpace(resolved.SubscriptionId))
        {
            tenant.StripeSubscriptionId = resolved.SubscriptionId;
        }

        if (!string.IsNullOrWhiteSpace(resolved.Status))
        {
            tenant.SubscriptionStatus = resolved.Status;
        }

        if (resolved.CurrentPeriodEnd is { } currentPeriodEnd)
        {
            tenant.CurrentPeriodEnd = currentPeriodEnd;
        }

        await tenantDb.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Applied Stripe webhook event {StripeEventId} ({StripeEventType}) for tenant {TenantId}: plan {OldPlan} -> {NewPlan}, subscription status {SubscriptionStatus}.",
            stripeEvent.Id,
            stripeEvent.Type,
            tenantId,
            oldPlan,
            tenant.Plan,
            tenant.SubscriptionStatus);
        return Results.Ok();
    }

    private async Task<ResolvedStripeEvent?> ResolveAsync(
        global::Stripe.Event stripeEvent,
        CancellationToken ct)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                return await ResolveCheckoutAsync(
                        stripeEvent.Data.Object as global::Stripe.Checkout.Session,
                        ct)
                    .ConfigureAwait(false);

            case "customer.subscription.deleted":
                return ResolveSubscription(
                    stripeEvent.Data.Object as global::Stripe.Subscription,
                    ResolvedEventKind.SubscriptionDeleted,
                    isPotentiallyStale: false);

            case "customer.subscription.created":
            case "customer.subscription.updated":
                return ResolveSubscription(
                    stripeEvent.Data.Object as global::Stripe.Subscription,
                    ResolvedEventKind.SubscriptionState,
                    isPotentiallyStale: stripeEvent.Type == "customer.subscription.updated");

            case "invoice.paid":
                return await ResolveInvoiceAsync(
                        stripeEvent.Data.Object as global::Stripe.Invoice,
                        ResolvedEventKind.InvoicePaid,
                        ct)
                    .ConfigureAwait(false);

            case "invoice.payment_failed":
                return await ResolveInvoiceAsync(
                        stripeEvent.Data.Object as global::Stripe.Invoice,
                        ResolvedEventKind.SubscriptionState,
                        ct)
                    .ConfigureAwait(false);

            default:
                return null;
        }
    }

    private async Task<ResolvedStripeEvent?> ResolveCheckoutAsync(
        global::Stripe.Checkout.Session? session,
        CancellationToken ct)
    {
        if (session is null)
        {
            return null;
        }

        SubscriptionSnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            snapshot = await _billingProvider
                .GetSubscriptionAsync(session.SubscriptionId, ct)
                .ConfigureAwait(false);
        }

        var tenantId = ParseTenantId(session.ClientReferenceId)
            ?? ParseTenantId(session.Metadata)
            ?? ParseTenantId(session.Subscription?.Metadata)
            ?? ParseTenantId(session.Customer?.Metadata)
            ?? snapshot?.TenantId;
        var deliveredPriceId = session.LineItems?.Data.FirstOrDefault()?.Price?.Id;

        return new ResolvedStripeEvent(
            tenantId,
            ResolvedEventKind.SubscriptionState,
            session.CustomerId ?? snapshot?.CustomerId,
            session.SubscriptionId ?? snapshot?.SubscriptionId,
            snapshot?.Status ?? "active",
            snapshot?.PriceId ?? deliveredPriceId,
            snapshot?.CurrentPeriodEnd,
            IsPotentiallyStale: false);
    }

    private static ResolvedStripeEvent? ResolveSubscription(
        global::Stripe.Subscription? subscription,
        ResolvedEventKind kind,
        bool isPotentiallyStale)
    {
        if (subscription is null)
        {
            return null;
        }

        var firstItem = subscription.Items.Data.FirstOrDefault();
        return new ResolvedStripeEvent(
            ParseTenantId(subscription.Metadata),
            kind,
            subscription.CustomerId,
            subscription.Id,
            subscription.Status,
            firstItem?.Price?.Id,
            firstItem?.CurrentPeriodEnd,
            isPotentiallyStale);
    }

    private async Task<ResolvedStripeEvent?> ResolveInvoiceAsync(
        global::Stripe.Invoice? invoice,
        ResolvedEventKind kind,
        CancellationToken ct)
    {
        if (invoice is null)
        {
            return null;
        }

        var subscriptionDetails = invoice.Parent?.SubscriptionDetails;
        var subscriptionId = subscriptionDetails?.SubscriptionId;
        SubscriptionSnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            snapshot = await _billingProvider
                .GetSubscriptionAsync(subscriptionId, ct)
                .ConfigureAwait(false);
        }

        var line = invoice.Lines?.Data.FirstOrDefault();
        var tenantId = ParseTenantId(subscriptionDetails?.Metadata)
            ?? ParseTenantId(invoice.Metadata)
            ?? ParseTenantId(line?.Metadata)
            ?? snapshot?.TenantId;
        var priceId = snapshot?.PriceId ?? line?.Pricing?.PriceDetails?.PriceId;
        var fallbackStatus = kind == ResolvedEventKind.InvoicePaid ? "active" : "past_due";

        return new ResolvedStripeEvent(
            tenantId,
            kind,
            invoice.CustomerId ?? snapshot?.CustomerId,
            subscriptionId ?? snapshot?.SubscriptionId,
            snapshot?.Status ?? fallbackStatus,
            priceId,
            snapshot?.CurrentPeriodEnd,
            IsPotentiallyStale: false);
    }

    private static Guid? ParseTenantId(string? value) =>
        Guid.TryParse(value, out var tenantId) ? tenantId : null;

    private static Guid? ParseTenantId(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is not null
        && metadata.TryGetValue("tenant_id", out var tenantId)
            ? ParseTenantId(tenantId)
            : null;

    private sealed record ResolvedStripeEvent(
        Guid? TenantId,
        ResolvedEventKind Kind,
        string? CustomerId,
        string? SubscriptionId,
        string? Status,
        string? PriceId,
        DateTimeOffset? CurrentPeriodEnd,
        bool IsPotentiallyStale);

    private enum ResolvedEventKind
    {
        SubscriptionState,
        SubscriptionDeleted,
        InvoicePaid,
    }
}
