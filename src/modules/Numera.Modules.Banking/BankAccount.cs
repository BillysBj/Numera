using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Modules.Banking;

/// <summary>A tenant bank account synchronized from a provider or created for imports.</summary>
[Table("bank_account")]
public sealed class BankAccount : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Provider connection; null for import-only accounts.</summary>
    public Guid? BankConnectionId { get; set; }

    /// <summary>Optional provider connection navigation.</summary>
    [ForeignKey(nameof(BankConnectionId))]
    public BankConnection? BankConnection { get; set; }

    /// <summary>International Bank Account Number.</summary>
    public required string Iban { get; set; }

    /// <summary>User-facing account name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>ISO 4217 account currency.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>finAPI account identifier, when provider-backed.</summary>
    public string? FinApiAccountId { get; set; }

    /// <summary>Incremental synchronization cursor.</summary>
    public string? SyncCursor { get; set; }

    /// <summary>Creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
