using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Testcontainers.PostgreSql;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Boots a real <c>postgres:18</c> via Testcontainers, applies the actual EF Core
/// migrations (tables + RLS policies + the audit append-only REVOKE/trigger), and
/// exposes a factory that opens a <see cref="NumeraDbContext"/> as the
/// least-privilege <c>numera_app</c> role (NO BYPASSRLS) bound to a chosen tenant.
/// </summary>
/// <remarks>
/// <para>
/// The critical correctness detail: assertions run as <c>numera_app</c>, NOT as the
/// container superuser. A superuser bypasses RLS even under <c>FORCE ROW LEVEL
/// SECURITY</c>, which would make the isolation tests falsely pass. Running as a
/// non-superuser, no-bypass role is what actually exercises the RLS policies.
/// </para>
/// <para>
/// Setup order mirrors production (<c>scripts/db-roles.sql</c> then migrate): the
/// <c>numera_app</c> role is created BEFORE migrations so the audit migration's
/// role-guarded <c>REVOKE UPDATE, DELETE</c> engages, then baseline DML is granted
/// and the audit append-only exception re-applied so the final privilege state is
/// deterministic regardless of grant ordering.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AppPassword = "dev_app";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("numera")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _adminConnectionString = string.Empty;

    /// <summary>Connection string for the least-privilege runtime role (numera_app).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _adminConnectionString = _postgres.GetConnectionString();

        // 1) Create the runtime role BEFORE migrating so the audit migration's
        //    role-guarded REVOKE UPDATE/DELETE actually runs against it.
        await ExecuteAdminAsync(
            """
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                CREATE ROLE numera_app LOGIN PASSWORD 'dev_app'
                  NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
              END IF;
            END $$;
            """);

        // 2) Apply the real migrations (tables + RLS + audit append-only) as the
        //    owning superuser. Superuser is fine here: it only builds the schema;
        //    every assertion below runs as numera_app.
        await using (var migrationContext = CreateContext(_adminConnectionString, tenantId: null))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // 3) Grant the runtime role its baseline DML, then re-apply the audit
        //    append-only exception so numera_app can read/write tenant tables but
        //    can never UPDATE/DELETE audit_events (permission-denied layer). The
        //    BEFORE UPDATE OR DELETE trigger is the unconditional second layer.
        await ExecuteAdminAsync(
            """
            GRANT USAGE ON SCHEMA public TO numera_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO numera_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO numera_app;
            REVOKE UPDATE, DELETE ON audit_events FROM numera_app;
            REVOKE UPDATE, DELETE ON vat_payment FROM numera_app;
            REVOKE UPDATE, DELETE ON ug_ruecklage_buchungen FROM numera_app;
            """);

        AppConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Username = "numera_app",
            Password = AppPassword,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// Opens a <see cref="NumeraDbContext"/> as <c>numera_app</c> bound to
    /// <paramref name="tenantId"/>. The tenant GUC is applied by the production
    /// <c>TenantConnectionInterceptor</c> on connection open, so RLS is enforced.
    /// </summary>
    public NumeraDbContext CreateAppContext(Guid? tenantId, string? connectionStringOverride = null)
        => CreateContext(connectionStringOverride ?? AppConnectionString, tenantId);

    /// <summary>
    /// Builds an <c>numera_app</c> connection string with a single-connection pool so
    /// the pool-leak suite is guaranteed to reuse the same physical connection across
    /// simulated requests.
    /// </summary>
    public string SingleConnectionAppString(string applicationName) =>
        new NpgsqlConnectionStringBuilder(AppConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 1,
            ApplicationName = applicationName,
        }.ConnectionString;

    private static NumeraDbContext CreateContext(string connectionString, Guid? tenantId)
    {
        var tenant = new TenantContext();
        if (tenantId is { } id)
        {
            tenant.SetTenant(id);
        }

        var options = new DbContextOptionsBuilder<NumeraDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new NumeraDbContext(options, tenant);
    }

    /// <summary>Seeds a single audit_events row for <paramref name="tenantId"/> (as that tenant).</summary>
    public async Task SeedAuditEventAsync(Guid tenantId, string action)
    {
        await using var context = CreateAppContext(tenantId);
        context.Set<AuditEvent>().Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = Guid.CreateVersion7(),
            Action = action,
            EntityType = "IntegrationTest",
        });
        await context.SaveChangesAsync();
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Shares one Postgres container across every integration suite (container startup
/// is the expensive part; the RLS policies isolate per-test data by fresh tenant
/// GUIDs, so no cross-test cleanup is needed — and audit_events is append-only anyway).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
