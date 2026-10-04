using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Auth;

/// <summary>
/// Scoped, memoised resolver for the authenticated user's RLS-scoped membership role.
/// </summary>
public sealed class CurrentUserRole : ICurrentUserRole, ICurrentUserPermissions
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly NumeraDbContext _db;
    private MembershipRole? _cached;
    private string[]? _allowedAreas;
    private bool _resolved;

    /// <summary>Creates the resolver for the current request scope.</summary>
    public CurrentUserRole(
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        NumeraDbContext db)
    {
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<MembershipRole?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved)
        {
            return _cached;
        }

        var tenantId = _currentTenant.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId is null || userId is null)
        {
            _resolved = true;
            return null;
        }

        var membership = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId.Value && m.UserId == userId.Value)
            .Select(m => new { m.Role, m.AllowedAreas })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        _cached = membership?.Role;
        _allowedAreas = membership?.Role == MembershipRole.Employee
            ? AreaPermissions.Parse(membership.AllowedAreas)
            : null;
        _resolved = true;

        return _cached;
    }

    /// <inheritdoc />
    public async Task<string[]?> GetAllowedAreasAsync(CancellationToken cancellationToken = default)
    {
        await GetAsync(cancellationToken).ConfigureAwait(false);
        return _allowedAreas;
    }
}
