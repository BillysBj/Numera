using System.Text.RegularExpressions;

using FluentValidation;

using Numera.Api.Contracts;

namespace Numera.Api.Validators;

/// <summary>
/// Server-side validation for the issuer company-profile write request (plan 03-01),
/// enforcing the §14 UStG issuer minimum: a legal name, a billing address, and EXACTLY
/// ONE tax identity (USt-IdNr <b>or</b> Steuernummer). A supplied German USt-IdNr is
/// shape-checked offline (DE + 9 digits) — full ISO 7064 checksum validation lives in the
/// CRM module and is deliberately NOT reused here to keep the Sales surface module-isolated.
/// </summary>
public sealed partial class UpdateCompanyProfileRequestValidator : AbstractValidator<UpdateCompanyProfileRequest>
{
    /// <summary>Configures the §14 issuer validation rules.</summary>
    public UpdateCompanyProfileRequestValidator()
    {
        RuleFor(x => x.LegalName)
            .NotEmpty().WithMessage("A legal name is required.")
            .MaximumLength(200);

        // Billing address (BG-5): the invoice issuer must have a printable address.
        RuleFor(x => x.Address)
            .NotNull().WithMessage("A billing address is required.");

        When(x => x.Address is not null, () =>
        {
            RuleFor(x => x.Address.Street)
                .NotEmpty().WithName("Address.Street").WithMessage("A street is required.");
            RuleFor(x => x.Address.PostalCode)
                .NotEmpty().WithName("Address.PostalCode").WithMessage("A postal code is required.");
            RuleFor(x => x.Address.City)
                .NotEmpty().WithName("Address.City").WithMessage("A city is required.");
            RuleFor(x => x.Address.CountryCode)
                .NotEmpty().Length(2).WithName("Address.CountryCode")
                .WithMessage("Country must be a 2-letter ISO 3166-1 alpha-2 code.");
        });

        // §14 requires a tax identity: EXACTLY ONE of USt-IdNr / Steuernummer.
        RuleFor(x => x)
            .Must(HaveExactlyOneTaxId)
            .WithName("VatId")
            .WithMessage("Provide exactly one of USt-IdNr (VatId) or Steuernummer (TaxNumber).");

        // A supplied German USt-IdNr must at least match the offline DE shape.
        RuleFor(x => x.VatId!)
            .Must(v => GermanVatFormat().IsMatch(v.Replace(" ", string.Empty).ToUpperInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.VatId)
                && x.VatId.Replace(" ", string.Empty).StartsWith("DE", StringComparison.OrdinalIgnoreCase))
            .WithName("VatId")
            .WithMessage("A German USt-IdNr must be 'DE' followed by 9 digits.");

        RuleFor(x => x.DefaultPaymentTermsNetDays!.Value)
            .InclusiveBetween(0, 365)
            .When(x => x.DefaultPaymentTermsNetDays.HasValue)
            .WithName("DefaultPaymentTermsNetDays")
            .WithMessage("Payment terms must be between 0 and 365 days.");

        RuleFor(x => x.InvoiceFooterText)
            .MaximumLength(2000);

        RuleFor(x => x.DeliveryNoteFooterText)
            .MaximumLength(2000);
    }

    private static bool HaveExactlyOneTaxId(UpdateCompanyProfileRequest x)
    {
        var hasVat = !string.IsNullOrWhiteSpace(x.VatId);
        var hasTax = !string.IsNullOrWhiteSpace(x.TaxNumber);
        return hasVat ^ hasTax;
    }

    [GeneratedRegex(@"^DE[1-9]\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex GermanVatFormat();
}
