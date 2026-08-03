using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Ledger;

/// <summary>Whether a posting increases the debit (Soll) or credit (Haben) side.</summary>
public enum PostingDirection
{
    /// <summary>Soll.</summary>
    Debit = 1,

    /// <summary>Haben.</summary>
    Credit = 2,
}

/// <summary>
/// One leg of a double-entry booking: a signed movement against a single account.
/// </summary>
/// <remarks>
/// <para>
/// Posting rows are strictly append-only. A deferred database constraint enforces
/// that the postings of a single <see cref="JournalEntry"/> have equal total debits
/// and credits before the transaction can commit.
/// </para>
/// <para>
/// <see cref="Amount"/> is <see cref="decimal"/> mapped to Postgres
/// <c>numeric(19,4)</c>; money is never <c>float</c>/<c>double</c>.
/// </para>
/// </remarks>
public sealed class Posting : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The owning <see cref="JournalEntry"/>.</summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>The <see cref="Account"/> this leg posts against.</summary>
    public Guid AccountId { get; set; }

    /// <summary>The posting amount, always non-negative; side is given by
    /// <see cref="Direction"/>. Stored as <c>numeric(19,4)</c>.</summary>
    [Column(TypeName = "numeric(19,4)")]
    public decimal Amount { get; set; }

    /// <summary>Whether this leg is a debit (Soll) or credit (Haben).</summary>
    public PostingDirection Direction { get; set; }

    /// <summary>The DATEV BU tax key captured when this leg was posted.</summary>
    public Steuerschluessel? Steuerschluessel { get; set; }

    /// <summary>The VAT rate captured when this leg was posted.</summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? TaxRatePercent { get; set; }

    /// <summary>The EN 16931 VAT category captured when this leg was posted.</summary>
    public TaxCategory? TaxCategory { get; set; }
}
