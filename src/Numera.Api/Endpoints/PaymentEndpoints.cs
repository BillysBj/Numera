using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>HTTP endpoints for recording, reversing and listing tenant-scoped payments.</summary>
public static class PaymentEndpoints
{
    /// <summary>Maps the authorized <c>/api/payments</c> route group.</summary>
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments").RequireAuthorization();

        group.MapPost("/", async (
            RecordPaymentRequest request,
            PaymentService service,
            CancellationToken ct) =>
        {
            var result = await service.RecordAsync(request, ct).ConfigureAwait(false);
            return MapRecordResult(result);
        });

        group.MapPost("/{id:guid}/reverse", async (
            Guid id,
            PaymentService service,
            CancellationToken ct) =>
        {
            var result = await service.ReverseAsync(id, ct).ConfigureAwait(false);
            return result.Status switch
            {
                PaymentOperationStatus.Success => Results.Ok(new { id = result.PaymentId }),
                PaymentOperationStatus.NotFound => Results.NotFound(),
                PaymentOperationStatus.Conflict => Results.Conflict(new { error = result.Error }),
                _ => Results.ValidationProblem(
                    new Dictionary<string, string[]> { [result.ErrorKey ?? "payment"] = [result.Error ?? "Ungültige Zahlung."] },
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            };
        });

        group.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            Guid? openItemId = null) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = db.Set<Payment>().AsNoTracking();
            if (openItemId is { } id)
            {
                var paymentIds = db.Set<PaymentAllocation>()
                    .Where(a => a.OpenItemId == id)
                    .Select(a => a.PaymentId);
                query = query.Where(p => paymentIds.Contains(p.Id));
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var items = await query
                .OrderByDescending(p => p.ValueDate)
                .ThenByDescending(p => p.RecordedAt)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PaymentListItem(
                    p.Id, p.Amount, p.ValueDate, p.Method, p.Reference,
                    p.ReversesPaymentId, p.RecordedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(new { items, page, pageSize, total });
        });

        return app;
    }

    private static IResult MapRecordResult(PaymentOperationResult result) =>
        result.Status switch
        {
            PaymentOperationStatus.Success =>
                Results.Created($"/api/payments/{result.PaymentId}", new { id = result.PaymentId }),
            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { [result.ErrorKey ?? "payment"] = [result.Error ?? "Ungültige Zahlung."] },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
}
