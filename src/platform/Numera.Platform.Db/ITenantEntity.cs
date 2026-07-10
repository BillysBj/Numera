namespace Numera.Platform.Db;

/// <summary>
/// Marker for entities that are physically owned by a single tenant. Every such
/// entity carries a <see cref="TenantId"/>, which is:
/// <list type="bullet">
///   <item>the leading column of its access indexes,</item>
///   <item>enforced at the database by an RLS policy (plan 01-02), and</item>
///   <item>mirrored in-app by the <see cref="NumeraDbContext"/> global query
///   filter as defence-in-depth.</item>
/// </list>
/// </summary>
public interface ITenantEntity
{
    /// <summary>The owning tenant. Never <see cref="System.Guid.Empty"/> at rest.</summary>
    Guid TenantId { get; }
}
