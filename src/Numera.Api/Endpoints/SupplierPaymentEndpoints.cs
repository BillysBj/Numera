using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales.Belege.Payments;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated supplier-payment commands scoped to a receipt.</summary>
public static class SupplierPaymentEndpoints
{
    public static IEndpointRouteBuilder MapSupplierPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts/{id:guid}/payments").RequireAuthorization();
        group.MapGet("/", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            // RLS scopes both tables to the ambient tenant. A payment is always fully
            // allocated to the single receipt in the route, so the allocated amount is
            // the payment's amount from this receipt's perspective (reversals carry a
            // negative allocation and surface as their own row).
            var items = await (
                from allocation in db.Set<SupplierPaymentAllocation>().AsNoTracking()
                join payment in db.Set<SupplierPayment>().AsNoTracking()
                    on allocation.PaymentId equals payment.Id
                where allocation.ReceiptId == id
                orderby payment.RecordedAt
                select new SupplierPaymentListItem(
                    payment.Id,
                    allocation.AllocatedAmount,
                    payment.ValueDate,
                    payment.Method,
                    payment.Reference,
                    payment.ReversesPaymentId,
                    payment.RecordedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(new SupplierPaymentListResponse(items));
        });
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
