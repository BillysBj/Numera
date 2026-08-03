using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>Persists balanced journal entries on the caller's context and transaction.</summary>
public sealed class PostingEngine(NumeraDbContext db)
{
    /// <summary>
    /// Builds and persists a posting set after enforcing the domain balance invariant.
    /// This method deliberately does not open or commit a transaction.
    /// </summary>
    public async Task<JournalEntry> PostAsync(
        IPostingSource source,
        JournalEntry header,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(header);

        var postings = source.BuildPostings();
        var debit = postings
            .Where(posting => posting.Direction == PostingDirection.Debit)
            .Sum(posting => posting.Amount);
        var credit = postings
            .Where(posting => posting.Direction == PostingDirection.Credit)
            .Sum(posting => posting.Amount);

        if (debit != credit)
        {
            throw new LedgerImbalanceException(debit, credit);
        }

        if (header.Postings.Count > 0)
        {
            throw new InvalidOperationException("The journal entry header already contains postings.");
        }

        foreach (var posting in postings)
        {
            if (posting.Amount < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(source), posting.Amount, "Posting amounts must be non-negative.");
            }

            if (posting.TenantId != header.TenantId)
            {
                throw new InvalidOperationException(
                    $"Posting tenant '{posting.TenantId}' does not match journal tenant '{header.TenantId}'.");
            }

            posting.JournalEntryId = header.Id;
            header.Postings.Add(posting);
        }

        db.Add(header);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return header;
    }
}

/// <summary>Raised before persistence when total Soll and Haben differ.</summary>
public sealed class LedgerImbalanceException(decimal debit, decimal credit)
    : InvalidOperationException($"Journal entry is unbalanced: Soll {debit} <> Haben {credit}.")
{
    /// <summary>Total debit amount.</summary>
    public decimal Debit { get; } = debit;

    /// <summary>Total credit amount.</summary>
    public decimal Credit { get; } = credit;
}
