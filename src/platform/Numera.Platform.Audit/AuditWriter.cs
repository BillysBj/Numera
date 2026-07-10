using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Platform.Audit;

/// <summary>
/// Synchronous, in-transaction implementation of <see cref="IAuditWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stamps each event with the ambient tenant (<see cref="ICurrentTenant"/>) and
/// actor (<see cref="ICurrentUser"/>), then <c>Add</c>s an <see cref="AuditEvent"/>
/// to the SAME scoped <see cref="NumeraDbContext"/> the caller is using. It
/// deliberately does NOT open a new transaction, does NOT create a new DI scope,
/// and does NOT call <c>SaveChanges</c> or enqueue async work — the insert is
/// tracked and committed by the caller's own <c>SaveChanges</c>, so the audit row
/// and the change it records are atomic (both commit or both roll back).
/// </para>
/// <para>
/// Consequently, finance modules MUST call <see cref="RecordAsync"/> inside their
/// finalize/change transaction (the unit of work that mutates the audited entity),
/// never as a separate/background operation.
/// </para>
/// </remarks>
public sealed class AuditWriter : IAuditWriter
{
    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the writer over the caller's scoped context and ambient
    /// tenant/actor.</summary>
    public AuditWriter(NumeraDbContext db, ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public Task RecordAsync(IAuditEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var tenantId = _currentTenant.TenantId
            ?? throw new InvalidOperationException(
                "Cannot record an audit event without an ambient tenant. " +
                "RecordAsync must run inside a tenant-scoped unit of work.");

        var actorUserId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "Cannot record an audit event without an authenticated actor. " +
                "RecordAsync must run inside an authenticated (or explicitly acted) context.");

        // Track the insert on the CALLER's context — no new transaction, no SaveChanges.
        // The caller's own SaveChanges commits this row atomically with the recorded change.
        _db.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = actorUserId,
            Action = evt.Action,
            EntityType = evt.EntityType,
            EntityId = evt.EntityId,
            Before = evt.Before,
            After = evt.After,
            OccurredAt = DateTimeOffset.UtcNow,
            // PrevHash / RowHash are reserved and remain null in Phase 1.
        });

        return Task.CompletedTask;
    }
}
