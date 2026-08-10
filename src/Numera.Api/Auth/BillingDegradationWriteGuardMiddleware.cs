namespace Numera.Api.Auth;

/// <summary>Globally enforces billing degradation as non-blocking read-only access.</summary>
public sealed class BillingDegradationWriteGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BillingDegradationWriteGuardMiddleware> _logger;

    /// <summary>Creates the billing degradation guard.</summary>
    public BillingDegradationWriteGuardMiddleware(
        RequestDelegate next,
        ILogger<BillingDegradationWriteGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Allows the request onward or returns the billing read-only problem response.</summary>
    public async Task InvokeAsync(HttpContext context, IBillingState billingState)
    {
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!await billingState.IsDegradedAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!BillingReadOnlyAccessPolicy.IsAllowed(context.Request.Method, context.Request.Path))
        {
            _logger.LogWarning(
                "Billing read-only request denied: {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Nur-Lese-Modus: Zahlung/Testphase abgelaufen",
                detail: "Ihre Zahlung oder Testphase ist abgelaufen. Daten bleiben sichtbar; starten Sie einen Tarif, um wieder Änderungen vorzunehmen.",
                extensions: new Dictionary<string, object?> { ["error"] = "billing_read_only" })
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
