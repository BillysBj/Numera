using Numera.Api.Contracts;
using Numera.Api.Services;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated supplier-payment commands scoped to a receipt.</summary>
public static class SupplierPaymentEndpoints
{
    public static IEndpointRouteBuilder MapSupplierPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts/{id:guid}/payments").RequireAuthorization();
        group.MapPost("/", async (
            Guid id, RecordSupplierPaymentRequest request, SupplierPaymentService service, CancellationToken ct) =>
        {
            var result = await service.RecordAsync(id, request, ct).ConfigureAwait(false);
            return result.Status == PaymentOperationStatus.Success
                ? Results.Created($"/api/receipts/{id}/payments/{result.PaymentId}", new { id = result.PaymentId })
                : MapResult(result);
        });
        group.MapPost("/{paymentId:guid}/reverse", async (
            Guid id, Guid paymentId, SupplierPaymentService service, CancellationToken ct) =>
            MapResult(await service.ReverseAsync(id, paymentId, ct).ConfigureAwait(false)));
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
