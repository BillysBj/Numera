using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Platform.Db;

namespace Numera.Modules.Sales.EInvoice.Inbound;

/// <summary>
/// Matches a received e-invoice's seller to a <see cref="BusinessPartner"/> supplier (Phase-5
/// EINV-05). Deterministic + auditable (RESEARCH "Supplier matching key"): an EXACT VAT id match
/// first (BT-31, the strong key), then a fallback exact name match; returns the partner id or null
/// when no confident match exists ("nicht zugeordnet" in the UI).
/// </summary>
/// <remarks>
/// A thin service that takes the (RLS-scoped) <see cref="NumeraDbContext"/> so it stays testable and
/// so the query runs under the current tenant. It only READS the partner master — the matched id is
/// stored as provenance on the inbound document; no partner is created or mutated. When several
/// partners share a VAT id / name (data-entry duplicates), a supplier row (<see cref="BusinessPartner.IsSupplier"/>)
/// is preferred, then the earliest-created, so the result is stable across re-runs.
/// </remarks>
public sealed class SupplierMatcher
{
    /// <summary>
    /// Resolves the supplier for the parsed seller: exact VAT id, then exact name; null if neither
    /// yields a match. VAT ids are compared case-insensitively with surrounding whitespace removed
    /// (the raw stored value is otherwise untouched — normalization is a comparison detail only).
    /// </summary>
    public async Task<Guid?> MatchSellerAsync(
        NumeraDbContext db,
        string? sellerVatId,
        string? sellerName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var vatId = Normalize(sellerVatId);
        if (vatId is not null)
        {
            var byVat = await db.Set<BusinessPartner>()
                .AsNoTracking()
                .Where(p => p.VatId != null && p.VatId.ToUpper() == vatId)
                .OrderByDescending(p => p.IsSupplier)
                .ThenBy(p => p.Id)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (byVat is not null)
            {
                return byVat;
            }
        }

        var name = sellerName?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = await db.Set<BusinessPartner>()
                .AsNoTracking()
                .Where(p => p.Name == name)
                .OrderByDescending(p => p.IsSupplier)
                .ThenBy(p => p.Id)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (byName is not null)
            {
                return byName;
            }
        }

        return null;
    }

    // Upper-cases + trims a VAT id for a case-insensitive exact comparison; blank → null.
    private static string? Normalize(string? vatId)
    {
        var trimmed = vatId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
