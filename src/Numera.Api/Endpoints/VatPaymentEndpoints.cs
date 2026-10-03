using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Ledger.Tax;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated, tenant-scoped Finanzamt cash settlements.</summary>
public static class VatPaymentEndpoints
{
    public static IEndpointRouteBuilder MapVatPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vat-payments").RequireAuthorization();
        group.MapGet("/", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var items = await db.Set<VatPayment>().AsNoTracking()
                .OrderByDescending(payment => payment.RecordedAt).ThenByDescending(payment => payment.Id)
                .Select(payment => new VatPaymentListItem(
                    payment.Id, payment.Amount, payment.Kind, payment.ValueDate, payment.Reference,
                    payment.ReversesPaymentId, payment.RecordedAt))
                .ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(new VatPaymentListResponse(items));
        });
        group.MapPost("/", async (RecordVatPaymentRequest request, VatPaymentService service, CancellationToken ct) =>
        {
            var result = await service.RecordAsync(
                request.Amount, request.Kind, request.ValueDate, request.Reference, ct).ConfigureAwait(false);
            return result.Status == PaymentOperationStatus.Success
                ? Results.Created($"/api/vat-payments/{result.PaymentId}", new { id = result.PaymentId })
                : MapResult(result);
        });
        group.MapPost("/{id:guid}/reverse", async (Guid id, VatPaymentService service, CancellationToken ct) =>
            MapResult(await service.ReverseAsync(id, ct).ConfigureAwait(false)));
        return app;
    }

    private static IResult MapResult(PaymentOperationResult result) => result.Status switch
    {
        PaymentOperationStatus.Success => Results.Ok(new { id = result.PaymentId }),
        PaymentOperationStatus.NotFound => Results.NotFound(),
        PaymentOperationStatus.Conflict => Results.Conflict(new { error = result.Error }),
        _ => Results.ValidationProblem(
            new Dictionary<string, string[]> { [result.ErrorKey ?? "payment"] = [result.Error ?? "Ungültige Zahlung."] },
            statusCode: StatusCodes.Status422UnprocessableEntity),
    };
}
