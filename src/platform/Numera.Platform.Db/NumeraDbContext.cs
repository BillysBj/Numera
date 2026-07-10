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
        ConfigureLedgerTableNames(modelBuilder);

        ApplyTenantQueryFilters(modelBuilder);
        ApplySnakeCaseNaming(modelBuilder);
    }

    /// <summary>
    /// Maps the reflectively-registered ledger entity types to their pluralised,
    /// snake_case table names so they line up with the RLS SQL in the migration
    /// (accounts, journal_entries, postings). Done by simple-name match to avoid a
    /// compile-time reference to the Modules.Ledger assembly.
    /// </summary>
    private static void ConfigureLedgerTableNames(ModelBuilder modelBuilder)
    {
        var ledgerTables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Account"] = "accounts",
            ["JournalEntry"] = "journal_entries",
            ["Posting"] = "postings",
        };

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (ledgerTables.TryGetValue(entityType.ClrType.Name, out var table))
            {
                modelBuilder.Entity(entityType.ClrType).ToTable(table);
            }
        }
    }

    /// <summary>
    /// Rewrites table, column, key and index names to snake_case so the physical
    /// schema (and thus the hand-written RLS SQL referencing <c>tenant_id</c>) is
    /// consistent Postgres convention regardless of the CLR property casing.
    /// </summary>
    private static void ApplySnakeCaseNaming(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is not null)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entityType.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var fk in entityType.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (indexName is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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
        EnsureModuleAssembliesLoaded();

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

    /// <summary>
    /// Forces the transitive closure of referenced "Numera.*" assemblies to load so
    /// that reflection over <see cref="AppDomain.CurrentDomain"/> sees module entity
    /// types (e.g. Modules.Ledger) even though .NET loads assemblies lazily and
    /// nothing has yet touched a type from them (notably at design-time migration
    /// scaffolding). Modules reference Platform.Db, so this cannot be a compile-time
    /// reference without a cycle.
    /// </summary>
    private static void EnsureModuleAssembliesLoaded()
    {
        // 1) Walk the reference graph of everything already loaded (covers the normal
        //    runtime host where the Api entry assembly is present).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<System.Reflection.Assembly>(
            AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic));

        foreach (var a in queue)
        {
            seen.Add(a.GetName().Name!);
        }

        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is null
                    || !reference.Name.StartsWith("Numera.", StringComparison.Ordinal)
                    || !seen.Add(reference.Name))
                {
                    continue;
                }

                TryLoad(() => System.Reflection.Assembly.Load(reference), queue);
            }
        }

        // 2) Probe the DbContext assembly's directory for Numera.Modules.* DLLs and
        //    load any not yet present. This is what makes design-time `dotnet ef`
        //    scaffolding see module entities: the EF design host loads only the
        //    DbContext assembly, so the Api->Ledger reference edge is never walked,
        //    but the module DLLs ARE copied next to Platform.Db in the output.
        var baseDir = System.IO.Path.GetDirectoryName(typeof(NumeraDbContext).Assembly.Location);
        if (baseDir is null)
        {
            return;
        }

        foreach (var dll in System.IO.Directory.EnumerateFiles(baseDir, "Numera.Modules.*.dll"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(dll);
            if (!seen.Add(name))
            {
                continue;
            }

            TryLoad(() => System.Reflection.Assembly.LoadFrom(dll), queue);
        }
    }

    private static void TryLoad(Func<System.Reflection.Assembly> load, Queue<System.Reflection.Assembly> queue)
    {
        try
        {
            queue.Enqueue(load());
        }
        catch (System.IO.FileNotFoundException)
        {
            // Assembly not deployed in this context; skip.
        }
        catch (System.BadImageFormatException)
        {
            // Not a managed assembly we can reflect over; skip.
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
