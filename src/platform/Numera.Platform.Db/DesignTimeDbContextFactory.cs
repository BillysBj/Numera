using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Numera.Platform.Tenancy;

namespace Numera.Platform.Db;

/// <summary>
/// Lets <c>dotnet ef</c> instantiate <see cref="NumeraDbContext"/> at design time
/// (migrations, model snapshots) without a running host or a real tenant. Uses a
/// no-op tenant so no query filter or GUC is applied during scaffolding.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NumeraDbContext>
{
    /// <inheritdoc />
    public NumeraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NumeraDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=numera;Username=numera;Password=dev")
            .Options;

        return new NumeraDbContext(options, new DesignTimeCurrentTenant());
    }

    /// <summary>Design-time tenant with no ambient tenant scope.</summary>
    private sealed class DesignTimeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public void SetTenant(Guid tenantId)
        {
            // Intentionally inert: design-time scaffolding is never tenant-scoped.
        }
    }
}
