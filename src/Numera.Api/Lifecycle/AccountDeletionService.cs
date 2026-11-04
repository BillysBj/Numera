using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Lifecycle;

/// <summary>
/// Orchestrates the Owner-initiated account-deletion request/undo flow (Phase B1, Scheibe 1).
/// This stage only moves the tenant into (and out of) the read-only grace window; the actual
/// purge is performed later by the sweep job (Scheibe 2). All writes target the caller's own
/// tenant row, which RLS self-scopes, so no privileged path is involved here.
/// </summary>
public sealed class AccountDeletionService
{
    private readonly NumeraDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly AccountDeletionOptions _options;

    /// <summary>Creates the scoped deletion-flow service for the current request.</summary>
    public AccountDeletionService(
        NumeraDbContext db,
        ICurrentUser currentUser,
        IAuditWriter audit,
        IOptions<AccountDeletionOptions> options)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _options = options.Value;
    }

    /// <summary>Returns the current deletion state of the caller's tenant.</summary>
    public async Task<AccountDeletionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // RLS self-scopes `tenants` to the caller's own row, so this returns at most one.
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return ToStatus(tenant);
    }

    /// <summary>
    /// Requests full account deletion. Validates the typed confirmation name and that a fresh
    /// export exists, then sets the grace window (<c>DeletionScheduledFor = now + GraceDays</c>).
    /// </summary>
    public async Task<AccountDeletionResult> RequestDeletionAsync(string confirmationName, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return new AccountDeletionResult(AccountDeletionOutcome.NotFound, ToStatus(null));
        }

        if (tenant.DeletionScheduledFor is not null)
        {
            // Idempotent: already in the grace window — report the state, do not reset the clock.
            return new AccountDeletionResult(AccountDeletionOutcome.AlreadyPending, ToStatus(tenant));
        }

        if (!NameMatches(confirmationName, tenant.Name))
        {
            return new AccountDeletionResult(AccountDeletionOutcome.NameMismatch, ToStatus(tenant));
        }

        var now = DateTimeOffset.UtcNow;
        if (!HasFreshExport(tenant.LastExportAt, now))
        {
            return new AccountDeletionResult(AccountDeletionOutcome.ExportRequired, ToStatus(tenant));
        }

        tenant.DeletionRequestedAt = now;
        tenant.DeletionRequestedBy = _currentUser.UserId;
        tenant.DeletionScheduledFor = now.AddDays(_options.GraceDays);

        await _audit.RecordAsync(
            new AccountDeletionAuditEvent("account.deletion_requested", tenant.Id), ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AccountDeletionResult(AccountDeletionOutcome.Ok, ToStatus(tenant));
    }

    /// <summary>Cancels a pending deletion (Undo), restoring normal write access.</summary>
    public async Task<AccountDeletionResult> CancelDeletionAsync(CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return new AccountDeletionResult(AccountDeletionOutcome.NotFound, ToStatus(null));
        }

        if (tenant.DeletionScheduledFor is null)
        {
            // Idempotent: nothing pending.
            return new AccountDeletionResult(AccountDeletionOutcome.NotPending, ToStatus(tenant));
        }

        tenant.DeletionRequestedAt = null;
        tenant.DeletionRequestedBy = null;
        tenant.DeletionScheduledFor = null;

        await _audit.RecordAsync(
            new AccountDeletionAuditEvent("account.deletion_cancelled", tenant.Id), ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AccountDeletionResult(AccountDeletionOutcome.Ok, ToStatus(tenant));
    }

    private bool HasFreshExport(DateTimeOffset? lastExportAt, DateTimeOffset now)
        => lastExportAt is { } exportedAt
           && exportedAt >= now.AddHours(-_options.ExportFreshnessHours);

    private AccountDeletionStatus ToStatus(Numera.Platform.Db.Entities.Tenant? tenant)
    {
        if (tenant is null)
        {
            return new AccountDeletionStatus(false, null, null, _options.GraceDays, null, false);
        }

        var now = DateTimeOffset.UtcNow;
        return new AccountDeletionStatus(
            PendingDeletion: tenant.DeletionScheduledFor is not null,
            RequestedAt: tenant.DeletionRequestedAt,
            ScheduledFor: tenant.DeletionScheduledFor,
            GraceDays: _options.GraceDays,
            LastExportAt: tenant.LastExportAt,
            HasFreshExport: HasFreshExport(tenant.LastExportAt, now));
    }

    private static bool NameMatches(string? supplied, string actual)
        => !string.IsNullOrWhiteSpace(supplied)
           && string.Equals(supplied.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The outcome of a deletion request/cancel call.</summary>
public enum AccountDeletionOutcome
{
    /// <summary>The operation succeeded.</summary>
    Ok,

    /// <summary>The confirmation name did not match the tenant name.</summary>
    NameMismatch,

    /// <summary>No sufficiently fresh data export exists; export is required first.</summary>
    ExportRequired,

    /// <summary>A deletion is already pending (request was a no-op).</summary>
    AlreadyPending,

    /// <summary>No deletion was pending (cancel was a no-op).</summary>
    NotPending,

    /// <summary>The tenant row could not be resolved.</summary>
    NotFound,
}

/// <summary>A deletion request/cancel result: the outcome plus the resulting state.</summary>
public sealed record AccountDeletionResult(AccountDeletionOutcome Outcome, AccountDeletionStatus Status);

/// <summary>The caller-facing deletion state of a tenant.</summary>
public sealed record AccountDeletionStatus(
    bool PendingDeletion,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ScheduledFor,
    int GraceDays,
    DateTimeOffset? LastExportAt,
    bool HasFreshExport);

/// <summary>Audit row for an account-deletion request or cancellation (no business data changes).</summary>
internal sealed record AccountDeletionAuditEvent(string Action, Guid TenantId) : IAuditEvent
{
    public string EntityType => "Tenant";

    public Guid? EntityId => TenantId;

    public string? Before => null;

    public string? After => null;
}
