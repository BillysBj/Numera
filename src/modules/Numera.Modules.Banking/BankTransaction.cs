using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Banking;

/// <summary>A normalized bank transaction awaiting or carrying a human-confirmed match.</summary>
[Table("bank_transaction")]
[Index(nameof(TenantId), nameof(BankAccountId), nameof(DedupeKey), IsUnique = true)]
[Index(nameof(TenantId), nameof(MatchStatus))]
[Index(nameof(TenantId), nameof(ValueDate))]
public sealed class BankTransaction : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Account to which this transaction belongs.</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>Account navigation declaring the parent foreign key.</summary>
    [ForeignKey(nameof(BankAccountId))]
    public BankAccount BankAccount { get; set; } = null!;

    /// <summary>Stable provider identifier or content fingerprint.</summary>
    public required string DedupeKey { get; set; }

    /// <summary>Transaction source format/provider.</summary>
    public BankTransactionSource Source { get; set; }

    /// <summary>Signed amount: positive is incoming, negative is outgoing.</summary>
    [Precision(19, 4)]
    public decimal Amount { get; set; }

    /// <summary>Bank value date.</summary>
    public DateOnly ValueDate { get; set; }

    /// <summary>Bank booking date, when supplied.</summary>
    public DateOnly? BookingDate { get; set; }

    /// <summary>Verwendungszweck.</summary>
    public string? Purpose { get; set; }

    /// <summary>Counterparty display name.</summary>
    public string? CounterpartyName { get; set; }

    /// <summary>Counterparty IBAN.</summary>
    public string? CounterpartyIban { get; set; }

    /// <summary>End-to-end payment identifier.</summary>
    public string? EndToEndId { get; set; }

    /// <summary>Human-controlled reconciliation lifecycle state.</summary>
    public MatchStatus MatchStatus { get; set; } = MatchStatus.Unmatched;

    /// <summary>Ranking confidence for a proposed match.</summary>
    [Precision(5, 4)]
    public decimal? ConfidenceScore { get; set; }

    /// <summary>Payment created when a receivable match is confirmed.</summary>
    public Guid? MatchedPaymentId { get; set; }

    /// <summary>Creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
