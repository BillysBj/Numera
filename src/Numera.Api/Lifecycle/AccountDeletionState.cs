using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Lifecycle;

/// <summary>Resolves whether the current tenant is in the deletion grace window (read-only).</summary>
public interface IAccountDeletionState
{
    /// <summary>Returns true while a deletion is pending (<c>DeletionScheduledFor</c> is set).</summary>
    Task<bool> IsPendingDeletionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IAccountDeletionState"/>. Loads the caller's own tenant row (RLS self-scoped)
/// at most once per scope and memoises whether a deletion is pending. Used by the write guard.
/// </summary>
public sealed class AccountDeletionState : IAccountDeletionState
{
    private readonly ICurrentTenant _currentTenant;
    private readonly NumeraDbContext _db;

    private bool? _cached;

    /// <summary>Creates the scoped deletion-state resolver.</summary>
    public AccountDeletionState(ICurrentTenant currentTenant, NumeraDbContext db)
    {
        _currentTenant = currentTenant;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<bool> IsPendingDeletionAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        if (_currentTenant.TenantId is null)
        {
            return (_cached = false).Value;
        }

        var scheduledFor = await _db.Tenants
            .AsNoTracking()
            .Select(t => t.DeletionScheduledFor)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (_cached = scheduledFor is not null).Value;
    }
}
