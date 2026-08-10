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
    /// <summary>
    /// Name of the per-<see cref="ITenantEntity"/> tenant query filter. EF Core 10
    /// <b>named</b> filters (RESEARCH.md Pattern 1) let tenancy and archival coexist:
    /// a second <i>unnamed</i> <c>HasQueryFilter</c> would silently OVERWRITE this one.
    /// Disable selectively with <c>IgnoreQueryFilters([TenantFilter])</c>.
    /// </summary>
    public const string TenantFilter = "Tenant";

    /// <summary>
    /// Name of the per-<see cref="IArchivable"/> "hide archived rows" query filter.
    /// Reveal archived rows with <c>IgnoreQueryFilters([NotArchivedFilter])</c> while
    /// the tenant filter — and, above all, RLS — stay in force.
    /// </summary>
    public const string NotArchivedFilter = "NotArchived";

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

    /// <summary>Global Stripe webhook idempotency records; intentionally not tenant-scoped.</summary>
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();

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
        ConfigureProcessedStripeEvent(modelBuilder);

        // Discover tenant-scoped entities contributed by modules whose assemblies are
        // loaded in the current app-domain (e.g. Modules.Ledger via the Api startup
        // project, and Platform.Audit's AuditEvent). Each is registered, given a
        // tenant_id-leading access index, and a defence-in-depth global query filter
        // mirroring the DB RLS policy.
        RegisterModuleTenantEntities(modelBuilder);
        ConfigureLedgerTableNames(modelBuilder);
        ConfigureAuditEvents(modelBuilder);

        // Two independent NAMED query filters (never combined with &&): "Tenant"
        // (defence-in-depth mirror of RLS) and "NotArchived" (app-level soft-delete).
        // Order is irrelevant — named filters compose rather than overwrite.
        ApplyTenantQueryFilters(modelBuilder);
        ApplyArchiveQueryFilters(modelBuilder);
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
            ["LedgerSettings"] = "ledger_settings",
            ["FiscalPeriod"] = "fiscal_periods",
        };

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (ledgerTables.TryGetValue(entityType.ClrType.Name, out var table))
            {
                var builder = modelBuilder.Entity(entityType.ClrType);
                builder.ToTable(table);

                switch (entityType.ClrType.Name)
                {
                    case "Account":
                        var chartVariant = entityType.FindProperty("ChartVariant")!;
                        builder.Property("ChartVariant")
                            .HasDefaultValue(Enum.ToObject(chartVariant.ClrType, 3));
                        builder.Property("IsActive").HasDefaultValue(true);
                        break;
                    case "JournalEntry":
                        builder.HasIndex(nameof(ITenantEntity.TenantId), "EntryDate")
                            .HasDatabaseName("ix_journal_entries_tenant_id_entry_date");
                        break;
                    case "Posting":
                        builder.HasIndex("AccountId")
                            .HasDatabaseName("ix_postings_account_id");
                        break;
                    case "LedgerSettings":
                        builder.HasIndex(nameof(ITenantEntity.TenantId))
                            .IsUnique()
                            .HasDatabaseName("ux_ledger_settings_tenant_id");
                        break;
                    case "FiscalPeriod":
                        builder.HasIndex(nameof(ITenantEntity.TenantId), "Year", "Month")
                            .IsUnique()
                            .HasDatabaseName("ux_fiscal_periods_tenant_id_year_month");
                        break;
                }
            }
        }

        var journalEntry = modelBuilder.Model.GetEntityTypes()
            .SingleOrDefault(e => e.ClrType.Name == "JournalEntry");
        var fiscalPeriod = modelBuilder.Model.GetEntityTypes()
            .SingleOrDefault(e => e.ClrType.Name == "FiscalPeriod");

        if (journalEntry is not null && fiscalPeriod is not null)
        {
            var journalBuilder = modelBuilder.Entity(journalEntry.ClrType);
            journalBuilder.HasOne(fiscalPeriod.ClrType, null)
                .WithMany()
                .HasForeignKey("PeriodId")
                .OnDelete(DeleteBehavior.Restrict);
            journalBuilder.HasOne(journalEntry.ClrType, null)
                .WithMany()
                .HasForeignKey("ReversesEntryId")
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>
    /// Configures the reflectively-registered <c>AuditEvent</c> entity (from
    /// Numera.Platform.Audit — not a compile-time reference, else a cycle): its
    /// pluralised <c>audit_events</c> table name (matching the append-only REVOKE +
    /// trigger + RLS SQL in the AuditEvents migration) and a <c>(tenant_id,
    /// occurred_at)</c> index for chronological audit-trail reads within a tenant.
    /// Matched by simple type name to avoid referencing the Audit assembly.
    /// </summary>
    private static void ConfigureAuditEvents(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.Name != "AuditEvent")
            {
                continue;
            }

            var builder = modelBuilder.Entity(entityType.ClrType);
            builder.ToTable("audit_events");
            builder.HasIndex(nameof(ITenantEntity.TenantId), "OccurredAt")
                .HasDatabaseName("ix_audit_events_tenant_id_occurred_at");
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

            // For an owned type sharing its owner's table (e.g. the CRM Address value
            // objects embedded in partners), the primary-key property is the link to
            // the owner's PK column. Renaming it would detach that shared-column
            // mapping and make EF emit a SECOND, conflicting primary key on the table.
            // Leave those key properties to EF's default so they keep sharing the
            // owner's (already snake-cased) key column.
            var ownedKeyProperties = entityType.IsOwned()
                ? entityType.FindPrimaryKey()?.Properties
                : null;

            // Two owned instances of the same value type in one row (e.g. a partner's
            // billing AND shipping Address) must not collide on identical column names.
            // At this point EF has NOT yet applied its navigation-name prefix, so the
            // value columns still carry their bare names (Street, City, …). Prefix them
            // with the owning navigation before snake-casing so billing_address_street
            // and shipping_address_street stay distinct.
            var ownershipNavigation = entityType.IsOwned()
                ? entityType.FindOwnership()?.PrincipalToDependent?.Name
                : null;

            foreach (var property in entityType.GetProperties())
            {
                if (ownedKeyProperties is not null
                    && ownedKeyProperties.Any(p => p.Name == property.Name))
                {
                    continue;
                }

                var columnName = property.GetColumnName();
                if (ownershipNavigation is not null)
                {
                    columnName = ownershipNavigation + columnName;
                }

                property.SetColumnName(ToSnakeCase(columnName));
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
            b.Property(t => t.CurrentPeriodEnd).HasColumnType("timestamptz");
            b.Property(t => t.TrialEndsAt).HasColumnType("timestamptz");
            b.Property(t => t.CreatedAt).HasColumnType("timestamptz");
            b.HasIndex(t => t.StripeCustomerId)
                .IsUnique()
                .HasFilter("stripe_customer_id IS NOT NULL");
        });
    }

    private static void ConfigureProcessedStripeEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedStripeEvent>(b =>
        {
            b.ToTable("processed_stripe_event");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.EventId).IsRequired();
            b.Property(e => e.EventType).IsRequired();
            b.Property(e => e.ProcessedAt).HasColumnType("timestamptz");
            b.HasIndex(e => e.EventId).IsUnique();
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

            // NAMED filter so the archive filter below can coexist instead of
            // overwriting this one (RESEARCH.md Pattern 1 — EF Core 10 named filters).
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(TenantFilter, filter);
        }
    }

    /// <summary>
    /// Registers the named <c>"NotArchived"</c> query filter on every mapped entity
    /// whose CLR type implements <see cref="IArchivable"/>, hiding soft-deleted rows
    /// (<c>ArchivedAt == null</c>) by default. Kept as a <b>separate</b> named filter
    /// from the tenant filter — never combined with <c>&amp;&amp;</c> — so each can be
    /// disabled independently (RESEARCH.md Pattern 1). The predicate is built the same
    /// reflective way as the tenant filter because archivable entities are discovered
    /// reflectively with no compile-time reference to them.
    /// </summary>
    private static void ApplyArchiveQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IArchivable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            // e => EF.Property<DateTimeOffset?>(e, "ArchivedAt") == null
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var efProperty = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                new[] { typeof(DateTimeOffset?) },
                parameter,
                Expression.Constant(nameof(IArchivable.ArchivedAt)));

            var body = Expression.Equal(
                efProperty,
                Expression.Constant(null, typeof(DateTimeOffset?)));

            var filter = Expression.Lambda(body, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(NotArchivedFilter, filter);
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

        // Probe both module DLLs and Platform.Audit (which carries the tenant-scoped
        // AuditEvent entity). Platform.Audit references Platform.Db, so Platform.Db
        // cannot reference it at compile time — same cycle-avoidance as the modules.
        var probePatterns = new[] { "Numera.Modules.*.dll", "Numera.Platform.Audit.dll" };
        foreach (var pattern in probePatterns)
        {
            foreach (var dll in System.IO.Directory.EnumerateFiles(baseDir, pattern))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(dll);
                if (!seen.Add(name))
                {
                    continue;
                }

                TryLoad(() => System.Reflection.Assembly.LoadFrom(dll), queue);
            }
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
