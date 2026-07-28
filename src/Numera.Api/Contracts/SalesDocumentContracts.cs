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
    IReadOnlyList<SalesLineRequest> Lines,
    string? Currency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null);

/// <summary>Payload to update an existing draft. Same shape as create (full replace of header + lines).</summary>
public sealed record UpdateSalesDocumentRequest(
    DocumentType DocumentType,
    Guid? PartnerId,
    DateOnly DocumentDate,
    DateOnly? ServiceDate,
    string? Notes,
    string? BuyerReference,
    IReadOnlyList<SalesLineRequest> Lines,
    string? Currency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null);

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

/// <summary>
/// Response of <c>POST /api/documents/{id}/storno</c> — the newly created Storno (EN 16931
/// type 384). Unlike a credit note, the Storno is finalized in the same request, so it
/// already carries its own <see cref="DocumentNumber"/> from the Storno series.
/// </summary>
public sealed record StornoResponse(Guid Id, string DocumentNumber);

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
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    decimal? TotalTaxEur,
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
    string? IssuerSnapshot,
    string? RecipientSnapshot,
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

/// <summary>
/// The body of <c>POST /api/documents/{id}/send</c> (plan 04-04, DOCS-03). Both fields are
/// optional: <see cref="ToAddress"/> defaults to the frozen recipient e-mail from the document's
/// RecipientSnapshot, and <see cref="Language"/> defaults to <c>de</c> (the label + covering-copy
/// language; the frozen §14 Pflichttexte stay German regardless).
/// </summary>
/// <param name="ToAddress">Override recipient address; null → the frozen recipient e-mail.</param>
/// <param name="Language">Send language (<c>de</c>/<c>en</c>); null → <c>de</c>.</param>
public sealed record SendDocumentEmailRequest(string? ToAddress, string? Language);
