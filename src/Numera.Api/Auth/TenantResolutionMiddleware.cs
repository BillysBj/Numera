using System.Text.Json;

using Numera.Platform.Tenancy;

namespace Numera.Api.Auth;

/// <summary>
/// Per-request middleware that resolves the current tenant from the authenticated
/// principal's <c>organization</c> claim (carried by the Keycloak <c>organization</c>
/// scope) and pushes it onto <see cref="ICurrentTenant"/>. The
/// <c>TenantConnectionInterceptor</c> then sets <c>app.current_tenant</c> when the
/// request's <c>NumeraDbContext</c> opens its connection, so RLS filters every query
/// by that tenant.
/// </summary>
/// <remarks>
/// <para>
/// Runs <b>after</b> authentication. Unauthenticated requests (health, registration,
/// the OIDC challenge/callback) carry no organization claim and are passed through
/// untouched — they simply have no tenant in scope, and tenant-scoped reads return
/// nothing under RLS.
/// </para>
/// <para>
/// The organization id is the tenant id (the app mirrors the Keycloak organization id
/// as <c>tenants.id</c>). Keycloak's organization membership mapper can surface the
/// claim in several shapes across versions (a bare id/alias, an array, or an object
/// keyed by alias with an <c>id</c>), so resolution scans the claim value(s) for the
/// first parseable GUID.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware
{
    private const string OrganizationClaim = "organization";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Resolves and sets the tenant, then invokes the next middleware.</summary>
    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        var user = context.User;
        if (user.Identity is { IsAuthenticated: true })
        {
            var tenantId = ResolveTenantId(user.FindAll(OrganizationClaim).Select(c => c.Value));
            if (tenantId is { } id)
            {
                currentTenant.SetTenant(id);
            }
            else
            {
                _logger.LogWarning(
                    "Authenticated request had no resolvable organization claim; no tenant set for {Path}.",
                    context.Request.Path);
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static Guid? ResolveTenantId(IEnumerable<string> claimValues)
    {
        foreach (var value in claimValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Guid.TryParse(value, out var direct))
            {
                return direct;
            }

            if (TryExtractIdFromJson(value, out var fromJson))
            {
                return fromJson;
            }
        }

        return null;
    }

    private static bool TryExtractIdFromJson(string value, out Guid id)
    {
        id = default;
        var trimmed = value.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            return TryFindGuid(doc.RootElement, out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindGuid(JsonElement element, out Guid id)
    {
        id = default;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return Guid.TryParse(element.GetString(), out id);

            case JsonValueKind.Object:
                // Prefer an explicit "id" property, else scan nested values.
                if (element.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idProp.GetString(), out id))
                {
                    return true;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindGuid(property.Value, out id))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindGuid(item, out id))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
