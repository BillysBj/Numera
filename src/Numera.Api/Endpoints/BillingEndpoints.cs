using System.Security.Claims;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Numera.Api.Services.Stripe;
using Numera.Modules.Billing;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated hosted Checkout, Customer Portal, and billing-status endpoints.</summary>
public static class BillingEndpoints
{
    /// <summary>Maps the authenticated billing self-service surface.</summary>
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/billing/checkout", async (
            BillingCheckoutRequest request,
            ClaimsPrincipal principal,
            HttpContext httpContext,
            ICurrentTenant currentTenant,
            NumeraDbContext db,
            IOptions<StripeOptions> stripeOptions,
            IBillingProvider billingProvider,
            CancellationToken ct) =>
        {
            var tenantId = currentTenant.TenantId;
            if (tenantId is null)
            {
                return Results.Problem(
                    detail: "No tenant is in scope for the authenticated user.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (request.Plan is not ("S" or "M" or "L" or "XL")
                || !Enum.TryParse<TenantPlan>(request.Plan, ignoreCase: false, out var targetPlan))
            {
                return Results.BadRequest(new { error = "Invalid paid plan." });
            }

            var priceId = stripeOptions.Value.PriceIdFor(targetPlan);
            if (priceId is null)
            {
                return Results.BadRequest(new { error = "No Stripe price is configured for this plan." });
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tenantId.Value, ct)
                .ConfigureAwait(false);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            var appBaseUrl = RequestBaseUrl(httpContext.Request);
            var checkout = await billingProvider.CreateCheckoutSessionAsync(
                    new CheckoutRequest(
                        tenantId.Value,
                        priceId,
                        tenant.StripeCustomerId,
                        principal.FindFirstValue(ClaimTypes.Email)
                            ?? principal.FindFirstValue("email"),
                        $"{appBaseUrl}/billing/success?session_id={{CHECKOUT_SESSION_ID}}",
                        $"{appBaseUrl}/billing/cancel"),
                    ct)
                .ConfigureAwait(false);
            return Results.Ok(new { url = checkout.Url });
        })
        .RequireAuthorization();

        app.MapPost("/api/billing/portal", async (
            HttpContext httpContext,
            ICurrentTenant currentTenant,
            NumeraDbContext db,
            IBillingProvider billingProvider,
            CancellationToken ct) =>
        {
            var tenantId = currentTenant.TenantId;
            if (tenantId is null)
            {
                return Results.Problem(
                    detail: "No tenant is in scope for the authenticated user.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var stripeCustomerId = await db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId.Value)
                .Select(t => t.StripeCustomerId)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (stripeCustomerId is null)
            {
                return Results.Conflict(new { error = "The tenant has no Stripe customer." });
            }

            var portal = await billingProvider.CreatePortalSessionAsync(
                    stripeCustomerId,
                    $"{RequestBaseUrl(httpContext.Request)}/billing",
                    ct)
                .ConfigureAwait(false);
            return Results.Ok(new { url = portal.Url });
        })
        .RequireAuthorization();

        app.MapGet("/api/billing/status", async (
            ICurrentTenant currentTenant,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var tenantId = currentTenant.TenantId;
            if (tenantId is null)
            {
                return Results.Problem(
                    detail: "No tenant is in scope for the authenticated user.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tenantId.Value, ct)
                .ConfigureAwait(false);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            var now = DateTimeOffset.UtcNow;
            var trialActive = tenant.TrialEndsAt > now;
            var subscriptionAllowsAccess = tenant.SubscriptionStatus is
                "trialing" or "active" or "past_due";

            return Results.Ok(new
            {
                plan = tenant.Plan.ToString(),
                tenant.TrialEndsAt,
                tenant.SubscriptionStatus,
                tenant.CurrentPeriodEnd,
                hasSubscription = tenant.StripeSubscriptionId is not null,
                trialActive,
                degraded = !(subscriptionAllowsAccess || trialActive),
            });
        })
        .RequireAuthorization();

        return app;
    }

    private static string RequestBaseUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
}

/// <summary>Requested paid plan for a hosted Checkout session.</summary>
/// <param name="Plan">One of S, M, L, or XL.</param>
public sealed record BillingCheckoutRequest(string Plan);
