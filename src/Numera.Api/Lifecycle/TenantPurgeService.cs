using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Db;

namespace Numera.Api.Lifecycle;

/// <summary>
/// Hard-deletes every database row of a single tenant (Phase B1 Scheibe 2, Szenario 1 Stufe 3)
/// and writes the tamper-evident purge record to <c>platform_deletion_log</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONE place in the system that deliberately bypasses the GoBD immutability triggers,
/// and it is reachable ONLY from the operator sweep job — never from a tenant request. It runs on a
/// dedicated <b>maintenance</b> connection (<c>ConnectionStrings:Maintenance</c>, falling back to the
/// schema-owning <c>Hangfire</c> connection = the Postgres superuser), so it is not the least-
/// privilege <c>numera_app</c> role. In a single transaction it sets
/// <c>session_replication_role = replica</c> (which disables the per-table immutability triggers and
/// FK triggers for the duration of this transaction only), deletes the tenant's rows across every
/// mapped <see cref="ITenantEntity"/> table plus the <c>tenants</c> row, writes the append-only log
/// entry, and commits. Outside this transaction the immutability triggers remain fully in force.
/// </para>
/// <para>
/// The set of purged tables is derived reflectively from the EF model (every mapped
/// <see cref="ITenantEntity"/>), so a future tenant table is purged automatically. The
/// <c>platform_deletion_log</c> table is deliberately NOT an <see cref="ITenantEntity"/> and is not
/// mapped in the DbContext, so it is neither tenant-scoped nor itself ever purged or exported.
/// </para>
/// </remarks>
public sealed class TenantPurgeService
{
    private readonly NumeraDbContext _db;
    private readonly IConfiguration _configuration;

    /// <summary>Creates the purge service. The DbContext is used only to read the EF model.</summary>
    public TenantPurgeService(NumeraDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    /// <summary>
    /// Purges the tenant's database rows and writes the purge log in one privileged transaction.
    /// Idempotent at the sweep level: once the <c>tenants</c> row is gone the tenant is no longer
    /// discoverable, so it is never purged twice.
    /// </summary>
    public async Task<TenantPurgeDbResult> PurgeDatabaseAndLogAsync(
        TenantPurgeRecord record,
        CancellationToken ct)
    {
        var tables = TenantTableNames();

        await using var connection = new NpgsqlConnection(ResolveMaintenanceConnectionString());
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Disable user + FK triggers for THIS transaction only. This is what lets the purge
        // delete finalized, immutable rows; it is scoped to the transaction (SET LOCAL) and never
        // leaks to any other connection.
        await ExecuteAsync(connection, transaction, "SET LOCAL session_replication_role = replica;", ct)
            .ConfigureAwait(false);

        var rowCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Table names come from the trusted EF model (not user input); tenant id is parameterized.
            command.CommandText = $"DELETE FROM \"{table}\" WHERE tenant_id = @tenantId;";
            command.Parameters.AddWithValue("tenantId", record.TenantId);
            var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            rowCounts[table] = deleted;
            total += deleted;
        }

        // The self-scoped tenants row itself (keyed on id, not tenant_id).
        await using (var tenantDelete = connection.CreateCommand())
        {
            tenantDelete.Transaction = transaction;
            tenantDelete.CommandText = "DELETE FROM \"tenants\" WHERE id = @tenantId;";
            tenantDelete.Parameters.AddWithValue("tenantId", record.TenantId);
            var tenantRows = await tenantDelete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            rowCounts["tenants"] = tenantRows;
            total += tenantRows;
        }

        // The append-only purge record, written inside the SAME transaction (after all deletes, so
        // its total reflects everything) and committed atomically with them: "tenant row gone" and
        // "purge logged" are therefore inseparable.
        await InsertLogAsync(connection, transaction, record, total, rowCounts, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new TenantPurgeDbResult(total, rowCounts);
    }

    private static async Task InsertLogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TenantPurgeRecord record,
        int totalRows,
        IReadOnlyDictionary<string, int> rowCounts,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO platform_deletion_log
                (id, tenant_id, tenant_name, requested_by, requested_at, purged_at,
                 rows_deleted, row_counts, keycloak_outcome, finapi_outcome, stripe_outcome)
            VALUES
                (@id, @tenantId, @tenantName, @requestedBy, @requestedAt, now(),
                 @rowsDeleted, @rowCounts::jsonb, @keycloak, @finapi, @stripe);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenantId", record.TenantId);
        command.Parameters.AddWithValue("tenantName", record.TenantName);
        command.Parameters.AddWithValue("requestedBy", (object?)record.RequestedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("requestedAt", (object?)record.RequestedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("rowsDeleted", totalRows);
        command.Parameters.AddWithValue("rowCounts", JsonSerializer.Serialize(rowCounts));
        command.Parameters.AddWithValue("keycloak", record.KeycloakOutcome);
        command.Parameters.AddWithValue("finapi", record.FinApiOutcome);
        command.Parameters.AddWithValue("stripe", record.StripeOutcome);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Every mapped tenant-scoped table name, derived from the EF model.</summary>
    private IReadOnlyList<string> TenantTableNames() =>
        _db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned()
                && typeof(ITenantEntity).IsAssignableFrom(t.ClrType)
                && t.GetTableName() is not null)
            .Select(t => t.GetTableName()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    private string ResolveMaintenanceConnectionString() =>
        _configuration.GetConnectionString("Maintenance")
        ?? _configuration.GetConnectionString("Hangfire")
        ?? throw new InvalidOperationException(
            "A privileged connection (ConnectionStrings:Maintenance or ConnectionStrings:Hangfire) "
            + "is required for the tenant purge.");
}

/// <summary>Everything the purge needs to record, gathered by the orchestrator.</summary>
public sealed record TenantPurgeRecord(
    Guid TenantId,
    string TenantName,
    Guid? RequestedBy,
    DateTimeOffset? RequestedAt,
    string KeycloakOutcome,
    string FinApiOutcome,
    string StripeOutcome);

/// <summary>Outcome of the database portion of a purge.</summary>
public sealed record TenantPurgeDbResult(int RowsDeleted, IReadOnlyDictionary<string, int> RowCounts);
