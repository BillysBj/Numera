using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Sales;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>
/// The OP-Übersicht (offene Posten) HTTP surface (plan 03-04) — the read half of OPDN-01.
/// A single paged, due-date-sorted list over <c>open_items</c> with an overdue flag.
/// </summary>
/// <remarks>
/// Read-only by design: open items are CREATED atomically at invoice finalize (plan 03-05)
/// and payment recording lands in Phase 6 — neither is exposed here. RLS + the tenant query
/// filter scope the list, so no manual tenant <c>WHERE</c> is needed.
/// </remarks>
public static class OpenItemEndpoints
{
    /// <summary>Maps <c>/api/open-items</c> — the paged offene-Posten list.</summary>
    public static IEndpointRouteBuilder MapOpenItemEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/open-items").RequireAuthorization();

        // GET /api/open-items — paged, due-date-sorted list (RLS-scoped, server-side).
        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            OpenItemStatus? status = null,
            bool overdueOnly = false) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var query = db.Set<OpenItem>().AsNoTracking();

            if (status is not null)
            {
                query = query.Where(o => o.Status == status);
            }

            if (overdueOnly)
            {
                query = query.Where(o =>
                    o.DueDate < today &&
                    (o.Status == OpenItemStatus.Open || o.Status == OpenItemStatus.PartiallyPaid));
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);

            var items = await query
                .OrderBy(o => o.DueDate).ThenBy(o => o.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new OpenItemListItem(
                    o.Id, o.DocumentId, o.DocumentNumber, o.PartnerId, o.Currency,
                    o.OriginalAmount, o.OpenAmount, o.Status, o.IssuedOn, o.DueDate,
                    o.DueDate < today &&
                        (o.Status == OpenItemStatus.Open || o.Status == OpenItemStatus.PartiallyPaid)))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new { items, page, pageSize, total });
        });

        return app;
    }
}
