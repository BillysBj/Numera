using Numera.Platform.Db.Entities;

namespace Numera.Api.Auth;

/// <summary>
/// Resolves the authenticated user's role in the current tenant.
/// This seam deliberately lives in the API host because <see cref="MembershipRole"/>
/// belongs to the Db project; keeping it here prevents the low-level Tenancy project
/// from acquiring a dependency on Db.
/// </summary>
public interface ICurrentUserRole
{
    /// <summary>Returns the current membership role, or <c>null</c> when none is in scope.</summary>
    Task<MembershipRole?> GetAsync(CancellationToken cancellationToken = default);
}
