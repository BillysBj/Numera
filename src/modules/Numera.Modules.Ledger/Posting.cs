using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

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
/// Inert Phase 1 schema. The core double-entry invariant — <b>the postings of a
/// single <see cref="JournalEntry"/> must sum to zero</b> (total debits equal total
/// credits, per matching VAT/currency scope) — is a documented target only. No
/// runtime balancing logic exists in Phase 1; invoicing will produce balanced
/// posting sets via <see cref="IPostingSource"/> in a later milestone.
/// </para>
/// <para>
/// <see cref="Amount"/> is <see cref="decimal"/> mapped to Postgres
/// <c>numeric(19,4)</c> — money is never <c>float</c>/<c>double</c>.
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
}
