using Numera.Platform.Db.Entities;

namespace Numera.Api.Auth;

/// <summary>Globally enforces the tax advisor's read-only, read-allow-listed access.</summary>
public sealed class ReadOnlyWriteGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ReadOnlyWriteGuardMiddleware> _logger;

    /// <summary>Creates the global read-only guard.</summary>
    public ReadOnlyWriteGuardMiddleware(
        RequestDelegate next,
        ILogger<ReadOnlyWriteGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Allows the request onward or returns a read-only-role problem response.</summary>
    public async Task InvokeAsync(HttpContext context, ICurrentUserRole currentUserRole)
    {
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var role = await currentUserRole.GetAsync(context.RequestAborted).ConfigureAwait(false);
        if (role != MembershipRole.TaxAdvisor)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!ReadOnlyAccessPolicy.IsAllowed(context.Request.Method, context.Request.Path, role))
        {
            _logger.LogWarning(
                "Read-only tax advisor request denied: {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Zugriff verweigert",
                detail: "Steuerberater haben nur Lesezugriff",
                extensions: new Dictionary<string, object?> { ["error"] = "read_only_role" })
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
