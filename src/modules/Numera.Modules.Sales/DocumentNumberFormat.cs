using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Modules.Sales;

/// <summary>
/// Per-tenant, per-doc-type numbering format configuration (RESEARCH.md Pattern 3,
/// INV-02). Drives how a <see cref="NumberSequence"/> value renders into a document
/// number: <c>{prefix}{YYYY?-}{seq:0{padding}}</c> → e.g. <c>RE-2026-00001</c>. One row
/// per <c>(tenant, doc_type)</c>, enforced by a unique index in the migration.
/// </summary>
[Table("document_number_formats")]
public sealed class DocumentNumberFormat : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The document type this format applies to (<see cref="DocumentType"/> ordinal).</summary>
    public int DocType { get; set; }

    /// <summary>Number prefix, e.g. <c>RE-</c>. Required.</summary>
    public required string Prefix { get; set; }

    /// <summary>When true the number embeds the year and the series resets annually.</summary>
    public bool IncludeYear { get; set; }

    /// <summary>Zero-pad width of the sequential part, e.g. 5 → 00001.</summary>
    public int Padding { get; set; }
}
