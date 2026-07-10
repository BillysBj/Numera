namespace Numera.Platform.Audit;

/// <summary>
/// Appends immutable audit records. The single writer seam consumed by finance
/// modules to record who/what/when for every finance-relevant change.
/// </summary>
/// <remarks>
/// Implementations write <b>synchronously, in the caller's transaction</b>: the
/// audit row and the change it records commit or roll back together. Callers must
/// therefore invoke <see cref="RecordAsync"/> inside the same unit of work
/// (finalize/change transaction) as the change — never as fire-and-forget.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Records <paramref name="evt"/> as an audit row, stamped with the ambient
    /// tenant and actor, appended to the caller's <c>DbContext</c> so it is part of
    /// the caller's transaction. Does not call <c>SaveChanges</c> — the caller's
    /// existing save/commit persists it atomically with the recorded change.
    /// </summary>
    /// <param name="evt">The change to record.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAsync(IAuditEvent evt, CancellationToken ct);
}
