using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Catalog;

/// <summary>
/// A single sellable product or service in the tenant's catalog (Artikelstamm) — the
/// master record referenced by future invoice/quote line items. Carries an article
/// number (Artikelnummer, unique per tenant among non-archived rows), a UN/ECE Rec 20
/// unit code, a Money-precision net price, and the EN 16931 tax identity so Phase-3
/// invoicing can consume it without a schema break.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy + tenant query filter;
/// <see cref="IArchivable"/> makes it soft-deletable (hidden by the "NotArchived"
/// filter, never physically removed — an archived article's number is reusable).
/// </para>
/// <para>
/// Prices are exact <see cref="decimal"/> columns mapped to <c>numeric(19,4)</c>
/// (never <c>float</c>), matching the <see cref="Money"/> precision contract.
/// <see cref="RevenueAccount"/> is a nullable SKR03/04 hint seam for the later DATEV
/// export (Phase 6) with no v1 UI.
/// </para>
/// </remarks>
[Table("catalog_items")]
[Index(nameof(TenantId), nameof(Name))]
public sealed class CatalogItem : ITenantEntity, IArchivable
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    // --- Identity ------------------------------------------------------------

    /// <summary>
    /// Article number (Artikelnummer). Unique per tenant among non-archived rows
    /// (enforced by a partial unique index in the migration, NOT a DB sequence —
    /// gapless legal numbering is a separate concern).
    /// </summary>
    public required string ItemNumber { get; set; }

    /// <summary>Human-readable article name (Artikelbezeichnung, required).</summary>
    public required string Name { get; set; }

    /// <summary>Longer description / line-item text (optional).</summary>
    public string? Description { get; set; }

    /// <summary>Whether this is a product (Ware) or a service (Dienstleistung).</summary>
    public CatalogItemKind Kind { get; set; } = CatalogItemKind.Product;

    // --- Quantity / pricing --------------------------------------------------

    /// <summary>
    /// UN/ECE Rec 20 unit-of-measure code (EN 16931 BT-130), e.g. <c>C62</c> (Stück)
    /// or <c>HUR</c> (Stunde). Defaults via <see cref="UnitOfMeasure.DefaultFor"/>.
    /// </summary>
    public string UnitCode { get; set; } = UnitOfMeasure.Piece;

    /// <summary>Net unit price (Nettopreis). Exact decimal → <c>numeric(19,4)</c>, never float.</summary>
    [Precision(19, 4)]
    public decimal NetPrice { get; set; }

    /// <summary>Price currency as an ISO 4217 code (BT-5). Defaults to EUR.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>
    /// Optional cost price (Einkaufspreis) for later margin / EÜR reporting. Nullable,
    /// no v1 UI. Exact decimal → <c>numeric(19,4)</c>.
    /// </summary>
    [Precision(19, 4)]
    public decimal? CostPrice { get; set; }

    // --- Tax identity --------------------------------------------------------

    /// <summary>EN 16931 VAT category (BT-151 seam), e.g. standard rate.</summary>
    public TaxCategory TaxCategory { get; set; } = TaxCategory.S;

    /// <summary>Applicable VAT rate as a percentage (e.g. 19.00, 7.00, 0). Nullable.</summary>
    [Precision(5, 2)]
    public decimal? VatRatePercent { get; set; }

    // --- Deferred cheap nullable seams (no v1 UI/validation) -----------------

    /// <summary>
    /// SKR03/SKR04 revenue-account hint (Erlöskonto) for the later DATEV export seam.
    /// Nullable, no v1 UI.
    /// </summary>
    public string? RevenueAccount { get; set; }

    // --- Lifecycle -----------------------------------------------------------

    /// <inheritdoc />
    public DateTimeOffset? ArchivedAt { get; set; }
}
