using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>Whether an entry is a regular booking or a reversing entry.</summary>
public enum PostingType
{
    /// <summary>Regular booking.</summary>
    Normal = 0,

    /// <summary>Stornobuchung reversing a prior entry.</summary>
    Storno = 1,
}

/// <summary>The economic fact from which a journal entry was projected.</summary>
public enum LedgerSourceType
{
    /// <summary>Sales or supplier invoice.</summary>
    Invoice = 1,

    /// <summary>Incoming or outgoing payment.</summary>
    Payment = 2,

    /// <summary>Expense or receipt.</summary>
    Expense = 3,

    /// <summary>Manually initiated booking.</summary>
    Manual = 0,
}

/// <summary>
/// A single booking (Buchungssatz) grouping the postings of one economic event.
/// </summary>
/// <remarks>
/// Entries are append-only under the database's GoBD enforcement. The only
/// permitted update is the one-shot Festschreibung stamp.
/// </remarks>
public sealed class JournalEntry : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The accounting date of the entry (Buchungsdatum).</summary>
    public DateOnly EntryDate { get; set; }

    /// <summary>Gapless per-fiscal-year number, assigned at Festschreibung.</summary>
    public string? JournalNumber { get; set; }

    /// <summary>Optional fiscal period containing the accounting date.</summary>
    public Guid? PeriodId { get; set; }

    /// <summary>Reference to the originating document/event (e.g. invoice id).</summary>
    public required string SourceRef { get; set; }

    /// <summary>The type of source fact represented by this entry.</summary>
    public LedgerSourceType SourceType { get; set; }

    /// <summary>Free-text description (Buchungstext).</summary>
    public required string Description { get; set; }

    /// <summary>Whether this is a regular booking or a Stornobuchung.</summary>
    public PostingType PostingType { get; set; }

    /// <summary>The entry reversed by this Stornobuchung, when applicable.</summary>
    public Guid? ReversesEntryId { get; set; }

    /// <summary>When the journal number was permanently stamped.</summary>
    public DateTimeOffset? FestgeschriebenAt { get; set; }

    /// <summary>The postings comprising this entry.</summary>
    public ICollection<Posting> Postings { get; init; } = [];
}
