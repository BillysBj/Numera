using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Numera.Api.Endpoints;
using Numera.Api.Events;
using Numera.Api.Jobs;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Events;
using Numera.Platform.Entitlements;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Server-authoritative EInvoicing (plan L+) tarif-gate proofs (09-02, criterion 3). The e-invoice
/// ARTIFACTS are L+; core finalize + §14 PDF + e-mail stay all-tier. Fast + DB-free: the gate is a
/// pure predicate over <see cref="IEntitlementService"/> and the auto-enqueue suppression is proven
/// at the <see cref="EnqueueEInvoiceOnFinalize"/> handler with a recording job client (no job runs,
/// so no <c>document_einvoice</c> is ever produced for a non-EInvoicing tenant).
/// </summary>
public sealed class EInvoiceGateTests
{
    [Fact]
    public async Task Gate_queries_the_EInvoicing_capability_specifically()
    {
        // Granting only EInvoicing → the gate opens.
        Assert.True(await EInvoiceEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.EInvoicing), CancellationToken.None));

        // Granting a DIFFERENT capability (Dunning) → the gate stays closed: it asks for EInvoicing.
        Assert.False(await EInvoiceEndpoints.HasCapabilityAsync(
            FakeEntitlementService.GrantingOnly(Capability.Dunning), CancellationToken.None));

        // Deny-by-default.
        Assert.False(await EInvoiceEndpoints.HasCapabilityAsync(
            FakeEntitlementService.Denying, CancellationToken.None));
    }

    [Fact]
    public void UpgradeRequired_is_a_403()
    {
        var result = Assert.IsType<ProblemHttpResult>(EInvoiceEndpoints.UpgradeRequired());
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Finalize_hook_skips_the_auto_enqueue_for_a_non_EInvoicing_tenant()
    {
        var client = new RecordingClient();
        // No EInvoicing: the handler must skip the enqueue entirely, so NO document_einvoice is ever
        // produced. Core finalize/PDF/email are untouched (this handler is one of several fan-out
        // subscribers to InvoiceFinalized and only owns the e-invoice enqueue).
        var handler = new EnqueueEInvoiceOnFinalize(client, FakeEntitlementService.Denying);

        await handler.HandleAsync(
            new InvoiceFinalized(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "RE-2026-00099",
                100m, 19m, 119m, new DateOnly(2026, 6, 1), DocumentType.Rechnung),
            CancellationToken.None);

        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task Finalize_hook_enqueues_the_generate_job_for_an_EInvoicing_tenant()
    {
        var client = new RecordingClient();
        var handler = new EnqueueEInvoiceOnFinalize(client, FakeEntitlementService.Granting);

        await handler.HandleAsync(
            new InvoiceFinalized(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "RE-2026-00100",
                100m, 19m, 119m, new DateOnly(2026, 6, 1), DocumentType.Rechnung),
            CancellationToken.None);

        Assert.Equal(1, client.CreateCalls);
        Assert.Equal(typeof(GenerateEInvoiceJob), client.LastJob!.Type);
        Assert.Equal(nameof(GenerateEInvoiceJob.RunAsync), client.LastJob!.Method.Name);
    }

    private sealed class RecordingClient : IBackgroundJobClient
    {
        public int CreateCalls { get; private set; }

        public Job? LastJob { get; private set; }

        public string Create(Job job, IState state)
        {
            CreateCalls++;
            LastJob = job;
            return Guid.CreateVersion7().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
