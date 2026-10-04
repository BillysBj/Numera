namespace Numera.Api.Auth;

/// <summary>Request-cached employee permissions. Null means unrestricted.</summary>
public interface ICurrentUserPermissions
{
    Task<string[]?> GetAllowedAreasAsync(CancellationToken cancellationToken = default);
}
