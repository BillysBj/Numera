using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>
/// An append-only (GoBD-style) file attached to a <see cref="BusinessPartner"/>.
/// Persist new files via <c>db.Add(...)</c> and never mutate or delete them; list
/// queries must project metadata only so the <see cref="Bytes"/> are not selected.
/// </summary>
[Table("customer_files")]
[Index(nameof(TenantId), nameof(PartnerId), nameof(UploadedAt))]
public sealed class CustomerFile : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning partner.</summary>
    public Guid PartnerId { get; init; }

    /// <summary>Immutable file contents stored as PostgreSQL bytea.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Original upload file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Validated MIME content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size of <see cref="Bytes"/> in bytes, denormalized for listings.</summary>
    public long ByteSize { get; init; }

    /// <summary>User who uploaded the file, when available.</summary>
    public Guid? UploadedByUserId { get; init; }

    /// <summary>Upload instant.</summary>
    public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.UtcNow;
}
