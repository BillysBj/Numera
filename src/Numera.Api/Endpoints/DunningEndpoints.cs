using Numera.Api.Contracts;
using Numera.Api.Services;

namespace Numera.Api.Endpoints;

/// <summary>Tenant dunning configuration endpoints.</summary>
public static class DunningEndpoints
{
    /// <summary>Maps GET/PUT <c>/api/dunning/config</c>.</summary>
    public static IEndpointRouteBuilder MapDunningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dunning").RequireAuthorization();

        group.MapGet("/config", async (DunningConfigService service, CancellationToken ct) =>
        {
            var levels = await service.GetConfigAsync(ct).ConfigureAwait(false);
            return Results.Ok(new DunningConfigResponse(levels));
        });

        group.MapPut("/config", async (
            UpdateDunningConfigRequest request,
            DunningConfigService service,
            CancellationToken ct) =>
        {
            var result = await service.UpsertConfigAsync(request.Levels, ct).ConfigureAwait(false);
            return result.IsValid
                ? Results.Ok(new DunningConfigResponse(result.Levels!))
                : Results.ValidationProblem(result.Errors);
        });

        // Extension seam: the dunning-run endpoint and send workflow are implemented in plan 06-04.
        return app;
    }
}
