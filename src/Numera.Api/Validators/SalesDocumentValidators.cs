using FluentValidation;

using Numera.Api.Contracts;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Money;
using Numera.Platform.Money;

namespace Numera.Api.Validators;

/// <summary>
/// The §14 UStG completeness gate applied at finalize (plan 03-05). Unlike the request-body
/// validators below, this checks the LOADED aggregate against the issuer
/// <see cref="CompanyProfile"/> and the recipient <see cref="BusinessPartner"/> — finalize
/// takes only an id, so there is no request body to validate. Blocks finalization of a
/// document that could not produce a legal invoice (RESEARCH.md §14 table + Pitfall 4).
/// </summary>
internal static class FinalizeValidation
{
    /// <summary>
    /// Returns the §14 completeness failures keyed by field group; an empty dictionary means
    /// the document may be finalized.
    /// </summary>
    /// <param name="doc">The tracked draft (with its lines loaded).</param>
    /// <param name="profile">The issuer company profile, or null when none exists yet.</param>
    /// <param name="partner">The recipient partner, or null when none is set/found.</param>
    public static Dictionary<string, string[]> Check(
        SalesDocument doc,
        CompanyProfile? profile,
        BusinessPartner? partner)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = [];
                errors[key] = list;
            }

            list.Add(message);
        }

        // --- Issuer (BG-4/BG-5 + tax identity) -------------------------------
        if (profile is null)
        {
            Add("Issuer", "An issuer company profile is required before finalizing (§14 UStG).");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profile.LegalName))
            {
                Add("Issuer", "The issuer legal name is required (§14 UStG, BT-27).");
            }

            var hasVatId = !string.IsNullOrWhiteSpace(profile.VatId);
            var hasTaxNumber = !string.IsNullOrWhiteSpace(profile.TaxNumber);
            if (!hasVatId && !hasTaxNumber)
            {
                Add("Issuer", "At least one of the issuer VAT ID or tax number is required (§14 UStG, BT-31/BT-32).");
            }

            if (!IsAddressComplete(profile.Address?.Street, profile.Address?.PostalCode, profile.Address?.City))
            {
                Add("Issuer", "A complete issuer address (street, postal code, city) is required (§14 UStG, BG-5).");
            }
        }

        // --- Lines -----------------------------------------------------------
        if (doc.Lines.Count == 0)
        {
            Add("Lines", "A document must have at least one line before finalizing.");
        }

        // --- Recipient (BG-7/BG-8) -------------------------------------------
        if (partner is null)
        {
            Add("Recipient", "A recipient partner is required before finalizing (§14 UStG, BG-7).");
        }
        else if (!IsAddressComplete(partner.BillingAddress?.Street, partner.BillingAddress?.PostalCode, partner.BillingAddress?.City))
        {
            Add("Recipient", "A complete recipient billing address (street, postal code, city) is required (§14 UStG, BG-8).");
        }

        // --- AE/K lines require the recipient VAT ID (§14a / §6a) ------------
        var requiresRecipientVatId = doc.Lines.Any(l => l.TaxCategory is TaxCategory.AE or TaxCategory.K);
        if (requiresRecipientVatId && string.IsNullOrWhiteSpace(partner?.VatId))
        {
            Add("Recipient", "Reverse-charge (AE) or intra-EU (K) lines require the recipient VAT ID (§14a UStG / §6a UStG).");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static bool IsAddressComplete(string? street, string? postalCode, string? city)
        => !string.IsNullOrWhiteSpace(street)
        && !string.IsNullOrWhiteSpace(postalCode)
        && !string.IsNullOrWhiteSpace(city);
}

/// <summary>
/// Server-side validation for sales-document write requests (plan 03-04). A draft must
/// carry at least one line, and each line must be well-formed (a snapshot name, a positive
/// quantity, a non-negative net unit price, a unit code, and a VAT rate in [0,100]).
/// Auto-scanned by <c>AddValidatorsFromAssemblyContaining&lt;Program&gt;()</c> — no DI edit.
/// </summary>
internal static class SalesDocumentRules
{
    /// <summary>Shared write rules for create/update sales-document requests.</summary>
    public static void Apply<T>(
        AbstractValidator<T> v,
        Func<T, IReadOnlyList<SalesLineRequest>?> lines,
        Func<T, string?> currency,
        Func<T, decimal?> exchangeRate,
        Func<T, DateOnly?> exchangeRateDate)
    {
        v.RuleFor(x => lines(x))
            .NotNull().WithName("Lines").WithMessage("A document must have at least one line.")
            .Must(l => l is { Count: > 0 }).WithName("Lines")
            .WithMessage("A document must have at least one line.");

        v.RuleForEach(x => lines(x))
            .SetValidator(new SalesLineRequestValidator())
            .OverridePropertyName("Lines")
            .When(x => lines(x) is not null);

        v.RuleFor(x => currency(x))
            .Must(code => string.IsNullOrWhiteSpace(code) || CurrencyScope.IsSupported(code))
            .WithName("Currency")
            .WithMessage(CurrencyScope.UnsupportedReason);

        v.RuleFor(x => exchangeRate(x))
            .NotNull().WithName("ExchangeRate").WithMessage("Ein positiver Wechselkurs ist erforderlich.")
            .GreaterThan(0m).WithName("ExchangeRate").WithMessage("Ein positiver Wechselkurs ist erforderlich.")
            .When(x => IsForeignCurrency(currency(x)));

        v.RuleFor(x => exchangeRateDate(x))
            .NotNull().WithName("ExchangeRateDate").WithMessage("Ein Wechselkursdatum ist erforderlich.")
            .When(x => IsForeignCurrency(currency(x)));

        v.RuleFor(x => exchangeRate(x))
            .Must(rate => rate is null or 1m)
            .WithName("ExchangeRate")
            .WithMessage("Für EUR muss der Wechselkurs leer oder 1 sein.")
            .When(x => !IsForeignCurrency(currency(x)));
    }

    private static bool IsForeignCurrency(string? currency)
        => !string.IsNullOrWhiteSpace(currency)
        && !string.Equals(currency.Trim(), "EUR", StringComparison.OrdinalIgnoreCase);
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

        RuleFor(x => x.DiscountPercent)
            .GreaterThanOrEqualTo(0m).LessThanOrEqualTo(100m)
            .WithMessage("Discount must be between 0 and 100 percent.");

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
    public CreateSalesDocumentRequestValidator() => SalesDocumentRules.Apply(
        this, x => x.Lines, x => x.Currency, x => x.ExchangeRate, x => x.ExchangeRateDate);
}

/// <summary>Validates <see cref="UpdateSalesDocumentRequest"/> (identical rule set to create).</summary>
public sealed class UpdateSalesDocumentRequestValidator : AbstractValidator<UpdateSalesDocumentRequest>
{
    /// <summary>Configures the update rules.</summary>
    public UpdateSalesDocumentRequestValidator() => SalesDocumentRules.Apply(
        this, x => x.Lines, x => x.Currency, x => x.ExchangeRate, x => x.ExchangeRateDate);
}
