using Numera.Modules.Sales;
using Numera.Platform.Money;

namespace Numera.Api.Contracts;

/// <summary>
/// Request/response DTOs for the sales-document HTTP surface (plan 03-04): draft
/// create/update/delete/convert plus list/detail over the polymorphic
/// <see cref="SalesDocument"/> aggregate. Delivers DOCS-01 (the Angebot → AB →
/// Lieferschein → Rechnung chain with status tracking) for the draft lifecycle;
/// finalize/numbering/Storno land in later plans.
/// </summary>
/// <remarks>
/// Enums cross the wire as NUMBERS (the Catalog convention — System.Text.Json's default
/// enum serialization; the React client mirrors the ordinals). Money-adjacent fields are
/// exact <see cref="decimal"/> — never <c>float</c>/<c>double</c>. Each
/// <see cref="SalesLineRequest"/> already carries the catalog SNAPSHOT (name/unit/net
/// price/tax category/rate) from the client (the CATL-02 seam); the server never re-reads
/// the catalog once a line exists (RESEARCH.md Pitfall 2).
/// </remarks>
public sealed record CreateSalesDocumentRequest(
    DocumentType DocumentType,
    Guid? PartnerId,
    DateOnly DocumentDate,
    DateOnly? ServiceDate,
    string? Notes,
    string? BuyerReference,
    IReadOnlyList<SalesLineRequest> Lines);

/// <summary>Payload to update an existing draft. Same shape as create (full replace of header + lines).</summary>
public sealed record UpdateSalesDocumentRequest(
    DocumentType DocumentType,
    Guid? PartnerId,
    DateOnly DocumentDate,
    DateOnly? ServiceDate,
    string? Notes,
    string? BuyerReference,
    IReadOnlyList<SalesLineRequest> Lines);

/// <summary>
/// One requested line (BG-25). The fields are a SNAPSHOT the client supplies (usually
/// sourced from the CATL-02 picker); <see cref="CatalogItemId"/> is provenance only.
/// </summary>
public sealed record SalesLineRequest(
    Guid? CatalogItemId,
    string Name,
    string? Description,
    decimal Quantity,
    string UnitCode,
    decimal NetUnitPrice,
    TaxCategory TaxCategory,
    decimal VatRatePercent);

/// <summary>Body of <c>POST /api/documents/{id}/convert</c> — the target kind of the new draft.</summary>
public sealed record ConvertDocumentRequest(DocumentType TargetType);

/// <summary>Compact projection for the paged document list (GET /api/documents).</summary>
public sealed record SalesDocumentListItem(
    Guid Id,
    DocumentType DocumentType,
    DocumentStatus Status,
    string? DocumentNumber,
    Guid? PartnerId,
    DateOnly DocumentDate,
    decimal TotalGross,
    string Currency);

/// <summary>Full document projection for the detail view (GET /api/documents/{id}).</summary>
public sealed record SalesDocumentDetail(
    Guid Id,
    DocumentType DocumentType,
    DocumentStatus Status,
    string? DocumentNumber,
    Guid? PartnerId,
    DateOnly DocumentDate,
    DateOnly? ServiceDate,
    DateOnly? ServicePeriodEnd,
    DateOnly? DueDate,
    string Currency,
    decimal TotalNet,
    decimal TotalTax,
    decimal TotalGross,
    decimal AmountDue,
    bool IsKleinunternehmer,
    bool ReverseCharge,
    string? BuyerReference,
    string? Notes,
    Guid? SourceDocumentId,
    Guid? CorrectsDocumentId,
    Guid? CancelledByDocumentId,
    IReadOnlyList<SalesLineDto> Lines,
    IReadOnlyList<SalesTaxBreakdownDto> TaxBreakdown);

/// <summary>A line of a document detail (BG-25).</summary>
public sealed record SalesLineDto(
    Guid Id,
    int LineNumber,
    Guid? CatalogItemId,
    string Name,
    string? Description,
    decimal Quantity,
    string UnitCode,
    decimal NetUnitPrice,
    decimal LineNetAmount,
    TaxCategory TaxCategory,
    decimal VatRatePercent);

/// <summary>A VAT breakdown row of a document detail (BG-23).</summary>
public sealed record SalesTaxBreakdownDto(
    Guid Id,
    TaxCategory TaxCategory,
    decimal VatRatePercent,
    decimal TaxableBase,
    decimal TaxAmount,
    string? ExemptionReasonCode,
    string? ExemptionReasonText);
