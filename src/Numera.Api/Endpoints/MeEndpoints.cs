using System.Security.Claims;

using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;

using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// Authenticated "who am I" surface for the SPA: the current user + tenant + role,
/// the tenant's entitlements (for cosmetic show/hide), and a server-gated example
/// endpoint proving the plan filter is authoritative on the server.
/// </summary>
public static class MeEndpoints
{
    /// <summary>Maps <c>/api/me*</c> and the gated <c>/api/features/*</c> example.</summary>
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me — current user (from claims) + current tenant + role.
        app.MapGet("/api/me", async (
            ClaimsPrincipal principal,
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

            // RLS self-scopes tenants to the caller's own row.
            var tenant = await db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId.Value)
                .Select(t => new { t.Id, t.Name, Plan = t.Plan.ToString() })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            var role = await db.Memberships
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId.Value && m.UserId == Sub(principal))
                .Select(m => m.Role.ToString())
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                user = new
                {
                    sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"),
                    email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    name = principal.FindFirstValue("name") ?? principal.Identity?.Name,
                },
                tenant,
                role,
            });
        })
        .RequireAuthorization();

        // GET /api/me/entitlements — the current tenant's capability set (server-authoritative;
        // the SPA uses it for cosmetic visibility only).
        app.MapGet("/api/me/entitlements", async (
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var caps = await entitlements.CurrentCapabilitiesAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { capabilities = caps.Select(c => c.ToString()).OrderBy(s => s).ToArray() });
        })
        .RequireAuthorization();

        // GET /api/features/einvoicing-check — example server-side gate. Routes through the
        // FeatureManagement "EInvoicing" feature -> PlanFeatureFilter -> IEntitlementService,
        // so it is 200 only for a plan that grants EInvoicing (L/XL), else 403.
        app.MapGet("/api/features/einvoicing-check", async (
            IFeatureManager features,
            CancellationToken ct) =>
        {
            var enabled = await features.IsEnabledAsync("EInvoicing").ConfigureAwait(false);
            return enabled
                ? Results.Ok(new { capability = "EInvoicing", granted = true })
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        })
        .RequireAuthorization();

        return app;
    }

    private static Guid Sub(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
