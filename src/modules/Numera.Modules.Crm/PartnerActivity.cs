using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>
/// One entry on a <see cref="BusinessPartner"/>'s activity timeline (created,
/// updated, archived, note added, …). Append-by-convention — rows are added, never
/// updated — giving a lightweight per-partner history.
/// </summary>
[Table("partner_activities")]
[Index(nameof(TenantId), nameof(PartnerId), nameof(OccurredAt))]
public sealed class PartnerActivity : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning partner (FK to <c>partners.id</c>).</summary>
    public Guid PartnerId { get; init; }

    /// <summary>When the activity occurred.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The kind of activity.</summary>
    public PartnerActivityType Type { get; init; }

    /// <summary>The user who triggered the activity, if known (system events may be null).</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Human-readable one-line summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Optional type of a referenced entity (e.g. "Invoice") for deep-linking.</summary>
    public string? RefType { get; init; }

    /// <summary>Optional id of the referenced entity.</summary>
    public Guid? RefId { get; init; }
}
