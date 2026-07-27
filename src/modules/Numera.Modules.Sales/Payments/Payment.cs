using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Payments;

/// <summary>
/// A booked incoming payment (Zahlungseingang) — a GoBD booked fact (OPDN-02). Recording a
/// payment reduces the target <see cref="OpenItem.OpenAmount"/> and transitions its
/// <see cref="OpenItemStatus"/> via one or more <see cref="PaymentAllocation"/> rows; one payment
/// can settle several open items (a single bank transfer clearing several invoices) and one open
/// item can be settled by several payments (Teilzahlungen).
/// </summary>
/// <remarks>
/// <para>
/// GoBD: a recorded payment is a Buchung and therefore APPEND-ONLY — the DB immutability trigger
/// (see the <c>Payments</c> migration) blocks any UPDATE/DELETE. A correction is NOT an edit: it is
/// a REVERSAL payment carrying a negative <see cref="Amount"/> and a <see cref="ReversesPaymentId"/>
/// back-link, whose reversing allocations restore the open amount.
/// </para>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy (hand-written in the migration — reflective
/// discovery never emits policies) + the tenant query filter. The entity self-describes via
/// attributes so <c>NumeraDbContext</c> is not edited and no DbSet is added. Persist via
/// <c>db.Add(...)</c>, never a collection-navigation add (a client-set UUIDv7 PK child would
/// otherwise be tracked Modified → a 0-row UPDATE, the 03-11 bug).
/// </para>
/// </remarks>
[Table("payment")]
[Index(nameof(TenantId), nameof(ValueDate))]
public sealed class Payment : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>
    /// The received amount. Positive for a normal receipt; NEGATIVE for a reversal payment
    /// (a correction of a prior booked payment).
    /// </summary>
    [Precision(19, 4)]
    public decimal Amount { get; init; }

    /// <summary>Value date (Wertstellung) — when the money was received (BT-9-adjacent seam).</summary>
    public DateOnly ValueDate { get; init; }

    /// <summary>How the payment was received (descriptive only).</summary>
    public PaymentMethod Method { get; init; }

    /// <summary>Free reference (bank end-to-end reference, Verwendungszweck, or a note). Nullable.</summary>
    public string? Reference { get; init; }

    /// <summary>
    /// When this payment reverses a prior one, the id of that original payment; null for a normal
    /// receipt. The original is NEVER edited or deleted (the immutability trigger would raise) —
    /// the reversal is an independent append-only row.
    /// </summary>
    public Guid? ReversesPaymentId { get; init; }

    /// <summary>When the payment was recorded (booking timestamp).</summary>
    public DateTimeOffset RecordedAt { get; init; }
}
