using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>The unique, unguessable receipt-intake address provisioned for one tenant.</summary>
[Table("tenant_belege_mailbox")]
[Index(nameof(TenantId), IsUnique = true)]
[Index(nameof(AddressToken), IsUnique = true)]
public sealed class TenantBelegeMailbox : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>URL-safe 256-bit routing secret; never derived from tenant data.</summary>
    public required string AddressToken { get; init; }

    /// <summary>The local part exposed to the tenant, for example <c>belege-{token}</c>.</summary>
    public required string LocalPart { get; init; }

    /// <summary>Whether incoming messages may currently be routed to this tenant.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the intake address was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Creates a cryptographically random URL-safe token with 256 bits of entropy.</summary>
    public static string GenerateAddressToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Builds the canonical local part for a routing token.</summary>
    public static string BuildLocalPart(string token) => $"belege-{token}";
}

/// <summary>Per-tenant idempotency record for a successfully handled incoming message.</summary>
[Table("processed_belege_mail")]
[Index(nameof(TenantId), nameof(MessageId), IsUnique = true)]
public sealed class ProcessedBelegeMail : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The RFC Message-Id, or a stable synthetic id if the sender omitted it.</summary>
    public required string MessageId { get; init; }

    /// <summary>Composite SHA-256 of the supported attachment contents.</summary>
    public string? ContentHash { get; init; }

    /// <summary>When processing completed and the IMAP message became seen.</summary>
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
