using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Numera.Api.Endpoints;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Server-authoritative Dunning (plan L+) tarif-gate proofs (09-02, criterion 3). The dunning
/// config + run are L+. The gate is a pure predicate; the run endpoint (the one internal-static
/// handler, config are minimal-API lambdas) is exercised on real postgres to prove it 403s BEFORE
/// any work — a throwing job client would fire if the gate were ever bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DunningGateTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Gate_queries_the_Dunning_capability_specifically()
    {
        Assert.True(await DunningEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.Dunning), CancellationToken.None));

        // Granting a DIFFERENT capability (EInvoicing) → the gate stays closed: it asks for Dunning.
        Assert.False(await DunningEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.EInvoicing), CancellationToken.None));

        Assert.False(await DunningEndpoints.HasCapabilityAsync(
            FakeEntitlementService.Denying, CancellationToken.None));
    }

    [Fact]
    public void UpgradeRequired_is_a_403()
    {
        var result = Assert.IsType<ProblemHttpResult>(DunningEndpoints.UpgradeRequired());
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Run_returns_403_and_does_no_work_for_a_non_Dunning_tenant()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);

        // A throwing job client proves the gate short-circuits BEFORE any enqueue/DB mutation.
        var result = await DunningEndpoints.RunAsync(
            db,
            tenantContext,
            FakeEntitlementService.Denying,
            new NoOpAuditWriter(),
            new ThrowingJobClient(),
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    private sealed class ThrowingJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) =>
            throw new Xunit.Sdk.XunitException("Dunning gate was bypassed: a job was enqueued without the Dunning capability.");

        public bool ChangeState(string jobId, IState state, string expectedState) =>
            throw new Xunit.Sdk.XunitException("Dunning gate was bypassed: ChangeState called without the Dunning capability.");
    }
}
