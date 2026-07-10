using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Audit;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating cross-tenant isolation suite (success criterion 2). Proves, on a
/// real postgres:18 as the non-BYPASSRLS <c>numera_app</c> role, that a tenant can
/// neither read nor write another tenant's rows.
/// </summary>
/// <remarks>
/// Every query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// so the EF global query filter (the app-level defence-in-depth) is switched OFF —
/// leaving Row-Level Security as the ONLY isolation control under test. That is the
/// point: if RLS were broken (or the tests ran as a superuser/migrator that bypasses
/// it), the cross-tenant rows would leak and these assertions would fail.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RlsIsolationTests
{
    private readonly PostgresFixture _fixture;

    public RlsIsolationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tenant_A_sees_only_its_own_rows_and_zero_Tenant_B_rows()
    {
        // Arrange: two tenants, each with one audit row, seeded as themselves.
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await _fixture.SeedAuditEventAsync(tenantA, "a.created");
        await _fixture.SeedAuditEventAsync(tenantB, "b.created");

        // Act: read as tenant A with the app-level filter disabled -> RLS alone decides.
        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rowsVisibleToA = await contextA.Set<AuditEvent>()
            .IgnoreQueryFilters()
            .ToListAsync();

        // Assert: only A's rows are visible; none of B's leak through RLS.
        Assert.NotEmpty(rowsVisibleToA);
        Assert.All(rowsVisibleToA, e => Assert.Equal(tenantA, e.TenantId));
        Assert.DoesNotContain(rowsVisibleToA, e => e.TenantId == tenantB);

        var tenantBRowsSeenByA = await contextA.Set<AuditEvent>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == tenantB);
        Assert.Equal(0, tenantBRowsSeenByA);
    }

    [Fact]
    public async Task Insert_with_foreign_tenant_id_is_rejected_by_the_WITH_CHECK_policy()
    {
        // Arrange: acting as tenant A (GUC = A) ...
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);

        // ... attempt to write a row stamped for tenant B.
        contextA.Set<AuditEvent>().Add(new AuditEvent
        {
            TenantId = tenantB,
            ActorUserId = Guid.CreateVersion7(),
            Action = "cross.tenant.write",
            EntityType = "IntegrationTest",
        });

        // Assert: the RLS WITH CHECK clause rejects the cross-tenant INSERT.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => contextA.SaveChangesAsync());
        Assert.Contains("row-level security", (ex.InnerException?.Message ?? ex.Message), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reading_without_a_tenant_context_never_leaks_rows()
    {
        // A row exists for some tenant ...
        var tenant = Guid.CreateVersion7();
        await _fixture.SeedAuditEventAsync(tenant, "orphan.read");

        // ... but with no app.current_tenant set, the RLS predicate
        // (tenant_id = current_setting('app.current_tenant')::uuid) fails closed:
        // Postgres either errors on the unset GUC or matches nothing. Both are safe
        // — the ONLY unacceptable outcome is another tenant's row being returned.
        try
        {
            await using var context = _fixture.CreateAppContext(tenantId: null);
            var rows = await context.Set<AuditEvent>()
                .IgnoreQueryFilters()
                .Where(e => e.TenantId == tenant)
                .ToListAsync();

            // If the query succeeded it MUST have leaked nothing.
            Assert.Empty(rows);
        }
        catch (PostgresException)
        {
            // Fail-closed at the database: an unset tenant GUC errors the predicate
            // rather than leaking data. Equally safe.
        }
    }
}
