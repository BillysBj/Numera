using System.Text.Json;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Crm;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// The partner management HTTP surface (plan 02-04): partner CRUD + archive, contact
/// and note sub-resources, and the read-only activity timeline. Delivers CRM-01
/// (create/edit/archive partners), CRM-03 (notes) and the read half of CRM-02
/// (per-partner activity history).
/// </summary>
/// <remarks>
/// Every partner mutation follows RESEARCH.md Pattern 4: mutate → audit → activity,
/// committed by a single <c>SaveChangesAsync</c> so the change, its audit row and its
/// timeline entry are atomic. RLS + the named tenant/NotArchived query filters scope
/// all reads, so no manual tenant <c>WHERE</c> is needed beyond asserting ownership on
/// 404. There is deliberately NO partner hard-delete route (archive-never-delete).
/// </remarks>
public static class PartnerEndpoints
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/api/partners</c> and its contact/note/activity sub-resources.</summary>
    public static IEndpointRouteBuilder MapPartnerEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/partners").RequireAuthorization();

        MapPartnerCrud(g);
        MapContacts(g);
        MapNotes(g);
        MapActivities(g);

        return app;
    }

    // --- Partner CRUD + archive ---------------------------------------------

    private static void MapPartnerCrud(RouteGroupBuilder g)
    {
        // GET /api/partners — paged, filterable, sortable list.
        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? q = null,
            string? role = null,
            bool archived = false) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // Default list hides archived rows (NotArchived filter). When the archived
            // toggle is on, drop ONLY that filter (keeps the tenant filter + RLS).
            var query = archived
                ? db.Set<BusinessPartner>().IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                : db.Set<BusinessPartner>();

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(p => p.Name.Contains(q));
            }

            query = role switch
            {
                "customer" => query.Where(p => p.IsCustomer),
                "supplier" => query.Where(p => p.IsSupplier),
                _ => query,
            };

            var total = await query.CountAsync(ct).ConfigureAwait(false);

            var items = await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PartnerListItem(
                    p.Id, p.Name, p.IsCustomer, p.IsSupplier,
                    p.BillingAddress.City, p.ArchivedAt != null))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new { items, page, pageSize, total });
        });

        // GET /api/partners/{id} — full detail (archived rows are still readable by id).
        g.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var p = await db.Set<BusinessPartner>()
                .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);

            return p is null ? Results.NotFound() : Results.Ok(ToDetail(p));
        });

        // POST /api/partners — create.
        g.MapPost("/", async (
            CreatePartnerRequest req,
            IValidator<CreatePartnerRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var p = new BusinessPartner
            {
                TenantId = tenant.TenantId!.Value,
                Name = req.Name,
                LegalForm = req.LegalForm,
                BillingAddress = ToAddress(req.BillingAddress),
                ShippingAddress = req.ShippingAddress is null ? null : ToAddress(req.ShippingAddress),
                VatId = req.VatId,
                TaxNumber = req.TaxNumber,
                Email = req.Email,
                Phone = req.Phone,
                Website = req.Website,
                PaymentTermsNetDays = req.PaymentTermsNetDays,
                SkontoPercent = req.SkontoPercent,
                SkontoDays = req.SkontoDays,
                DefaultCurrency = req.DefaultCurrency,
                Language = req.Language,
                DefaultTaxCategory = req.DefaultTaxCategory,
                IsCustomer = req.IsCustomer,
                IsSupplier = req.IsSupplier,
                CustomerNumber = req.CustomerNumber,
                SupplierNumber = req.SupplierNumber,
            };

            db.Add(p);
            await audit.RecordAsync(
                new PartnerAuditEvent("partner.created", p.Id, Before: null, After: Snapshot(p)), ct)
                .ConfigureAwait(false);
            AddActivity(db, p.Id, p.TenantId, PartnerActivityType.PartnerCreated,
                $"Partner \"{p.Name}\" created", user.UserId);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/partners/{p.Id}", new { p.Id });
        });

        // PUT /api/partners/{id} — full update.
        g.MapPut("/{id:guid}", async (
            Guid id,
            UpdatePartnerRequest req,
            IValidator<UpdatePartnerRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var p = await db.Set<BusinessPartner>()
                .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (p is null)
            {
                return Results.NotFound();
            }

            var before = Snapshot(p);

            p.Name = req.Name;
            p.LegalForm = req.LegalForm;
            p.BillingAddress = ToAddress(req.BillingAddress);
            p.ShippingAddress = req.ShippingAddress is null ? null : ToAddress(req.ShippingAddress);
            p.VatId = req.VatId;
            p.TaxNumber = req.TaxNumber;
            p.Email = req.Email;
            p.Phone = req.Phone;
            p.Website = req.Website;
            p.PaymentTermsNetDays = req.PaymentTermsNetDays;
            p.SkontoPercent = req.SkontoPercent;
            p.SkontoDays = req.SkontoDays;
            p.DefaultCurrency = req.DefaultCurrency;
            p.Language = req.Language;
            p.DefaultTaxCategory = req.DefaultTaxCategory;
            p.IsCustomer = req.IsCustomer;
            p.IsSupplier = req.IsSupplier;
            p.CustomerNumber = req.CustomerNumber;
            p.SupplierNumber = req.SupplierNumber;

            await audit.RecordAsync(
                new PartnerAuditEvent("partner.updated", p.Id, before, Snapshot(p)), ct)
                .ConfigureAwait(false);
            AddActivity(db, p.Id, p.TenantId, PartnerActivityType.PartnerUpdated,
                $"Partner \"{p.Name}\" updated", user.UserId);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Ok(ToDetail(p));
        });

        // POST /api/partners/{id}/archive — soft-delete (never a hard DELETE).
        g.MapPost("/{id:guid}/archive", (Guid id, NumeraDbContext db, IAuditWriter audit, ICurrentUser user, CancellationToken ct) =>
            SetArchivedAsync(id, archive: true, db, audit, user, ct));

        // POST /api/partners/{id}/unarchive — reactivate.
        g.MapPost("/{id:guid}/unarchive", (Guid id, NumeraDbContext db, IAuditWriter audit, ICurrentUser user, CancellationToken ct) =>
            SetArchivedAsync(id, archive: false, db, audit, user, ct));
    }

    private static async Task<IResult> SetArchivedAsync(
        Guid id, bool archive, NumeraDbContext db, IAuditWriter audit, ICurrentUser user, CancellationToken ct)
    {
        var p = await db.Set<BusinessPartner>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
        if (p is null)
        {
            return Results.NotFound();
        }

        var before = Snapshot(p);
        p.ArchivedAt = archive ? DateTimeOffset.UtcNow : null;

        var (action, type, summary) = archive
            ? ("partner.archived", PartnerActivityType.Archived, $"Partner \"{p.Name}\" archived")
            : ("partner.unarchived", PartnerActivityType.Unarchived, $"Partner \"{p.Name}\" reactivated");

        await audit.RecordAsync(new PartnerAuditEvent(action, p.Id, before, Snapshot(p)), ct).ConfigureAwait(false);
        AddActivity(db, p.Id, p.TenantId, type, summary, user.UserId);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    // --- Mapping + shared helpers -------------------------------------------

    private static Address ToAddress(AddressDto a) => new()
    {
        Street = a.Street,
        Line2 = a.Line2,
        PostalCode = a.PostalCode,
        City = a.City,
        CountryCode = a.CountryCode,
        PoBox = a.PoBox,
    };

    private static AddressDto ToAddressDto(Address a) =>
        new(a.Street, a.Line2, a.PostalCode, a.City, a.CountryCode, a.PoBox);

    private static PartnerDetail ToDetail(BusinessPartner p) => new(
        p.Id, p.Name, p.LegalForm,
        ToAddressDto(p.BillingAddress),
        p.ShippingAddress is null ? null : ToAddressDto(p.ShippingAddress),
        p.VatId, p.TaxNumber, p.Email, p.Phone, p.Website,
        p.PaymentTermsNetDays, p.SkontoPercent, p.SkontoDays,
        p.DefaultCurrency, p.Language, p.DefaultTaxCategory,
        p.IsCustomer, p.IsSupplier, p.CustomerNumber, p.SupplierNumber,
        p.ArchivedAt != null);

    private static void AddActivity(
        NumeraDbContext db, Guid partnerId, Guid tenantId, PartnerActivityType type, string summary, Guid? actor) =>
        db.Add(new PartnerActivity
        {
            TenantId = tenantId,
            PartnerId = partnerId,
            Type = type,
            Summary = summary,
            ActorUserId = actor,
        });

    private static string Snapshot(BusinessPartner p) => JsonSerializer.Serialize(new
    {
        p.Name,
        p.LegalForm,
        p.BillingAddress,
        p.ShippingAddress,
        p.VatId,
        p.TaxNumber,
        p.Email,
        p.Phone,
        p.Website,
        p.PaymentTermsNetDays,
        p.SkontoPercent,
        p.SkontoDays,
        p.DefaultCurrency,
        Language = p.Language.ToString(),
        DefaultTaxCategory = p.DefaultTaxCategory?.ToString(),
        p.IsCustomer,
        p.IsSupplier,
        p.CustomerNumber,
        p.SupplierNumber,
        Archived = p.ArchivedAt != null,
    }, AuditJson);

    // Contact / note / activity sub-resources (CRM-02/03) — filled in Task 3.
    private static void MapContacts(RouteGroupBuilder g)
    {
    }

    private static void MapNotes(RouteGroupBuilder g)
    {
    }

    private static void MapActivities(RouteGroupBuilder g)
    {
    }
}

/// <summary>
/// A minimal <see cref="IAuditEvent"/> for partner mutations. The writer stamps
/// tenant + actor from ambient context; the endpoint supplies only the change.
/// </summary>
internal sealed record PartnerAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(BusinessPartner);
}
