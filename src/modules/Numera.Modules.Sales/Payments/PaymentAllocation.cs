using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Payments;

/// <summary>
/// The join between a <see cref="Payment"/> and an <see cref="OpenItem"/> it settles, carrying the
/// amount of that payment applied to that open item (OPDN-02). The many-to-many split is what lets
/// one payment span several open items and one open item accumulate several partial payments
/// (Teilzahlungen).
/// </summary>
/// <remarks>
/// Append-only like its parent payment (the <c>payment_allocation_immutable</c> trigger blocks any
/// UPDATE/DELETE — corrections are reversing allocations with a negative
/// <see cref="AllocatedAmount"/>). Both ids are plain Guid FKs (provenance only — NO navigation);
/// persist via <c>db.Add(...)</c>, never a collection-navigation add.
/// </remarks>
[Table("payment_allocation")]
[Index(nameof(TenantId), nameof(OpenItemId))]
public sealed class PaymentAllocation : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The payment this allocation belongs to (FK → payment.id).</summary>
    public Guid PaymentId { get; init; }

    /// <summary>The open item this allocation settles (FK → open_items.id).</summary>
    public Guid OpenItemId { get; init; }

    /// <summary>
    /// The amount of the payment applied to the open item. Positive for a normal allocation;
    /// NEGATIVE for a reversal allocation (which restores the open item's open amount).
    /// </summary>
    [Precision(19, 4)]
    public decimal AllocatedAmount { get; init; }
}
