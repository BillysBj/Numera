using FluentValidation;

using Numera.Api.Contracts;

namespace Numera.Api.Validators;

public sealed class UpdateEmailSettingsValidator : AbstractValidator<UpdateEmailSettingsRequest>
{
    public UpdateEmailSettingsValidator()
    {
        RuleFor(x => x.Host).MaximumLength(253);
        When(x => !string.IsNullOrWhiteSpace(x.Host), () =>
        {
            RuleFor(x => x.Host).Must(host => Uri.CheckHostName(host!.Trim()) != UriHostNameType.Unknown)
                .WithMessage("Enter an SMTP hostname or IP address (without a scheme or port).");
            RuleFor(x => x.Port).InclusiveBetween(1, 65535);
            RuleFor(x => x.FromAddress).NotEmpty().Must(EmailAddressValidation.IsValid)
                .WithMessage("Enter a valid sender email address.");
        });
        RuleFor(x => x.Username).MaximumLength(320);
        RuleFor(x => x.Password).MaximumLength(4096);
        RuleFor(x => x.FromAddress).MaximumLength(320);
        RuleFor(x => x.FromName).MaximumLength(200).Must(NoNewlines);
        RuleFor(x => x.InvoiceSubject).MaximumLength(500).Must(NoNewlines);
        RuleFor(x => x.DunningSubject).MaximumLength(500).Must(NoNewlines);
        RuleFor(x => x.InvoiceBody).MaximumLength(50000);
        RuleFor(x => x.DunningBody).MaximumLength(50000);
    }

    private static bool NoNewlines(string? value) => value is null || value.IndexOfAny(['\r', '\n']) < 0;
}

internal static class EmailAddressValidation
{
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 320 && value.IndexOfAny(['\r', '\n']) < 0
        && System.Net.Mail.MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value;
}
