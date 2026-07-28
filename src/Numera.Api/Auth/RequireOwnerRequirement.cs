using Microsoft.AspNetCore.Authorization;

using Numera.Platform.Db.Entities;

namespace Numera.Api.Auth;

/// <summary>Requires the current tenant membership to have the Owner role.</summary>
public sealed class RequireOwnerRequirement : IAuthorizationRequirement;

/// <summary>Checks the server-authoritative membership role for an owner-only operation.</summary>
public sealed class RequireOwnerHandler : AuthorizationHandler<RequireOwnerRequirement>
{
    private readonly ICurrentUserRole _currentUserRole;

    /// <summary>Creates the owner authorization handler.</summary>
    public RequireOwnerHandler(ICurrentUserRole currentUserRole)
    {
        _currentUserRole = currentUserRole;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireOwnerRequirement requirement)
    {
        if (await _currentUserRole.GetAsync().ConfigureAwait(false) == MembershipRole.Owner)
        {
            context.Succeed(requirement);
        }
    }
}
