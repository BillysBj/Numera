using Microsoft.AspNetCore.DataProtection;

namespace Numera.Api.Services.FinApi;

/// <summary>Protects finAPI sub-user credentials and access tokens before persistence.</summary>
public interface IBankCredentialProtector
{
    /// <summary>Encrypts a plaintext credential for storage.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a credential loaded from storage.</summary>
    string Unprotect(string protectedValue);
}

/// <summary>ASP.NET Core Data Protection implementation scoped to finAPI credentials.</summary>
public sealed class DataProtectionBankCredentialProtector : IBankCredentialProtector
{
    private const string Purpose = "finapi-credentials";
    private readonly IDataProtector _protector;

    /// <summary>Creates a purpose-isolated credential protector.</summary>
    public DataProtectionBankCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        return _protector.Unprotect(protectedValue);
    }
}
