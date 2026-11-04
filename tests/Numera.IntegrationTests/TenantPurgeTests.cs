using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Numera.Api.Lifecycle;
using Numera.IntegrationTests.Fixtures;
using Numera.Modules.Banking;
using Numera.Modules.Billing;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Real-PostgreSQL coverage for the irreversible tenant hard-purge (Phase B1 Scheibe 2): the
/// privileged, GoBD-trigger-bypassing database purge, its append-only log, and cross-tenant safety.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantPurgeServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Purge_deletes_all_rows_including_immutable_and_leaves_other_tenants_intact()
    {
        var victim = await SeedTenantAsync("Zu Löschen GmbH");
        var bystander = await SeedTenantAsync("Bleibt Bestehen GmbH");

        // audit_events is append-only and carries an unconditional BEFORE UPDATE/DELETE trigger —
        // the toughest possible row to delete. If the purge removes it, it removes everything.
        await fixture.SeedAuditEventAsync(victim, "test.victim");
        await fixture.SeedAuditEventAsync(victim, "test.victim.2");
        await fixture.SeedAuditEventAsync(bystander, "test.bystander");

        var requestedBy = Guid.CreateVersion7();
        var requestedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var record = new TenantPurgeRecord(
            victim, "Zu Löschen GmbH", requestedBy, requestedAt,
            "skipped:not-configured", "none", "none");

        var result = await CreatePurgeService(victim).PurgeDatabaseAndLogAsync(record, default);

        // The victim's immutable rows AND tenants row are gone.
        Assert.True(result.RowsDeleted >= 3); // 2 audit_events + the tenants row (+ any defaults)
        Assert.Equal(2, result.RowCounts["audit_events"]);
        Assert.Equal(1, result.RowCounts["tenants"]);
        Assert.Equal(0, await CountAuditEventsAsync(victim));
        Assert.False(await TenantRowExistsAsync(victim));

        // The bystander is completely untouched.
        Assert.Equal(1, await CountAuditEventsAsync(bystander));
        Assert.True(await TenantRowExistsAsync(bystander));

        // The append-only purge record was written with the outcomes passed in.
        var log = await ReadLogAsync(victim);
        Assert.Equal("Zu Löschen GmbH", log.TenantName);
        Assert.Equal(result.RowsDeleted, log.RowsDeleted);
        Assert.Equal("skipped:not-configured", log.Keycloak);
        Assert.Equal("none", log.FinApi);
        Assert.Equal("none", log.Stripe);
        Assert.Contains("audit_events", log.RowCounts);
    }

    [Fact]
    public async Task Immutability_trigger_still_blocks_deletes_outside_the_purge()
    {
        var tenantId = await SeedTenantAsync("Trigger Test GmbH");
        await fixture.SeedAuditEventAsync(tenantId, "test.immutable");

        // A privileged (superuser) DELETE that does NOT enter the purge's replica-mode transaction
        // still hits the append-only trigger — proving the trigger bypass is scoped to the purge,
        // not a global trigger-off.
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM audit_events WHERE tenant_id = @id;";
        command.Parameters.AddWithValue("id", tenantId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", ex.MessageText);

        Assert.Equal(1, await CountAuditEventsAsync(tenantId));
    }

    [Fact]
    public async Task Platform_deletion_log_is_append_only()
    {
        var tenantId = await SeedTenantAsync("Log Test GmbH");
        var record = new TenantPurgeRecord(
            tenantId, "Log Test GmbH", null, null, "none", "none", "none");
        await CreatePurgeService(tenantId).PurgeDatabaseAndLogAsync(record, default);

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE platform_deletion_log SET tenant_name = 'x' WHERE tenant_id = @id;";
        update.Parameters.AddWithValue("id", tenantId);
        var updateEx = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.Contains("append-only", updateEx.MessageText);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM platform_deletion_log WHERE tenant_id = @id;";
        delete.Parameters.AddWithValue("id", tenantId);
        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
        Assert.Contains("append-only", deleteEx.MessageText);
    }

    [Fact]
    public async Task Orchestrator_cancels_stripe_and_is_idempotent_once_purged()
    {
        var tenantId = await SeedTenantAsync("Stripe Löschung GmbH", t => t.StripeSubscriptionId = "sub_123");
        var billing = new FakeBillingProvider();

        var firstResult = await CreateOrchestrator(tenantId, billing).PurgeAsync(tenantId, default);

        Assert.NotNull(firstResult);
        Assert.Equal("sub_123", billing.LastCancelledSubscriptionId);
        Assert.False(await TenantRowExistsAsync(tenantId));

        var log = await ReadLogAsync(tenantId);
        Assert.Equal("cancelled:sub_123", log.Stripe);
        Assert.Equal("skipped:not-configured", log.Keycloak); // no Keycloak admin configured in tests
        Assert.Equal("none", log.FinApi); // no bank connections seeded

        // Re-running after the tenants row is gone is a safe no-op (idempotent sweep).
        var secondResult = await CreateOrchestrator(tenantId, new FakeBillingProvider()).PurgeAsync(tenantId, default);
        Assert.Null(secondResult);
    }

    [Fact]
    public async Task Orchestrator_deletes_each_finapi_subuser_exactly_once()
    {
        var tenantId = await SeedTenantAsync("finAPI Löschung GmbH");
        // finAPI reuses a single sub-user across a tenant's connections, so two of these share one
        // (encrypted) sub-user id and the third has its own — the purge must delete two, not three.
        await SeedBankConnectionAsync(tenantId, "cipher-user-A");
        await SeedBankConnectionAsync(tenantId, "cipher-user-A");
        await SeedBankConnectionAsync(tenantId, "cipher-user-B");

        var banking = new FakeBankConnectionProvider();
        var log = await RunOrchestratorAndReadLogAsync(tenantId, banking);

        Assert.Equal("deleted:2users", log.FinApi);
        Assert.Equal(2, banking.DeletedConnectionIds.Count);
        // A connection without a finAPI sub-user is never handed to the provider.
        Assert.All(banking.DeletedConnectionIds, id => Assert.NotEqual(Guid.Empty, id));
    }

    [Fact]
    public async Task Orchestrator_records_finapi_failures_without_blocking_the_purge()
    {
        var tenantId = await SeedTenantAsync("finAPI Fehler GmbH");
        await SeedBankConnectionAsync(tenantId, "cipher-user-A");

        var banking = new FakeBankConnectionProvider
        {
            DeleteResult = _ => throw new InvalidOperationException("finAPI unreachable"),
        };
        var log = await RunOrchestratorAndReadLogAsync(tenantId, banking);

        // The provider failed, but the DB purge still completed and the failure is recorded.
        Assert.Equal("deleted:0;failed:1", log.FinApi);
        Assert.False(await TenantRowExistsAsync(tenantId));
    }

    // --- helpers -----------------------------------------------------------

    private async Task<PurgeLogRow> RunOrchestratorAndReadLogAsync(
        Guid tenantId,
        IBankConnectionProvider banking)
    {
        var result = await CreateOrchestrator(tenantId, new FakeBillingProvider(), banking)
            .PurgeAsync(tenantId, default);
        Assert.NotNull(result);
        return await ReadLogAsync(tenantId);
    }

    private TenantPurgeService CreatePurgeService(Guid tenantId)
    {
        var db = fixture.CreateAppContext(tenantId);
        return new TenantPurgeService(db, MaintenanceConfig());
    }

    private TenantPurgeOrchestrator CreateOrchestrator(
        Guid tenantId,
        IBillingProvider billing,
        IBankConnectionProvider? banking = null)
    {
        var db = fixture.CreateAppContext(tenantId);
        var purge = new TenantPurgeService(fixture.CreateAppContext(tenantId), MaintenanceConfig());
        var httpFactory = new ServiceCollection().AddHttpClient().BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();
        // Empty config => KeycloakTenantPurger.IsConfigured is false => the Keycloak step is skipped.
        var keycloak = new KeycloakTenantPurger(httpFactory, new ConfigurationBuilder().Build());
        return new TenantPurgeOrchestrator(
            db,
            billing,
            banking ?? new StubBankConnectionProvider(),
            keycloak,
            purge,
            NullLogger<TenantPurgeOrchestrator>.Instance);
    }

    private async Task<Guid> SeedBankConnectionAsync(Guid tenantId, string finApiUserId)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        var connection = new BankConnection
        {
            TenantId = tenantId,
            Provider = BankProvider.FinApi,
            FinApiUserId = finApiUserId,
            FinApiUserSecret = "cipher-secret",
        };
        db.Set<BankConnection>().Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    private IConfiguration MaintenanceConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Maintenance"] = fixture.AdminConnectionString,
            })
            .Build();

    private async Task<Guid> SeedTenantAsync(string name, Action<Tenant>? configure = null)
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenantId);
        var tenant = new Tenant { Id = tenantId, Name = name, Plan = TenantPlan.M };
        configure?.Invoke(tenant);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<int> CountAuditEventsAsync(Guid tenantId)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        return await db.Set<Numera.Platform.Audit.AuditEvent>().AsNoTracking().CountAsync();
    }

    private async Task<bool> TenantRowExistsAsync(Guid tenantId)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        return await db.Tenants.AsNoTracking().AnyAsync();
    }

    private async Task<PurgeLogRow> ReadLogAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tenant_name, rows_deleted, keycloak_outcome, finapi_outcome, stripe_outcome, "
            + "row_counts::text FROM platform_deletion_log WHERE tenant_id = @id;";
        command.Parameters.AddWithValue("id", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected a platform_deletion_log row for the tenant.");
        return new PurgeLogRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    private sealed record PurgeLogRow(
        string TenantName,
        int RowsDeleted,
        string Keycloak,
        string FinApi,
        string Stripe,
        string RowCounts);
}
