using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Endpoints;
using Numera.Modules.Crm;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Lifecycle;

/// <summary>
/// Irreversibly anonymizes a single <see cref="BusinessPartner"/> (Phase B1 Scheibe 2, Szenario 2:
/// DSGVO Art. 17 right to erasure for a customer/supplier that cannot be hard-deleted because it is
/// referenced by GoBD-retained documents).
/// </summary>
/// <remarks>
/// <para>
/// The partner row is kept (its <c>CustomerNumber</c>/<c>SupplierNumber</c>, role flags and
/// ledger accounts stay) so the FK from historical <c>SalesDocument</c>s remains valid, but every
/// personal/identifying master field is overwritten with a neutral placeholder. This does NOT touch
/// the already-issued documents: each sales document froze its own <c>RecipientSnapshot</c> at
/// finalization, so the legally retained invoice still shows the original buyer. The authoritative
/// change trail lives in the immutable <c>audit_events</c> (kept under the Art. 17(3)(b) legal-
/// obligation exemption); this service writes one more audit row recording the anonymization.
/// </para>
/// <para>
/// The partner's PII-bearing child records that are NOT themselves GoBD documents — contacts,
/// notes, tasks and the free-text activity timeline — are hard-deleted, then a single neutral
/// "anonymized" activity is appended. All writes commit in one <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
/// so the anonymization, the child-record removal and the audit row are atomic.
/// </para>
/// </remarks>
public sealed class PartnerAnonymizationService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly NumeraDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;

    /// <summary>Creates the scoped anonymization service for the current request.</summary>
    public PartnerAnonymizationService(NumeraDbContext db, IAuditWriter audit, ICurrentUser user)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    /// <summary>
    /// Anonymizes the partner identified by <paramref name="partnerId"/>. Idempotent: a partner that
    /// is already anonymized is reported as such and left unchanged.
    /// </summary>
    public async Task<PartnerAnonymizationOutcome> AnonymizeAsync(Guid partnerId, CancellationToken ct = default)
    {
        // Archived partners are still subject to erasure, so look past the NotArchived filter.
        var partner = await _db.Set<BusinessPartner>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .FirstOrDefaultAsync(p => p.Id == partnerId, ct)
            .ConfigureAwait(false);
        if (partner is null)
        {
            return PartnerAnonymizationOutcome.NotFound;
        }

        if (partner.AnonymizedAt is not null)
        {
            return PartnerAnonymizationOutcome.AlreadyAnonymized;
        }

        var before = Snapshot(partner);
        var now = DateTimeOffset.UtcNow;

        // Overwrite every personal/identifying master field with a neutral placeholder. Deliberately
        // kept: the partner number (document continuity), role flags, currency/language and the DATEV
        // ledger accounts (bookkeeping identifiers, not personal data).
        partner.Name = "Anonymisiert";
        partner.LegalForm = null;
        partner.BillingAddress = EmptyAddress();
        partner.ShippingAddress = null;
        partner.VatId = null;
        partner.TaxNumber = null;
        partner.Email = null;
        partner.Phone = null;
        partner.Website = null;
        partner.Iban = null;
        partner.Bic = null;
        partner.LeitwegId = null;
        partner.AnonymizedAt = now;
        // An anonymized partner must disappear from the active list but stay resolvable by id.
        partner.ArchivedAt ??= now;

        // Hard-delete the PII-bearing, GoBD-irrelevant child records (not frozen into any document).
        await DeleteChildrenAsync<PartnerContact>(partnerId, ct).ConfigureAwait(false);
        await DeleteChildrenAsync<PartnerNote>(partnerId, ct).ConfigureAwait(false);
        await DeleteChildrenAsync<PartnerTask>(partnerId, ct).ConfigureAwait(false);
        // The activity timeline carries the old name in its summaries → replace it with one neutral row.
        await DeleteChildrenAsync<PartnerActivity>(partnerId, ct).ConfigureAwait(false);
        _db.Add(new PartnerActivity
        {
            TenantId = partner.TenantId,
            PartnerId = partnerId,
            Type = PartnerActivityType.Anonymized,
            Summary = "Partner anonymisiert (DSGVO Art. 17)",
            ActorUserId = _user.UserId,
        });

        await _audit.RecordAsync(
            new PartnerAuditEvent("partner.anonymized", partnerId, before, After: null), ct)
            .ConfigureAwait(false);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return PartnerAnonymizationOutcome.Ok;
    }

    private async Task DeleteChildrenAsync<TEntity>(Guid partnerId, CancellationToken ct)
        where TEntity : class
    {
        var rows = await _db.Set<TEntity>()
            .Where(e => EF.Property<Guid>(e, "PartnerId") == partnerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        _db.RemoveRange(rows);
    }

    private static Address EmptyAddress() => new()
    {
        Street = string.Empty,
        Line2 = null,
        PostalCode = string.Empty,
        City = string.Empty,
        CountryCode = string.Empty,
        PoBox = null,
    };

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
        p.CustomerNumber,
        p.SupplierNumber,
    }, AuditJson);
}

/// <summary>The outcome of an anonymization request.</summary>
public enum PartnerAnonymizationOutcome
{
    /// <summary>The partner was anonymized.</summary>
    Ok,

    /// <summary>No partner with the given id exists for this tenant.</summary>
    NotFound,

    /// <summary>The partner was already anonymized (request was a no-op).</summary>
    AlreadyAnonymized,
}
