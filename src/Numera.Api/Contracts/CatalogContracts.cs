using Numera.Modules.Catalog;
using Numera.Platform.Money;

namespace Numera.Api.Contracts;

/// <summary>
/// Request/response DTOs for the catalog (Artikelstamm) management API (plan 02-05).
/// Deliver CATL-01 (manage products/services with price, unit and VAT) plus the
/// CATL-02 seam — a stable <see cref="CatalogLineItem"/> projection a future invoice
/// line editor consumes. Money-adjacent fields are <see cref="decimal"/> — never
/// <c>float</c>/<c>double</c>. The <c>RevenueAccount</c> SKR03/04 seam on
/// <see cref="CatalogItem"/> stays null in v1 (no DTO surface).
/// </summary>
/// <remarks>
/// Payload to create a new catalog item (CATL-01). <c>UnitCode</c> must be a curated
/// UN/ECE Rec 20 code (<see cref="UnitOfMeasure"/>); an empty unit defaults via
/// <see cref="UnitOfMeasure.DefaultFor"/> for the item kind.
/// </remarks>
public sealed record CreateCatalogItemRequest(
    string ItemNumber,
    string Name,
    string? Description,
    CatalogItemKind Kind,
    string? UnitCode,
    decimal NetPrice,
    string Currency,
    TaxCategory TaxCategory,
    decimal? VatRatePercent,
    decimal? CostPrice);

/// <summary>Payload to update an existing catalog item. Same shape as create (full replace).</summary>
public sealed record UpdateCatalogItemRequest(
    string ItemNumber,
    string Name,
    string? Description,
    CatalogItemKind Kind,
    string? UnitCode,
    decimal NetPrice,
    string Currency,
    TaxCategory TaxCategory,
    decimal? VatRatePercent,
    decimal? CostPrice);

/// <summary>Compact projection for the paged catalog list (GET /api/catalog-items).</summary>
public sealed record CatalogListItem(
    Guid Id,
    string ItemNumber,
    string Name,
    CatalogItemKind Kind,
    string UnitCode,
    decimal NetPrice,
    TaxCategory TaxCategory,
    decimal? VatRatePercent,
    bool Archived);

/// <summary>Full catalog item projection for the detail view (GET /api/catalog-items/{id}).</summary>
public sealed record CatalogItemDetail(
    Guid Id,
    string ItemNumber,
    string Name,
    string? Description,
    CatalogItemKind Kind,
    string UnitCode,
    decimal NetPrice,
    string Currency,
    TaxCategory TaxCategory,
    decimal? VatRatePercent,
    decimal? CostPrice,
    bool Archived);

/// <summary>
/// The CATL-02 seam — the stable "usable as an invoice line" projection of a catalog
/// item. GET /api/catalog-items?picker=true returns a capped list of these for the
/// Phase-3 invoice line editor.
/// </summary>
/// <remarks>
/// Phase 3 SNAPSHOTS these values onto the invoice line at creation time: the catalog
/// entry is a convenience source for the line's initial number/name/unit/price/tax, but
/// it is NOT the source of truth once the line exists. Editing or archiving the catalog
/// item later must never mutate a posted line. Keep this shape additive-only so the
/// Phase-3 contract does not break.
/// </remarks>
public sealed record CatalogLineItem(
    Guid Id,
    string ItemNumber,
    string Name,
    string UnitCode,
    decimal NetPrice,
    TaxCategory TaxCategory,
    decimal? VatRatePercent);
