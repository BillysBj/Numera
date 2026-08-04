using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Belege;

/// <summary>
/// The append-only GoBD archive for a Tier-B receipt's original artifact. The original bytes and
/// metadata are retained for ten years; corrections create a new archive row and never alter or
/// delete an existing one.
/// </summary>
/// <remarks>
/// Persist with <c>db.Add(...)</c>. Database privileges and an immutability trigger enforce WORM
/// storage independently of these init-only CLR properties.
/// </remarks>
[Table("receipt_archive")]
[Index(nameof(TenantId), nameof(ContentHash))]
public sealed class ReceiptArchive : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning receipt (plain Guid provenance link to <c>receipt.id</c>).</summary>
    public Guid ReceiptId { get; init; }

    /// <summary>The received file bytes (bytea), preserved byte-for-byte and NEVER mutated.</summary>
    public required byte[] OriginalBytes { get; init; }

    /// <summary>The original upload file name.</summary>
    public required string OriginalFileName { get; init; }

    /// <summary>The validated MIME content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size of <see cref="OriginalBytes"/> in bytes, denormalized for listings.</summary>
    public long ByteSize { get; init; }

    /// <summary>SHA-256 hex digest of <see cref="OriginalBytes"/>.</summary>
    public required string ContentHash { get; init; }

    /// <summary>The channel through which the original was received.</summary>
    public ReceiptSource Source { get; init; }

    /// <summary>When the original artifact was received.</summary>
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>User who uploaded the file, when available.</summary>
    public Guid? UploadedByUserId { get; init; }
}
