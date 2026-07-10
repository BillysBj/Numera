using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Platform.Db;

/// <summary>
/// Base <see cref="DbContext"/> for the Numera modular monolith. Modules add their
/// own entity types (discovered here via the <see cref="ITenantEntity"/> scan) and
/// configuration against this context.
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
/// The <c>tenants</c> table itself is NOT an <see cref="ITenantEntity"/> — it IS the
/// tenant — so it is self-scoped by RLS on <c>id</c> rather than <c>tenant_id</c>.
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

    /// <summary>Tenants (Mandanten). Self-scoped by RLS on <c>id</c>.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>User&lt;-&gt;tenant memberships (mirror of Keycloak org membership).</summary>
    public DbSet<Membership> Memberships => Set<Membership>();

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

        ConfigureTenant(modelBuilder);
        ConfigureMembership(modelBuilder);

        // Discover tenant-scoped entities contributed by modules whose assemblies are
        // loaded in the current app-domain (e.g. Modules.Ledger via the Api startup
        // project). Each is registered, given a tenant_id-leading access index, and a
        // defence-in-depth global query filter mirroring the DB RLS policy.
        RegisterModuleTenantEntities(modelBuilder);

        ApplyTenantQueryFilters(modelBuilder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Name).IsRequired();
            b.Property(t => t.Plan).HasConversion<int>();
            b.Property(t => t.CreatedAt).HasColumnType("timestamptz");
        });
    }

    private static void ConfigureMembership(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("membership");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Role).HasConversion<int>();
            b.Property(m => m.CreatedAt).HasColumnType("timestamptz");

            b.HasOne(m => m.Tenant)
                .WithMany(t => t.Memberships)
                .HasForeignKey(m => m.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Business uniqueness AND the tenant-leading access path in one index.
            b.HasIndex(m => new { m.TenantId, m.UserId })
                .IsUnique()
                .HasDatabaseName("ix_membership_tenant_id_user_id");
        });
    }

    /// <summary>
    /// Registers every non-abstract <see cref="ITenantEntity"/> from loaded assemblies
    /// that is not already in the model (e.g. ledger entities from Modules.Ledger),
    /// giving each a tenant_id-leading composite index over (TenantId, Id).
    /// </summary>
    private static void RegisterModuleTenantEntities(ModelBuilder modelBuilder)
    {
        var tenantEntityTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(ITenantEntity).IsAssignableFrom(t));

        foreach (var clrType in tenantEntityTypes)
        {
            if (modelBuilder.Model.FindEntityType(clrType) is not null)
            {
                continue;
            }

            modelBuilder.Entity(clrType);
        }

        // For every mapped ITenantEntity (module-contributed OR built-in Membership),
        // ensure a tenant_id-leading composite index exists on (tenant_id, id) so RLS
        // predicates hit an index (RESEARCH.md Pitfall 2).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var builder = modelBuilder.Entity(entityType.ClrType);
            var pk = entityType.FindPrimaryKey()?.Properties[0].Name ?? "Id";
            var hasTenantLeadingIndex = entityType.GetIndexes()
                .Any(i => i.Properties.Count >= 1
                          && i.Properties[0].Name == nameof(ITenantEntity.TenantId));

            if (!hasTenantLeadingIndex)
            {
                builder.HasIndex(nameof(ITenantEntity.TenantId), pk);
            }
        }
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
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

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
