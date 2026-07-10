using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>
/// A single booking (Buchungssatz) grouping the postings of one economic event.
/// </summary>
/// <remarks>
/// Inert Phase 1 schema. The balancing invariant (postings sum to zero) is
/// documented on <see cref="Posting"/> but not enforced here — no posting engine
/// exists until a later milestone implements <see cref="IPostingSource"/>.
/// </remarks>
public sealed class JournalEntry : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The accounting date of the entry (Buchungsdatum).</summary>
    public DateOnly EntryDate { get; set; }

    /// <summary>Reference to the originating document/event (e.g. invoice id).</summary>
    public required string SourceRef { get; set; }

    /// <summary>Free-text description (Buchungstext).</summary>
    public required string Description { get; set; }

    /// <summary>The postings comprising this entry.</summary>
    public ICollection<Posting> Postings { get; init; } = [];
}
