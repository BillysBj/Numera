using Numera.Modules.Crm;
using Numera.Platform.Money;

namespace Numera.Api.Contracts;

/// <summary>
/// Request/response DTOs for the partner management API (plan 02-04). Deliberately
/// carry only the RESEARCH.md "Build NOW" fields; the B2G / bank / DATEV seam columns
/// on <see cref="BusinessPartner"/> stay null in v1 (RESEARCH.md Open Question 2).
/// Money-adjacent fields are <see cref="decimal"/> — never <c>float</c>/<c>double</c>.
/// </summary>
/// <remarks>
/// A postal address (EN 16931 BG-8). Mirrors <see cref="Address"/> as an owned value.
/// </remarks>
public sealed record AddressDto(
    string Street,
    string? Line2,
    string PostalCode,
    string City,
    string CountryCode,
    string? PoBox);

/// <summary>Payload to create a new partner (CRM-01). At least one role must be set.</summary>
public sealed record CreatePartnerRequest(
    string Name,
    string? LegalForm,
    AddressDto BillingAddress,
    AddressDto? ShippingAddress,
    string? VatId,
    string? TaxNumber,
    string? Email,
    string? Phone,
    string? Website,
    int? PaymentTermsNetDays,
    decimal? SkontoPercent,
    int? SkontoDays,
    string DefaultCurrency,
    PartnerLanguage Language,
    TaxCategory? DefaultTaxCategory,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerNumber,
    string? SupplierNumber);

/// <summary>Payload to update an existing partner. Same shape as create (full replace).</summary>
public sealed record UpdatePartnerRequest(
    string Name,
    string? LegalForm,
    AddressDto BillingAddress,
    AddressDto? ShippingAddress,
    string? VatId,
    string? TaxNumber,
    string? Email,
    string? Phone,
    string? Website,
    int? PaymentTermsNetDays,
    decimal? SkontoPercent,
    int? SkontoDays,
    string DefaultCurrency,
    PartnerLanguage Language,
    TaxCategory? DefaultTaxCategory,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerNumber,
    string? SupplierNumber);

/// <summary>Compact projection for the paged partner list (GET /api/partners).</summary>
public sealed record PartnerListItem(
    Guid Id,
    string Name,
    bool IsCustomer,
    bool IsSupplier,
    string? City,
    bool Archived);

/// <summary>Full partner projection for the detail view (GET /api/partners/{id}).</summary>
public sealed record PartnerDetail(
    Guid Id,
    string Name,
    string? LegalForm,
    AddressDto BillingAddress,
    AddressDto? ShippingAddress,
    string? VatId,
    string? TaxNumber,
    string? Email,
    string? Phone,
    string? Website,
    int? PaymentTermsNetDays,
    decimal? SkontoPercent,
    int? SkontoDays,
    string DefaultCurrency,
    PartnerLanguage Language,
    TaxCategory? DefaultTaxCategory,
    bool IsCustomer,
    bool IsSupplier,
    string? CustomerNumber,
    string? SupplierNumber,
    bool Archived);

/// <summary>A partner contact person (EN 16931 BG-9).</summary>
public sealed record ContactDto(
    Guid Id,
    string? Salutation,
    string? FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Position,
    bool IsPrimary);

/// <summary>Payload to create or update a partner contact.</summary>
public sealed record CreateContactRequest(
    string? Salutation,
    string? FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Position,
    bool IsPrimary);

/// <summary>A free-text partner note (CRM-03).</summary>
public sealed record NoteDto(
    Guid Id,
    Guid AuthorUserId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Payload to create or update a partner note.</summary>
public sealed record CreateNoteRequest(string Body);

/// <summary>One entry on the partner activity timeline (CRM-02 read).</summary>
public sealed record ActivityDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    PartnerActivityType Type,
    string Summary,
    Guid? ActorUserId);
