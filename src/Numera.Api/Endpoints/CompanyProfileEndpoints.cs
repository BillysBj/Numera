using System.Text.Json;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Sales;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// The company-profile (issuer / Ausstellerstammdaten) settings surface (plan 03-01):
/// GET + PUT of the single §14 UStG issuer record a tenant owns. This is the issuer data
/// source plan 03-05 (finalize) snapshots onto documents. PUT has upsert semantics — it
/// creates the profile on the first write and updates it thereafter, because a tenant has
/// exactly one profile (unique index on tenant_id).
/// </summary>
/// <remarks>
/// Follows the Catalog mutate → audit → single-SaveChanges idiom: the entity change and its
/// audit row commit atomically. RLS + the tenant query filter scope the read. §14 validation
/// (legal name + billing address + exactly one tax id) is enforced by
/// <c>IValidator&lt;UpdateCompanyProfileRequest&gt;</c>, auto-registered from this assembly.
/// </remarks>
public static class CompanyProfileEndpoints
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/api/company-profile</c> GET (read) and PUT (upsert).</summary>
    public static IEndpointRouteBuilder MapCompanyProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/company-profile").RequireAuthorization();

        // GET /api/company-profile — the tenant's single profile, or an empty editable
        // shell (all nulls) when none exists yet so the settings form has a create state.
        g.MapGet("/", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var profile = await db.Set<CompanyProfile>()
                .AsNoTracking()
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(profile is null ? EmptyProfile() : ToDto(profile));
        });

        // PUT /api/company-profile — UPSERT: create on first write, update thereafter.
        g.MapPut("/", async (
            UpdateCompanyProfileRequest req,
            IValidator<UpdateCompanyProfileRequest> validator,
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

            var profile = await db.Set<CompanyProfile>()
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            string? before;
            string action;
            if (profile is null)
            {
                profile = new CompanyProfile
                {
                    TenantId = tenant.TenantId!.Value,
                    LegalName = req.LegalName,
                    Address = ToAddress(req.Address),
                };
                Apply(profile, req);
                db.Add(profile);
                before = null;
                action = "company_profile.created";
            }
            else
            {
                before = Snapshot(profile);
                profile.LegalName = req.LegalName;
                profile.Address = ToAddress(req.Address);
                Apply(profile, req);
                action = "company_profile.updated";
            }

            await audit.RecordAsync(
                new CompanyProfileAuditEvent(action, profile.Id, before, Snapshot(profile)), ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Ok(ToDto(profile));
        });

        return app;
    }

    // --- Mapping + shared helpers -------------------------------------------

    // Copies the non-identity issuer fields from the request onto the entity (shared by the
    // create and update branches; LegalName + Address are set by the caller).
    private static void Apply(CompanyProfile p, UpdateCompanyProfileRequest req)
    {
        p.VatId = Trim(req.VatId);
        p.TaxNumber = Trim(req.TaxNumber);
        p.IsKleinunternehmer = req.IsKleinunternehmer;
        p.DefaultPaymentTermsNetDays = req.DefaultPaymentTermsNetDays;
        p.DefaultTaxCategory = req.DefaultTaxCategory;
        p.Iban = Trim(req.Iban);
        p.Bic = Trim(req.Bic);
        p.BankName = Trim(req.BankName);
        p.RegisterCourt = Trim(req.RegisterCourt);
        p.RegisterNumber = Trim(req.RegisterNumber);
        p.ManagingDirector = Trim(req.ManagingDirector);
        p.ContactEmail = Trim(req.ContactEmail);
        p.ContactPhone = Trim(req.ContactPhone);
        p.LogoRef = Trim(req.LogoRef);
    }

    private static Address ToAddress(AddressDto a) => new()
    {
        Street = a.Street,
        Line2 = Trim(a.Line2),
        PostalCode = a.PostalCode,
        City = a.City,
        CountryCode = string.IsNullOrWhiteSpace(a.CountryCode) ? "DE" : a.CountryCode,
        PoBox = Trim(a.PoBox),
    };

    private static CompanyProfileDto ToDto(CompanyProfile p) => new(
        p.LegalName,
        new AddressDto(
            p.Address.Street, p.Address.Line2, p.Address.PostalCode,
            p.Address.City, p.Address.CountryCode, p.Address.PoBox),
        p.VatId, p.TaxNumber, p.IsKleinunternehmer,
        p.DefaultPaymentTermsNetDays, p.DefaultTaxCategory,
        p.Iban, p.Bic, p.BankName,
        p.RegisterCourt, p.RegisterNumber, p.ManagingDirector,
        p.ContactEmail, p.ContactPhone, p.LogoRef);

    private static CompanyProfileDto EmptyProfile() => new(
        LegalName: null, Address: null, VatId: null, TaxNumber: null,
        IsKleinunternehmer: false, DefaultPaymentTermsNetDays: null, DefaultTaxCategory: null,
        Iban: null, Bic: null, BankName: null, RegisterCourt: null, RegisterNumber: null,
        ManagingDirector: null, ContactEmail: null, ContactPhone: null, LogoRef: null);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Snapshot(CompanyProfile p) => JsonSerializer.Serialize(new
    {
        p.LegalName,
        p.Address.Street,
        p.Address.PostalCode,
        p.Address.City,
        p.Address.CountryCode,
        p.VatId,
        p.TaxNumber,
        p.IsKleinunternehmer,
        p.DefaultPaymentTermsNetDays,
        DefaultTaxCategory = p.DefaultTaxCategory?.ToString(),
    }, AuditJson);
}

/// <summary>
/// A minimal <see cref="IAuditEvent"/> for company-profile mutations. The writer stamps
/// tenant + actor from ambient context; the endpoint supplies only the change.
/// </summary>
internal sealed record CompanyProfileAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(CompanyProfile);
}
