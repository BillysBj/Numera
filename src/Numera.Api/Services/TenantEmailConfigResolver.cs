using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Numera.Modules.Sales.Email;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

public interface ITenantEmailConfigResolver
{
    Task<EmailOptions> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolve at send time, after the job has established its tenant context.</summary>
public sealed class TenantEmailConfigResolver(
    NumeraDbContext db,
    ICurrentTenant tenant,
    IOptions<EmailOptions> fallback,
    SmtpPasswordProtector passwords) : ITenantEmailConfigResolver
{
    public async Task<EmailOptions> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is null)
        {
            throw new InvalidOperationException("A tenant context is required to send email.");
        }

        var settings = await db.Set<TenantEmailSettings>().AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings?.Host))
        {
            return fallback.Value;
        }

        // A configured tenant uses only its own credentials. Invalid ciphertext fails closed.
        return new EmailOptions
        {
            Host = settings.Host,
            Port = settings.Port,
            UseSsl = settings.UseSsl,
            Username = settings.Username,
            Password = string.IsNullOrEmpty(settings.PasswordCiphertext)
                ? null : passwords.Unprotect(settings.PasswordCiphertext),
            FromAddress = settings.FromAddress!,
            FromName = settings.FromName ?? string.Empty,
        };
    }
}
