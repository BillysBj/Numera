using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Modules.Banking;

/// <summary>A tenant's bank connection and its PSD2 consent lifecycle.</summary>
[Table("bank_connection")]
public sealed class BankConnection : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Connection implementation backing this row.</summary>
    public BankProvider Provider { get; set; } = BankProvider.Stub;

    /// <summary>Encrypted finAPI sub-user identifier; plaintext is never persisted.</summary>
    public string? FinApiUserId { get; set; }

    /// <summary>Encrypted finAPI sub-user secret; plaintext is never persisted.</summary>
    public string? FinApiUserSecret { get; set; }

    /// <summary>Encrypted finAPI access token; plaintext is never persisted.</summary>
    public string? AccessTokenCipher { get; set; }

    /// <summary>Current PSD2 consent state.</summary>
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Pending;

    /// <summary>When the recurring consent expires, if known.</summary>
    public DateTimeOffset? ConsentExpiresAt { get; set; }

    /// <summary>finAPI Web Form identifier for an active import or re-auth flow.</summary>
    public string? WebFormId { get; set; }

    /// <summary>Status reported for the active finAPI Web Form.</summary>
    public string? WebFormStatus { get; set; }

    /// <summary>When transactions were last synchronized successfully.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
