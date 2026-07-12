using Numera.Platform.Money;

namespace Numera.Api.Contracts;

/// <summary>
/// Request/response DTOs for the company-profile (issuer / Ausstellerstammdaten) settings
/// API (plan 03-01). This is the §14 UStG issuer master data — legal name, address, VAT/tax
/// identity and §19 status — that plan 03-05 snapshots onto finalized invoices. Enums cross
/// the wire as NUMBERS (per the Catalog convention); Money-adjacent fields stay absent here
/// (issuer defaults are terms/tax-category, not prices).
/// </summary>
/// <remarks>
/// The nested billing address reuses the shared <see cref="AddressDto"/> (defined in
/// <c>PartnerContracts</c>) — identical BG-5 shape, one namespace, no duplicate type.
/// </remarks>
/// <summary>
/// The tenant's issuer profile as returned by <c>GET /api/company-profile</c>. All fields are
/// nullable so the settings form gets an editable empty state when no profile exists yet
/// (first-write upsert via PUT). <see cref="Address"/> is null until the first save.
/// </summary>
public sealed record CompanyProfileDto(
    string? LegalName,
    AddressDto? Address,
    string? VatId,
    string? TaxNumber,
    bool IsKleinunternehmer,
    int? DefaultPaymentTermsNetDays,
    TaxCategory? DefaultTaxCategory,
    string? Iban,
    string? Bic,
    string? BankName,
    string? RegisterCourt,
    string? RegisterNumber,
    string? ManagingDirector,
    string? ContactEmail,
    string? ContactPhone,
    string? LogoRef);

/// <summary>
/// Payload to create-or-update the issuer profile via <c>PUT /api/company-profile</c> (upsert:
/// PUT creates on the first write, updates thereafter). Validation requires
/// <see cref="LegalName"/>, a billing <see cref="Address"/>, and EXACTLY ONE of
/// <see cref="VatId"/> / <see cref="TaxNumber"/> (§14 requires a tax identity).
/// </summary>
public sealed record UpdateCompanyProfileRequest(
    string LegalName,
    AddressDto Address,
    string? VatId,
    string? TaxNumber,
    bool IsKleinunternehmer,
    int? DefaultPaymentTermsNetDays,
    TaxCategory? DefaultTaxCategory,
    string? Iban,
    string? Bic,
    string? BankName,
    string? RegisterCourt,
    string? RegisterNumber,
    string? ManagingDirector,
    string? ContactEmail,
    string? ContactPhone,
    string? LogoRef);
