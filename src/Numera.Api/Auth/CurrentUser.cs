using System.Security.Claims;

using Numera.Platform.Tenancy;

namespace Numera.Api.Auth;

/// <summary>
/// Api-host implementation of the <see cref="ICurrentUser"/> seam (defined in
/// <c>Numera.Platform.Tenancy</c>, plan 01-04). Reads the authenticated user's
/// Keycloak <c>sub</c> claim from the current <see cref="HttpContext"/> and exposes
/// it as <see cref="UserId"/> — the actor id the <c>AuditWriter</c> stamps on events.
/// </summary>
/// <remarks>
/// Returns <c>null</c> when there is no authenticated user (health checks,
/// pre-authentication requests, background contexts without a request), rather than
/// fabricating an actor. Registered scoped so it tracks the per-request principal.
/// </remarks>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor over the ambient <see cref="HttpContext"/>.</summary>
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity is not { IsAuthenticated: true })
            {
                return null;
            }

            var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");

            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
    }
}
