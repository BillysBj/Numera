using Microsoft.AspNetCore.Http;

namespace Numera.Api.Auth;

/// <summary>Pure request-access policy for a tenant in billing read-only mode.</summary>
public static class BillingReadOnlyAccessPolicy
{
    private static readonly string[] AlwaysAllowedPrefixes =
    [
        "/health",
        "/api/auth",
        "/api/me",
    ];

    private static readonly string[] BillingRecoveryPaths =
    [
        "/api/billing/checkout",
        "/api/billing/portal",
        "/api/billing/status",
        "/api/billing/webhook",
    ];

    /// <summary>Returns whether a degraded tenant may access the method and path.</summary>
    public static bool IsAllowed(string method, PathString path)
    {
        // All data-reading surfaces remain visible in D4, including documents,
        // open items, reports, ledger, banking, receipts, partners, and catalog.
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method))
        {
            return true;
        }

        // Health, authentication, current-user state, and the exact billing recovery
        // endpoints remain reachable for every method. Checkout and Portal are crucial:
        // a degraded tenant must never be locked out of the action that restores access.
        if (AlwaysAllowedPrefixes.Any(prefix =>
                path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            || BillingRecoveryPaths.Any(recoveryPath =>
                path.StartsWithSegments(recoveryPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Every other write (POST/PUT/PATCH/DELETE) and unfamiliar method is blocked.
        return false;
    }
}
