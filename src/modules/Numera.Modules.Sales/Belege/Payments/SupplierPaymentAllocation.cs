using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Belege.Payments;

/// <summary>Append-only allocation to a receipt. Plain Guid links carry provenance without navigations.</summary>
[Table("supplier_payment_allocation")]
[Index(nameof(TenantId), nameof(ReceiptId))]
public sealed class SupplierPaymentAllocation : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid PaymentId { get; init; }
    public Guid ReceiptId { get; init; }
    [Precision(19, 4)]
    public decimal AllocatedAmount { get; init; }
}
