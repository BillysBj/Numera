using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Ledger.Tax;

/// <summary>The direction of a cash settlement with the Finanzamt.</summary>
public enum VatPaymentKind
{
    Payment = 0,
    Refund = 1,
}

/// <summary>Append-only EÜR cash recognition; reversals retain a positive amount and use the opposite kind.</summary>
[Table("vat_payment")]
[Index(nameof(TenantId), nameof(ValueDate))]
[Index(nameof(TenantId), nameof(ReversesPaymentId), IsUnique = true)]
public sealed class VatPayment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    [Precision(19, 4)]
    public decimal Amount { get; init; }
    public VatPaymentKind Kind { get; init; }
    public DateOnly ValueDate { get; init; }
    public string? Reference { get; init; }
    public Guid? ReversesPaymentId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}
