using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;

namespace Numera.Modules.Sales.Belege.Payments;

/// <summary>An append-only supplier payment; corrections are negative reversal payments.</summary>
[Table("supplier_payment")]
[Index(nameof(TenantId), nameof(ValueDate))]
public sealed class SupplierPayment : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    [Precision(19, 4)]
    public decimal Amount { get; init; }
    public DateOnly ValueDate { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Reference { get; init; }
    public Guid? ReversesPaymentId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}
