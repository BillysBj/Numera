using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Email;

/// <summary>Editable tenant SMTP configuration and plain-text email templates.</summary>
[Table("tenant_email_settings")]
[Index(nameof(TenantId), IsUnique = true)]
public sealed class TenantEmailSettings : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? PasswordCiphertext { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? InvoiceSubject { get; set; }
    public string? InvoiceBody { get; set; }
    public string? DunningSubject { get; set; }
    public string? DunningBody { get; set; }
}
