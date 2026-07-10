using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Platform.Audit;

/// <summary>
/// An immutable, append-only record of a finance-relevant change: who did what to
/// which entity, when, and the before/after state. Written synchronously in the
/// same transaction as the change it records (see <see cref="AuditWriter"/>).
/// </summary>
/// <remarks>
/// <para>
/// Tenant-scoped (<see cref="ITenantEntity"/>): subject to the same RLS
/// <c>tenant_isolation</c> policy and tenant-leading composite index as every other
/// tenant table. Additionally, the <c>audit_events</c> table is DB-enforced
/// append-only: <c>UPDATE</c>/<c>DELETE</c> are REVOKEd from the runtime role AND a
/// <c>BEFORE UPDATE OR DELETE</c> trigger raises an exception (plan 01-04 migration).
/// Application code must therefore only ever INSERT audit rows.
/// </para>
/// <para>
/// <see cref="PrevHash"/> and <see cref="RowHash"/> are RESERVED columns, nullable
/// and unused in Phase 1. They exist so a tamper-evident hash chain can be added in
/// the GoBD-archive phase without a breaking migration; no chaining logic exists yet.
/// </para>
/// </remarks>
public sealed class AuditEvent : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The actor (user) who caused the change — the Keycloak <c>sub</c>.
    /// Resolved from <see cref="Numera.Platform.Tenancy.ICurrentUser"/>.</summary>
    public Guid ActorUserId { get; init; }

    /// <summary>The action performed, e.g. <c>"invoice.finalized"</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The type of entity affected, e.g. <c>"Invoice"</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>The id of the affected entity, when applicable.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>The entity state before the change, serialized as <c>jsonb</c>
    /// (null for creations or when no prior state applies).</summary>
    [Column(TypeName = "jsonb")]
    public string? Before { get; init; }

    /// <summary>The entity state after the change, serialized as <c>jsonb</c>
    /// (null for deletions or when no resulting state applies).</summary>
    [Column(TypeName = "jsonb")]
    public string? After { get; init; }

    /// <summary>When the change occurred (UTC). Defaults to now at construction.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>RESERVED (Phase 1: always null). Hash of the previous audit row,
    /// for a future tamper-evident chain. Bytea; unused until the GoBD-archive phase.</summary>
    public byte[]? PrevHash { get; init; }

    /// <summary>RESERVED (Phase 1: always null). Hash of this audit row's canonical
    /// content, for a future tamper-evident chain. Bytea; unused until the
    /// GoBD-archive phase.</summary>
    public byte[]? RowHash { get; init; }
}
