using Microsoft.AspNetCore.DataProtection;

namespace Numera.Api.Services;

/// <summary>The shared API/worker purpose for SMTP credentials; never log its inputs or outputs.</summary>
public sealed class SmtpPasswordProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("TenantEmailSettings.SmtpPassword");

    public string Protect(string password) => _protector.Protect(password);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
