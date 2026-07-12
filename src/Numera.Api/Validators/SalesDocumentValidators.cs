using FluentValidation;

using Numera.Api.Contracts;

namespace Numera.Api.Validators;

/// <summary>
/// Server-side validation for sales-document write requests (plan 03-04). A draft must
/// carry at least one line, and each line must be well-formed (a snapshot name, a positive
/// quantity, a non-negative net unit price, a unit code, and a VAT rate in [0,100]).
/// Auto-scanned by <c>AddValidatorsFromAssemblyContaining&lt;Program&gt;()</c> — no DI edit.
/// </summary>
internal static class SalesDocumentRules
{
    /// <summary>Shared write rules for create/update sales-document requests.</summary>
    public static void Apply<T>(AbstractValidator<T> v, Func<T, IReadOnlyList<SalesLineRequest>?> lines)
    {
        v.RuleFor(x => lines(x))
            .NotNull().WithName("Lines").WithMessage("A document must have at least one line.")
            .Must(l => l is { Count: > 0 }).WithName("Lines")
            .WithMessage("A document must have at least one line.");

        v.RuleForEach(x => lines(x))
            .SetValidator(new SalesLineRequestValidator())
            .When(x => lines(x) is not null);
    }
}

/// <summary>Validates a single <see cref="SalesLineRequest"/>.</summary>
public sealed class SalesLineRequestValidator : AbstractValidator<SalesLineRequest>
{
    /// <summary>Configures the per-line rules.</summary>
    public SalesLineRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A line name is required.")
            .MaximumLength(400);

        RuleFor(x => x.Quantity)
            .GreaterThan(0m).WithMessage("Line quantity must be greater than zero.");

        RuleFor(x => x.NetUnitPrice)
            .GreaterThanOrEqualTo(0m).WithMessage("Net unit price must not be negative.");

        RuleFor(x => x.UnitCode)
            .NotEmpty().WithMessage("A unit code is required.")
            .MaximumLength(16);

        RuleFor(x => x.VatRatePercent)
            .InclusiveBetween(0m, 100m).WithMessage("VAT rate must be between 0 and 100 percent.");
    }
}

/// <summary>Validates <see cref="CreateSalesDocumentRequest"/>.</summary>
public sealed class CreateSalesDocumentRequestValidator : AbstractValidator<CreateSalesDocumentRequest>
{
    /// <summary>Configures the create rules.</summary>
    public CreateSalesDocumentRequestValidator() => SalesDocumentRules.Apply(this, x => x.Lines);
}

/// <summary>Validates <see cref="UpdateSalesDocumentRequest"/> (identical rule set to create).</summary>
public sealed class UpdateSalesDocumentRequestValidator : AbstractValidator<UpdateSalesDocumentRequest>
{
    /// <summary>Configures the update rules.</summary>
    public UpdateSalesDocumentRequestValidator() => SalesDocumentRules.Apply(this, x => x.Lines);
}
