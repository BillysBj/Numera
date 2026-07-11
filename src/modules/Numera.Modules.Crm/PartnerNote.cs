using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>
/// A free-text note attached to a <see cref="BusinessPartner"/>. Notes are
/// GoBD-irrelevant internal memos, so they are freely editable and deletable
/// (unlike audit events or legal documents).
/// </summary>
[Table("partner_notes")]
[Index(nameof(TenantId), nameof(PartnerId))]
public sealed class PartnerNote : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning partner (FK to <c>partners.id</c>).</summary>
    public Guid PartnerId { get; init; }

    /// <summary>The user who authored the note.</summary>
    public Guid AuthorUserId { get; init; }

    /// <summary>The note body (free text).</summary>
    public required string Body { get; set; }

    /// <summary>Creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last-update instant (equals <see cref="CreatedAt"/> until first edit).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
