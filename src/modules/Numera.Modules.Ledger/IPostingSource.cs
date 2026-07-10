namespace Numera.Modules.Ledger;

/// <summary>
/// Implemented by anything that can be expressed as a balanced set of ledger
/// postings (e.g. an invoice, a payment). This is the stable seam through which
/// later modules feed the ledger without the ledger knowing about them.
/// </summary>
/// <remarks>
/// Declared inert in Phase 1: there is no implementation and no balancing engine.
/// Implementers (invoicing, in a later milestone) are responsible for returning a
/// set of postings that satisfies the double-entry invariant documented on
/// <see cref="Posting"/> (debits equal credits).
/// </remarks>
public interface IPostingSource
{
    /// <summary>Builds the balanced postings representing this source's booking.</summary>
    IReadOnlyList<Posting> BuildPostings();
}
