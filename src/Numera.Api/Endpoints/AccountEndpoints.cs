using Numera.Api.Lifecycle;

namespace Numera.Api.Endpoints;

/// <summary>
/// Owner-only account lifecycle surface (Phase B1): request full account deletion, cancel it
/// during the grace window (Undo), and read the current deletion status. The actual purge runs
/// later via the sweep job; these endpoints only move the tenant in/out of the grace window.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Maps <c>/api/account/deletion</c> — Owner-only status, request, and cancel.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account/deletion").RequireAuthorization("RequireOwner");

        group.MapGet("/", async (AccountDeletionService service, CancellationToken ct) =>
            Results.Ok(await service.GetStatusAsync(ct).ConfigureAwait(false)));

        group.MapPost("/request", async (
            DeletionRequestBody body,
            AccountDeletionService service,
            CancellationToken ct) =>
        {
            var result = await service.RequestDeletionAsync(body.ConfirmationName ?? string.Empty, ct)
                .ConfigureAwait(false);

            return result.Outcome switch
            {
                AccountDeletionOutcome.Ok or AccountDeletionOutcome.AlreadyPending
                    => Results.Ok(result.Status),
                AccountDeletionOutcome.NameMismatch => Results.Problem(
                    title: "Bestätigung stimmt nicht",
                    detail: "Der eingegebene Firmenname stimmt nicht mit dem Mandantennamen überein.",
                    statusCode: StatusCodes.Status400BadRequest),
                AccountDeletionOutcome.ExportRequired => Results.Problem(
                    title: "Export erforderlich",
                    detail: "Bitte exportieren Sie zuerst Ihre Daten (DSGVO Art. 20). Die Löschung ist erst nach einem aktuellen Export möglich.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["error"] = "export_required" }),
                _ => Results.Problem(
                    title: "Konto nicht gefunden",
                    statusCode: StatusCodes.Status404NotFound),
            };
        });

        group.MapPost("/cancel", async (AccountDeletionService service, CancellationToken ct) =>
        {
            var result = await service.CancelDeletionAsync(ct).ConfigureAwait(false);
            return Results.Ok(result.Status);
        });

        return app;
    }

    /// <summary>Request body for <c>POST /api/account/deletion/request</c>.</summary>
    public sealed record DeletionRequestBody(string? ConfirmationName);
}
