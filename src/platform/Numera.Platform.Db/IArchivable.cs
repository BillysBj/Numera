namespace Numera.Platform.Db;

/// <summary>
/// Marker for entities that support soft-delete / archival. An archived row is
/// hidden from ordinary queries by the named <c>"NotArchived"</c> query filter
/// (see <see cref="NumeraDbContext"/>) but is never physically removed.
/// </summary>
/// <remarks>
/// Archival is an <b>application-level</b> concern only. The database RLS policies
/// (plan 01-02) do NOT filter archived rows: an archived row is still the tenant's
/// own data (RESEARCH.md Pattern 1), so tenant isolation and archival are orthogonal
/// controls. Reveal archived rows by disabling only the archive filter
/// (<c>IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])</c>) while the tenant
/// filter — and, above all, RLS — stay in force.
/// </remarks>
public interface IArchivable
{
    /// <summary>UTC instant this row was archived (soft-deleted), or null if active.</summary>
    DateTimeOffset? ArchivedAt { get; set; }
}
