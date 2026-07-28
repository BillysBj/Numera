using Microsoft.AspNetCore.Http;

using Numera.Platform.Db.Entities;

namespace Numera.Api.Auth;

/// <summary>Pure request-access decision policy for the read-only tax-advisor role.</summary>
public static class ReadOnlyAccessPolicy
{
    private static readonly string[] ReadPrefixes =
    [
        "/api/documents",
        "/api/open-items",
        "/api/inbound-documents",
        "/api/me",
        "/api/auth",
    ];

    /// <summary>Returns whether a role may access the supplied HTTP method and path.</summary>
    public static bool IsAllowed(string method, PathString path, MembershipRole? role)
    {
        if (role != MembershipRole.TaxAdvisor
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method))
        {
            return false;
        }

        if (!HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method)
            && !HttpMethods.IsOptions(method))
        {
            return false;
        }

        // Everything outside Belege/Auswertungen is denied, including partners,
        // catalog, company profile, dunning, recurring templates, team, payments
        // and features. Future report surfaces must be deliberately added here.
        return ReadPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
