using FluentValidation;

using Numera.Api.Contracts;
using Numera.Modules.Crm;

namespace Numera.Api.Validators;

/// <summary>
/// Server-side validation for partner write requests. Mirrors the DB constraints
/// (owned address NOT NULL columns, currency/country code lengths) and the domain
/// rule that a partner must hold at least one role. VAT-ID checks are OFFLINE only
/// (<see cref="VatId.IsPlausibleEu"/>) — a save is NEVER gated on VIES.
/// </summary>
internal static class AddressRules
{
    /// <summary>Shared address rules applied to the (required) billing address.</summary>
    public static void ApplyRequired<T>(AbstractValidator<T> validator, Func<T, AddressDto?> selector)
    {
        validator.RuleFor(x => selector(x))
            .NotNull()
            .WithMessage("A billing address is required.");

        validator.When(x => selector(x) is not null, () =>
        {
            validator.RuleFor(x => selector(x)!.Street)
                .NotEmpty().WithMessage("Street is required.").MaximumLength(200);
            validator.RuleFor(x => selector(x)!.PostalCode)
                .NotEmpty().WithMessage("Postal code is required.").MaximumLength(20);
            validator.RuleFor(x => selector(x)!.City)
                .NotEmpty().WithMessage("City is required.").MaximumLength(120);
            validator.RuleFor(x => selector(x)!.CountryCode)
                .NotEmpty().Length(2).WithMessage("Country code must be a 2-letter ISO code.");
        });
    }

    /// <summary>Shared rules for the optional shipping address (only when supplied).</summary>
    public static void ApplyOptional<T>(AbstractValidator<T> validator, Func<T, AddressDto?> selector)
    {
        validator.When(x => selector(x) is not null, () =>
        {
            validator.RuleFor(x => selector(x)!.Street)
                .NotEmpty().WithMessage("Shipping street is required when a shipping address is given.");
            validator.RuleFor(x => selector(x)!.PostalCode).NotEmpty();
            validator.RuleFor(x => selector(x)!.City).NotEmpty();
            validator.RuleFor(x => selector(x)!.CountryCode)
                .NotEmpty().Length(2).WithMessage("Country code must be a 2-letter ISO code.");
        });
    }
}

/// <summary>Validates <see cref="CreatePartnerRequest"/>.</summary>
public sealed class CreatePartnerRequestValidator : AbstractValidator<CreatePartnerRequest>
{
    /// <summary>Configures the create rules.</summary>
    public CreatePartnerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        AddressRules.ApplyRequired(this, x => x.BillingAddress);
        AddressRules.ApplyOptional(this, x => x.ShippingAddress);

        RuleFor(x => x)
            .Must(p => p.IsCustomer || p.IsSupplier)
            .WithName("Roles")
            .WithMessage("A partner must be a customer, a supplier, or both.");

        RuleFor(x => x.VatId!)
            .Must(VatId.IsPlausibleEu)
            .When(x => !string.IsNullOrWhiteSpace(x.VatId))
            .WithMessage("The VAT-ID is not a plausible EU VAT identifier.");

        RuleFor(x => x.SkontoPercent!.Value)
            .InclusiveBetween(0m, 100m)
            .When(x => x.SkontoPercent.HasValue)
            .WithName("SkontoPercent");

        RuleFor(x => x.DefaultCurrency)
            .NotEmpty().Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}

/// <summary>Validates <see cref="UpdatePartnerRequest"/> (identical rule set to create).</summary>
public sealed class UpdatePartnerRequestValidator : AbstractValidator<UpdatePartnerRequest>
{
    /// <summary>Configures the update rules.</summary>
    public UpdatePartnerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        AddressRules.ApplyRequired(this, x => x.BillingAddress);
        AddressRules.ApplyOptional(this, x => x.ShippingAddress);

        RuleFor(x => x)
            .Must(p => p.IsCustomer || p.IsSupplier)
            .WithName("Roles")
            .WithMessage("A partner must be a customer, a supplier, or both.");

        RuleFor(x => x.VatId!)
            .Must(VatId.IsPlausibleEu)
            .When(x => !string.IsNullOrWhiteSpace(x.VatId))
            .WithMessage("The VAT-ID is not a plausible EU VAT identifier.");

        RuleFor(x => x.SkontoPercent!.Value)
            .InclusiveBetween(0m, 100m)
            .When(x => x.SkontoPercent.HasValue)
            .WithName("SkontoPercent");

        RuleFor(x => x.DefaultCurrency)
            .NotEmpty().Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}

/// <summary>Validates <see cref="CreateContactRequest"/>.</summary>
public sealed class CreateContactRequestValidator : AbstractValidator<CreateContactRequest>
{
    /// <summary>Configures the contact rules.</summary>
    public CreateContactRequestValidator()
    {
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

/// <summary>Validates <see cref="CreateNoteRequest"/>.</summary>
public sealed class CreateNoteRequestValidator : AbstractValidator<CreateNoteRequest>
{
    /// <summary>Configures the note rules.</summary>
    public CreateNoteRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty();
    }
}
