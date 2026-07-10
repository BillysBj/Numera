using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Tenancy;

namespace Numera.Platform.Db;

/// <summary>
/// Base <see cref="DbContext"/> for the Numera modular monolith. Modules add their
/// own <c>DbSet</c>s and configuration against this context.
/// </summary>
/// <remarks>
/// Two tenancy controls are wired here:
/// <list type="number">
///   <item><b>Connection interceptor</b> — <see cref="TenantConnectionInterceptor"/>
///   sets the <c>app.current_tenant</c> GUC so the database RLS policies
///   (plan 01-02) are the primary isolation control.</item>
///   <item><b>Global query filter</b> — every <see cref="ITenantEntity"/> is filtered
///   by <c>TenantId == current tenant</c> as application-level defence-in-depth.</item>
/// </list>
/// No RLS policies or migrations are created in plan 01-01; this is the compiling
/// kernel that later plans build on.
/// </remarks>
public class NumeraDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    /// <summary>Creates the context bound to <paramref name="currentTenant"/>.</summary>
    public NumeraDbContext(DbContextOptions options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Register the tenant GUC interceptor even when the context is created via
        // AddDbContext; EF de-duplicates interceptors registered more than once.
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(_currentTenant));
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // e => e.TenantId == _currentTenant.TenantId  (defence-in-depth mirror of RLS).
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));

            // Compare against a nullable capture so a null current tenant yields no rows.
            Expression<Func<Guid?>> currentTenantAccessor = () => _currentTenant.TenantId;
            var body = Expression.Equal(
                Expression.Convert(tenantProperty, typeof(Guid?)),
                currentTenantAccessor.Body);

            var filter = Expression.Lambda(body, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
