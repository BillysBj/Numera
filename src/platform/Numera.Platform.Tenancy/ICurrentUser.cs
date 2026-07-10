namespace Numera.Platform.Tenancy;

/// <summary>
/// Ambient access to the authenticated actor (user) driving the current unit of
/// work. Resolved per-request by the Api host's <c>CurrentUser</c> implementation
/// (plan 01-06, wired from the authenticated principal's <c>sub</c> claim) and
/// consumed by the audit writer to stamp <c>actor_user_id</c> on every event.
/// </summary>
/// <remarks>
/// <para>
/// This seam lives in <c>Numera.Platform.Tenancy</c> — alongside
/// <see cref="ICurrentTenant"/> — deliberately: BOTH the audit writer
/// (<c>Numera.Platform.Audit</c>) and the Api host's concrete implementation
/// reference this low-level project. It cannot live in <c>Numera.Api</c> because
/// <c>Numera.Platform.Audit</c> must not depend on the Api host.
/// </para>
/// <para>
/// <see cref="UserId"/> is nullable so system/background and pre-authentication
/// contexts (design-time, health checks, unauthenticated requests) return
/// <c>null</c> rather than a fabricated actor.
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The authenticated actor's user id (the Keycloak <c>sub</c> claim),
    /// or <c>null</c> when no user is in scope (system/background/unauthenticated).</summary>
    Guid? UserId { get; }
}
