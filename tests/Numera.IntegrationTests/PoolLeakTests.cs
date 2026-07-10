using Microsoft.EntityFrameworkCore;

using Numera.Platform.Audit;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Proves the tenant GUC does not leak across pooled connections (RESEARCH Pitfall 1):
/// the <c>TenantConnectionInterceptor</c> sets <c>app.current_tenant</c> on open and
/// <c>RESET</c>s it on return-to-pool, so a connection reused by a different tenant's
/// request can never see the previous tenant's rows.
/// </summary>
/// <remarks>
/// Queries use <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// so only RLS (driven by the pooled connection's GUC) decides visibility — a stale
/// GUC would surface immediately as a cross-tenant leak.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PoolLeakTests
{
    private readonly PostgresFixture _fixture;

    public PoolLeakTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reused_pooled_connection_does_not_leak_tenant_context()
    {
        // A single-connection pool guarantees tenant B reuses the exact physical
        // connection tenant A just returned.
        var connectionString = _fixture.SingleConnectionAppString(nameof(Reused_pooled_connection_does_not_leak_tenant_context));

        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedAsync(connectionString, tenantA, "pool.a");
        await SeedAsync(connectionString, tenantB, "pool.b");

        // Request 1: act as tenant A, read A's rows, then return the connection to pool.
        await using (var contextA = _fixture.CreateAppContext(tenantA, connectionString))
        {
            var aRows = await contextA.Set<AuditEvent>().IgnoreQueryFilters().ToListAsync();
            Assert.NotEmpty(aRows);
            Assert.All(aRows, e => Assert.Equal(tenantA, e.TenantId));
        }

        // Request 2: SAME physical connection, now act as tenant B. If the interceptor
        // failed to RESET, A's context would still be set and A's rows would leak.
        await using var contextB = _fixture.CreateAppContext(tenantB, connectionString);
        var bRows = await contextB.Set<AuditEvent>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(bRows);
        Assert.All(bRows, e => Assert.Equal(tenantB, e.TenantId));
        Assert.DoesNotContain(bRows, e => e.TenantId == tenantA);
    }

    [Fact]
    public async Task Concurrent_tenants_over_a_small_pool_stay_isolated()
    {
        // A small shared pool forces connection reuse under concurrency; each task
        // must still see only its own tenant's data.
        var connectionString = _fixture.SingleConnectionAppString(nameof(Concurrent_tenants_over_a_small_pool_stay_isolated));

        var tenants = Enumerable.Range(0, 6).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var tenant in tenants)
        {
            await SeedAsync(connectionString, tenant, "pool.concurrent");
        }

        var tasks = tenants.Select(async tenant =>
        {
            await using var context = _fixture.CreateAppContext(tenant, connectionString);
            var rows = await context.Set<AuditEvent>().IgnoreQueryFilters().ToListAsync();
            Assert.NotEmpty(rows);
            Assert.All(rows, e => Assert.Equal(tenant, e.TenantId));
        });

        await Task.WhenAll(tasks);
    }

    private async Task SeedAsync(string connectionString, Guid tenantId, string action)
    {
        await using var context = _fixture.CreateAppContext(tenantId, connectionString);
        context.Set<AuditEvent>().Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorUserId = Guid.CreateVersion7(),
            Action = action,
            EntityType = "IntegrationTest",
        });
        await context.SaveChangesAsync();
    }
}
