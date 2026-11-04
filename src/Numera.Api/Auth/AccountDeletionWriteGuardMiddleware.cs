using Numera.Api.Lifecycle;

namespace Numera.Api.Auth;

/// <summary>
/// Forces a tenant that is in the deletion grace window into read-only access: data stays
/// visible and exportable and the deletion can be cancelled, but no other writes are accepted.
/// Mirrors <see cref="BillingDegradationWriteGuardMiddleware"/>.
/// </summary>
public sealed class AccountDeletionWriteGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AccountDeletionWriteGuardMiddleware> _logger;

    /// <summary>Creates the account-deletion read-only guard.</summary>
    public AccountDeletionWriteGuardMiddleware(
        RequestDelegate next,
        ILogger<AccountDeletionWriteGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Allows the request onward or returns the deletion read-only problem response.</summary>
    public async Task InvokeAsync(HttpContext context, IAccountDeletionState deletionState)
    {
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!await deletionState.IsPendingDeletionAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!AccountDeletionReadOnlyAccessPolicy.IsAllowed(context.Request.Method, context.Request.Path))
        {
            _logger.LogWarning(
                "Account-deletion read-only request denied: {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Nur-Lese-Modus: Kontolöschung beantragt",
                detail: "Für dieses Konto wurde die Löschung beantragt. Die Daten bleiben sichtbar und exportierbar; brechen Sie die Löschung ab, um wieder Änderungen vorzunehmen.",
                extensions: new Dictionary<string, object?> { ["error"] = "account_deletion_read_only" })
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
