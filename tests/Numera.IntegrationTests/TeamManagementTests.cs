using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Services;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres coverage for team membership persistence and RLS.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TeamManagementTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Invite_inserts_membership_with_selected_role_and_other_tenant_cannot_see_it()
    {
        var tenantId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var invitedId = Guid.CreateVersion7();
        await SeedTenantAndOwnerAsync(tenantId, ownerId);

        await using (var db = fixture.CreateAppContext(tenantId))
        {
            var result = await CreateService(db, tenantId, invitedId)
                .InviteAsync("berater@example.test", MembershipRole.TaxAdvisor, default);
            Assert.Equal(invitedId, result);
        }

        await using (var read = fixture.CreateAppContext(tenantId))
        {
            var membership = await read.Memberships.SingleAsync(x => x.UserId == invitedId);
            Assert.Equal(MembershipRole.TaxAdvisor, membership.Role);
        }

        await using var other = fixture.CreateAppContext(Guid.CreateVersion7());
        Assert.False(await other.Memberships.IgnoreQueryFilters().AnyAsync(x => x.UserId == invitedId));
    }

    [Fact]
    public async Task Changing_a_role_persists()
    {
        var tenantId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        await SeedTenantAndOwnerAsync(tenantId, ownerId, memberId);

        await using (var db = fixture.CreateAppContext(tenantId))
        {
            var outcome = await CreateService(db, tenantId).ChangeRoleAsync(
                memberId,
                MembershipRole.TaxAdvisor,
                default);
            Assert.Equal(MemberMutationOutcome.Success, outcome);
        }

        await using var read = fixture.CreateAppContext(tenantId);
        Assert.Equal(
            MembershipRole.TaxAdvisor,
            (await read.Memberships.SingleAsync(x => x.UserId == memberId)).Role);
    }

    [Fact]
    public async Task Removing_a_member_deletes_the_membership()
    {
        var tenantId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        await SeedTenantAndOwnerAsync(tenantId, ownerId, memberId);

        await using (var db = fixture.CreateAppContext(tenantId))
        {
            var outcome = await CreateService(db, tenantId).RemoveAsync(memberId, default);
            Assert.Equal(MemberMutationOutcome.Success, outcome);
        }

        await using var read = fixture.CreateAppContext(tenantId);
        Assert.False(await read.Memberships.AnyAsync(x => x.UserId == memberId));
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_or_removed()
    {
        var tenantId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        await SeedTenantAndOwnerAsync(tenantId, ownerId);

        await using var db = fixture.CreateAppContext(tenantId);
        var service = CreateService(db, tenantId);
        Assert.Equal(
            MemberMutationOutcome.LastOwner,
            await service.ChangeRoleAsync(ownerId, MembershipRole.Employee, default));
        Assert.Equal(
            MemberMutationOutcome.LastOwner,
            await service.RemoveAsync(ownerId, default));
        Assert.Equal(MembershipRole.Owner, (await db.Memberships.SingleAsync()).Role);
    }

    [Fact]
    public async Task Cross_tenant_membership_insert_is_rejected_by_rls()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedTenantAndOwnerAsync(tenantA, Guid.CreateVersion7());

        await using var db = fixture.CreateAppContext(tenantA);
        db.Memberships.Add(new Membership
        {
            TenantId = tenantB,
            UserId = Guid.CreateVersion7(),
            Role = MembershipRole.Employee,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedTenantAndOwnerAsync(Guid tenantId, Guid ownerId, Guid? memberId = null)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = $"Team test {tenantId}" });
        db.Memberships.Add(new Membership
        {
            TenantId = tenantId,
            UserId = ownerId,
            Role = MembershipRole.Owner,
        });
        if (memberId is { } id)
        {
            db.Memberships.Add(new Membership
            {
                TenantId = tenantId,
                UserId = id,
                Role = MembershipRole.Employee,
            });
        }

        await db.SaveChangesAsync();
    }

    private static InvitationService CreateService(
        NumeraDbContext db,
        Guid tenantId,
        Guid? createdUserId = null)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(tenantId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:AdminBaseUrl"] = "https://keycloak.test/",
                ["Keycloak:Realm"] = "numera",
                ["Keycloak:AdminUsername"] = "admin",
                ["Keycloak:AdminPassword"] = "admin",
            })
            .Build();
        return new InvitationService(
            new FakeHttpClientFactory(createdUserId ?? Guid.CreateVersion7()),
            db,
            tenant,
            configuration,
            NullLogger<InvitationService>.Instance,
            new NoOpAuditWriter());
    }

    private sealed class FakeHttpClientFactory(Guid createdUserId) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new KeycloakHandler(createdUserId), disposeHandler: true);
    }

    private sealed class KeycloakHandler(Guid createdUserId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            if (request.RequestUri?.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal) == true)
            {
                response = Json(HttpStatusCode.OK, """{"access_token":"test-token"}""");
            }
            else if (request.Method == HttpMethod.Post
                     && request.RequestUri?.AbsolutePath.EndsWith("/users", StringComparison.Ordinal) == true)
            {
                response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Location = new Uri($"https://keycloak.test/admin/realms/numera/users/{createdUserId}");
            }
            else
            {
                response = new HttpResponseMessage(
                    request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.Created);
            }

            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
