using System.Text.Json;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Modules.Catalog;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// The catalog (Artikelstamm) management HTTP surface (plan 02-05): product/service
/// CRUD + archive plus a lightweight picker that returns the stable
/// <see cref="CatalogLineItem"/> projection. Delivers CATL-01 (manage products/services
/// with price, unit and VAT) and the CATL-02 seam a future invoice line editor consumes.
/// </summary>
/// <remarks>
/// Every catalog mutation follows RESEARCH.md Pattern 4: mutate → audit, committed by a
/// single <c>SaveChangesAsync</c> so the change and its audit row are atomic. RLS + the
/// named tenant/NotArchived query filters scope all reads. There is deliberately NO
/// catalog hard-delete route (archive-never-delete); a duplicate article number surfaces
/// from the partial-unique index as a 409 Conflict, never a 500.
/// </remarks>
public static class CatalogEndpoints
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/api/catalog-items</c> CRUD, archive and the CATL-02 picker.</summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/catalog-items").RequireAuthorization();

        // GET /api/catalog-items — paged/filterable list, OR (picker=true) the compact
        // CatalogLineItem projection for the Phase-3 invoice line editor (CATL-02 seam).
        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? q = null,
            bool archived = false,
            bool picker = false) =>
        {
            // Picker mode: non-archived items matching q, capped, as CatalogLineItem.
            if (picker)
            {
                var pickerQuery = db.Set<CatalogItem>().AsNoTracking();
                if (!string.IsNullOrWhiteSpace(q))
                {
                    pickerQuery = pickerQuery.Where(c => c.ItemNumber.Contains(q) || c.Name.Contains(q));
                }

                var lines = await pickerQuery
                    .OrderBy(c => c.Name)
                    .Take(20)
                    .Select(c => new CatalogLineItem(
                        c.Id, c.ItemNumber, c.Name, c.Description, c.UnitCode, c.NetPrice, c.TaxCategory, c.VatRatePercent))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return Results.Ok(lines);
            }

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // Default list hides archived rows (NotArchived filter). When the archived
            // toggle is on, drop ONLY that filter (keeps the tenant filter + RLS).
            var query = archived
                ? db.Set<CatalogItem>().IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                : db.Set<CatalogItem>();

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(c => c.ItemNumber.Contains(q) || c.Name.Contains(q));
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);

            var items = await query
                .OrderBy(c => c.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CatalogListItem(
                    c.Id, c.ItemNumber, c.Name, c.Kind, c.UnitCode,
                    c.NetPrice, c.TaxCategory, c.VatRatePercent, c.ArchivedAt != null))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new { items, page, pageSize, total });
        });

        // GET /api/catalog-items/{id} — full detail (archived rows are still readable by id).
        g.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var c = await db.Set<CatalogItem>()
                .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);

            return c is null ? Results.NotFound() : Results.Ok(ToDetail(c));
        });

        // POST /api/catalog-items — create.
        g.MapPost("/", async (
            CreateCatalogItemRequest req,
            IValidator<CreateCatalogItemRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var item = new CatalogItem
            {
                TenantId = tenant.TenantId!.Value,
                ItemNumber = req.ItemNumber,
                Name = req.Name,
                Description = req.Description,
                Kind = req.Kind,
                UnitCode = string.IsNullOrWhiteSpace(req.UnitCode)
                    ? UnitOfMeasure.DefaultFor(req.Kind)
                    : req.UnitCode,
                NetPrice = req.NetPrice,
                Currency = req.Currency,
                TaxCategory = req.TaxCategory,
                VatRatePercent = req.VatRatePercent,
                CostPrice = req.CostPrice,
            };

            db.Add(item);
            await audit.RecordAsync(
                new CatalogAuditEvent("catalog_item.created", item.Id, Before: null, After: Snapshot(item)), ct)
                .ConfigureAwait(false);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsDuplicateNumber(ex))
            {
                return DuplicateNumberProblem(req.ItemNumber);
            }

            return Results.Created($"/api/catalog-items/{item.Id}", new { item.Id });
        });

        // PUT /api/catalog-items/{id} — full update.
        g.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCatalogItemRequest req,
            IValidator<UpdateCatalogItemRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var item = await db.Set<CatalogItem>()
                .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (item is null)
            {
                return Results.NotFound();
            }

            var before = Snapshot(item);

            item.ItemNumber = req.ItemNumber;
            item.Name = req.Name;
            item.Description = req.Description;
            item.Kind = req.Kind;
            item.UnitCode = string.IsNullOrWhiteSpace(req.UnitCode)
                ? UnitOfMeasure.DefaultFor(req.Kind)
                : req.UnitCode;
            item.NetPrice = req.NetPrice;
            item.Currency = req.Currency;
            item.TaxCategory = req.TaxCategory;
            item.VatRatePercent = req.VatRatePercent;
            item.CostPrice = req.CostPrice;

            await audit.RecordAsync(
                new CatalogAuditEvent("catalog_item.updated", item.Id, before, Snapshot(item)), ct)
                .ConfigureAwait(false);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsDuplicateNumber(ex))
            {
                return DuplicateNumberProblem(req.ItemNumber);
            }

            return Results.Ok(ToDetail(item));
        });

        // POST /api/catalog-items/{id}/archive — soft-delete (never a hard DELETE).
        g.MapPost("/{id:guid}/archive", (Guid id, NumeraDbContext db, IAuditWriter audit, CancellationToken ct) =>
            SetArchivedAsync(id, archive: true, db, audit, ct));

        // POST /api/catalog-items/{id}/unarchive — reactivate.
        g.MapPost("/{id:guid}/unarchive", (Guid id, NumeraDbContext db, IAuditWriter audit, CancellationToken ct) =>
            SetArchivedAsync(id, archive: false, db, audit, ct));

        return app;
    }

    private static async Task<IResult> SetArchivedAsync(
        Guid id, bool archive, NumeraDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        var item = await db.Set<CatalogItem>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Results.NotFound();
        }

        var before = Snapshot(item);
        item.ArchivedAt = archive ? DateTimeOffset.UtcNow : null;

        var action = archive ? "catalog_item.archived" : "catalog_item.unarchived";
        await audit.RecordAsync(new CatalogAuditEvent(action, item.Id, before, Snapshot(item)), ct)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    // --- Mapping + shared helpers -------------------------------------------

    private static CatalogItemDetail ToDetail(CatalogItem c) => new(
        c.Id, c.ItemNumber, c.Name, c.Description, c.Kind, c.UnitCode,
        c.NetPrice, c.Currency, c.TaxCategory, c.VatRatePercent, c.CostPrice,
        c.ArchivedAt != null);

    private static string Snapshot(CatalogItem c) => JsonSerializer.Serialize(new
    {
        c.ItemNumber,
        c.Name,
        c.Description,
        Kind = c.Kind.ToString(),
        c.UnitCode,
        c.NetPrice,
        c.Currency,
        TaxCategory = c.TaxCategory.ToString(),
        c.VatRatePercent,
        c.CostPrice,
        Archived = c.ArchivedAt != null,
    }, AuditJson);

    // A duplicate (tenant_id, item_number) among non-archived rows violates the partial
    // unique index → Postgres 23505. Return a clean 409 instead of a 500.
    private static bool IsDuplicateNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult DuplicateNumberProblem(string itemNumber) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["ItemNumber"] = [$"The article number \"{itemNumber}\" already exists."],
            },
            statusCode: StatusCodes.Status409Conflict,
            title: "Duplicate article number");
}

/// <summary>
/// A minimal <see cref="IAuditEvent"/> for catalog mutations. The writer stamps
/// tenant + actor from ambient context; the endpoint supplies only the change.
/// </summary>
internal sealed record CatalogAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(CatalogItem);
}
