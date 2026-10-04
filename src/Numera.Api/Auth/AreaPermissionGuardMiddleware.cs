using Numera.Platform.Db.Entities;

namespace Numera.Api.Auth;

/// <summary>Enforces employee areas for all HTTP methods, including nested resource routes.</summary>
public sealed class AreaPermissionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, ICurrentUserRole currentUserRole, ICurrentUserPermissions permissions)
    {
        if (context.User.Identity is { IsAuthenticated: true }
            && AreaPermissions.Resolve(context.Request.Path) is { } area
            && await currentUserRole.GetAsync(context.RequestAborted).ConfigureAwait(false) == MembershipRole.Employee)
        {
            var allowed = await permissions.GetAllowedAreasAsync(context.RequestAborted).ConfigureAwait(false);
            if (allowed is not null && !allowed.Contains(area, StringComparer.Ordinal))
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Zugriff verweigert",
                    detail: "Für diesen Bereich fehlt die Berechtigung.",
                    extensions: new Dictionary<string, object?> { ["error"] = "area_forbidden" })
                    .ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
