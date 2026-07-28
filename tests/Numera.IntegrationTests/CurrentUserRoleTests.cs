using Numera.Api.Auth;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CurrentUserRoleTests
{
    private readonly PostgresFixture _fixture;

    public CurrentUserRoleTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Resolves_tax_advisor_from_current_tenants_membership()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await SeedMembershipAsync(tenantId, userId);

        await using var db = _fixture.CreateAppContext(tenantId);
        var resolver = new CurrentUserRole(TenantOf(tenantId), new TestCurrentUser(userId), db);

        Assert.Equal(MembershipRole.TaxAdvisor, await resolver.GetAsync());
    }

    [Fact]
    public async Task Different_tenant_cannot_resolve_membership()
    {
        var membershipTenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await SeedMembershipAsync(membershipTenantId, userId);

        await using var db = _fixture.CreateAppContext(otherTenantId);
        var resolver = new CurrentUserRole(TenantOf(otherTenantId), new TestCurrentUser(userId), db);

        Assert.Null(await resolver.GetAsync());
    }

    [Fact]
    public async Task Missing_membership_resolves_null()
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = _fixture.CreateAppContext(tenantId);
        var resolver = new CurrentUserRole(
            TenantOf(tenantId),
            new TestCurrentUser(Guid.CreateVersion7()),
            db);

        Assert.Null(await resolver.GetAsync());
    }

    private async Task SeedMembershipAsync(Guid tenantId, Guid userId)
    {
        await using var db = _fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = $"Role test {tenantId}" });
        db.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            UserId = userId,
            Role = MembershipRole.TaxAdvisor,
        });
        await db.SaveChangesAsync();
    }

    private static TenantContext TenantOf(Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private sealed class TestCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
