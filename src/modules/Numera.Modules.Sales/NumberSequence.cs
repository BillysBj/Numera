using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Modules.Sales;

/// <summary>
/// A per-tenant, per-series numbering counter (RESEARCH.md Pattern 3). One row per
/// <c>(tenant, doc_type, year)</c>; the finalize transaction assigns a number via an
/// atomic <c>INSERT … ON CONFLICT … DO UPDATE … RETURNING</c> on this row — never a
/// Postgres SEQUENCE (burns numbers on rollback, not per-tenant) and never MAX()+1
/// (read-modify-write race). The unique <c>(tenant_id, doc_type, year)</c> index is
/// added in the migration.
/// </summary>
/// <remarks>
/// German law requires einmalig + nachvollziehbar (unique + traceable), NOT lückenlos
/// (gapless): there is deliberately no gap-backfill logic. Number reuse is made
/// impossible by the partial unique index on <c>sales_documents.document_number</c>.
/// </remarks>
[Table("number_sequences")]
public sealed class NumberSequence : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The document type this series numbers (<see cref="DocumentType"/> ordinal).</summary>
    public int DocType { get; set; }

    /// <summary>Reset boundary: the calendar year for an annual series; 0 = never-reset continuous series.</summary>
    public int Year { get; set; }

    /// <summary>The next number this series will assign.</summary>
    public long NextValue { get; set; }
}
