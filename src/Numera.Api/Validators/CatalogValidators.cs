using FluentValidation;

using Numera.Api.Contracts;
using Numera.Modules.Catalog;

namespace Numera.Api.Validators;

/// <summary>
/// Server-side validation for catalog write requests (plan 02-05). Mirrors the DB
/// constraints (article-number/name lengths, numeric precision) and the domain rule
/// that a unit code must be a curated UN/ECE Rec 20 code (EN 16931 BT-130,
/// <see cref="UnitOfMeasure.IsValid"/>). Money fields are exact decimals; a blank unit
/// is allowed here because the endpoint defaults it via
/// <see cref="UnitOfMeasure.DefaultFor"/> for the item kind.
/// </summary>
internal static class CatalogRules
{
    /// <summary>Shared write rules for create/update catalog requests.</summary>
    public static void Apply<T>(
        AbstractValidator<T> v,
        Func<T, string> itemNumber,
        Func<T, string> name,
        Func<T, string?> unitCode,
        Func<T, decimal> netPrice,
        Func<T, string> currency,
        Func<T, decimal?> vatRatePercent,
        Func<T, decimal?> costPrice)
    {
        v.RuleFor(x => itemNumber(x))
            .NotEmpty().WithName("ItemNumber").WithMessage("An article number is required.")
            .MaximumLength(64);

        v.RuleFor(x => name(x))
            .NotEmpty().WithName("Name").WithMessage("An article name is required.")
            .MaximumLength(200);

        // A blank unit is defaulted by the endpoint; a supplied unit must be curated.
        v.RuleFor(x => unitCode(x)!)
            .Must(UnitOfMeasure.IsValid)
            .When(x => !string.IsNullOrWhiteSpace(unitCode(x)))
            .WithName("UnitCode")
            .WithMessage("Unit code must be a curated UN/ECE Rec 20 code (EN 16931 BT-130).");

        v.RuleFor(x => netPrice(x))
            .GreaterThanOrEqualTo(0m).WithName("NetPrice")
            .WithMessage("Net price must not be negative.");

        v.RuleFor(x => currency(x))
            .NotEmpty().Length(3).WithName("Currency")
            .WithMessage("Currency must be a 3-letter ISO 4217 code.");

        v.RuleFor(x => vatRatePercent(x)!.Value)
            .InclusiveBetween(0m, 100m)
            .When(x => vatRatePercent(x).HasValue)
            .WithName("VatRatePercent")
            .WithMessage("VAT rate must be between 0 and 100 percent.");

        v.RuleFor(x => costPrice(x)!.Value)
            .GreaterThanOrEqualTo(0m)
            .When(x => costPrice(x).HasValue)
            .WithName("CostPrice")
            .WithMessage("Cost price must not be negative.");
    }
}

/// <summary>Validates <see cref="CreateCatalogItemRequest"/>.</summary>
public sealed class CreateCatalogItemRequestValidator : AbstractValidator<CreateCatalogItemRequest>
{
    /// <summary>Configures the create rules.</summary>
    public CreateCatalogItemRequestValidator() => CatalogRules.Apply(
        this,
        x => x.ItemNumber, x => x.Name, x => x.UnitCode,
        x => x.NetPrice, x => x.Currency, x => x.VatRatePercent, x => x.CostPrice);
}

/// <summary>Validates <see cref="UpdateCatalogItemRequest"/> (identical rule set to create).</summary>
public sealed class UpdateCatalogItemRequestValidator : AbstractValidator<UpdateCatalogItemRequest>
{
    /// <summary>Configures the update rules.</summary>
    public UpdateCatalogItemRequestValidator() => CatalogRules.Apply(
        this,
        x => x.ItemNumber, x => x.Name, x => x.UnitCode,
        x => x.NetPrice, x => x.Currency, x => x.VatRatePercent, x => x.CostPrice);
}
