using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>
/// A due-date driven task attached to a <see cref="BusinessPartner"/>.
/// Persist new tasks via <c>db.Add(...)</c>; adding a client-keyed UUIDv7 entity
/// through a collection navigation can be misclassified as an update.
/// </summary>
[Table("partner_tasks")]
[Index(nameof(TenantId), nameof(PartnerId), nameof(DueDate))]
public sealed class PartnerTask : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning partner.</summary>
    public Guid PartnerId { get; init; }

    /// <summary>Short task title.</summary>
    public required string Title { get; set; }

    /// <summary>Optional task details.</summary>
    public string? Description { get; set; }

    /// <summary>Optional due date.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Current completion status.</summary>
    public PartnerTaskStatus Status { get; set; } = PartnerTaskStatus.Open;

    /// <summary>Optional user responsible for the task.</summary>
    public Guid? AssignedUserId { get; set; }

    /// <summary>Creation instant.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Instant at which the task was completed; null while open.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
