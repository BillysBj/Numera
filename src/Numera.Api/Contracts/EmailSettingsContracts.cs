using Numera.Modules.Sales.Email;

namespace Numera.Api.Contracts;

// Explicit read model: neither plaintext nor ciphertext belongs on the wire.
public sealed record EmailSettingsDto(
    string? Host, int Port, bool UseSsl, string? Username, bool HasPassword,
    string? FromAddress, string? FromName,
    string? InvoiceSubject, string? InvoiceBody, string? DunningSubject, string? DunningBody)
{
    public static EmailSettingsDto FromEntity(TenantEmailSettings settings) => new(
        settings.Host, settings.Port, settings.UseSsl, settings.Username,
        !string.IsNullOrEmpty(settings.PasswordCiphertext), settings.FromAddress, settings.FromName,
        settings.InvoiceSubject, settings.InvoiceBody, settings.DunningSubject, settings.DunningBody);
}

// Class (not record) so the generated ToString cannot inadvertently print the password.
public sealed class UpdateEmailSettingsRequest
{
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? InvoiceSubject { get; init; }
    public string? InvoiceBody { get; init; }
    public string? DunningSubject { get; init; }
    public string? DunningBody { get; init; }
}

public sealed record TestEmailRequest(string? ToAddress);
public sealed record TestEmailResult(bool Success, string? Error = null);
