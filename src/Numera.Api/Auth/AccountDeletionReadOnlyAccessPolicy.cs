using Microsoft.AspNetCore.Http;

namespace Numera.Api.Auth;

/// <summary>Pure request-access policy for a tenant in the deletion grace window (read-only).</summary>
public static class AccountDeletionReadOnlyAccessPolicy
{
    private static readonly string[] AlwaysAllowedPrefixes =
    [
        "/health",
        "/api/auth",
        "/api/me",
        // The deletion surface itself must stay reachable so the Owner can Undo (cancel)
        // and read the current status while the grace window is active.
        "/api/account/deletion",
        // The data export (DSGVO Art. 20) must remain available during the grace window.
        "/api/export",
    ];

    /// <summary>Returns whether a tenant pending deletion may access the method and path.</summary>
    public static bool IsAllowed(string method, PathString path)
    {
        // All data-reading surfaces remain visible during the grace window.
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method))
        {
            return true;
        }

        if (AlwaysAllowedPrefixes.Any(prefix =>
                path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Every other write (POST/PUT/PATCH/DELETE) is blocked until the deletion is cancelled.
        return false;
    }
}
