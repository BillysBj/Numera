using Hangfire;

using Numera.Api.Jobs;
using Numera.Modules.Sales.Events;

namespace Numera.Api.Events;

/// <summary>
/// The finalize → PDF hook (Phase-4 DOCS-02, success criterion 3). Registered in DI as an
/// <see cref="IDomainEventHandler{T}"/> for <see cref="InvoiceFinalized"/>, so the
/// <c>InProcessDomainEventPublisher</c> — which fires AFTER the finalize transaction commits
/// (<c>SalesDocumentEndpoints</c> finalize) — invokes it. It ENQUEUES the render job and returns
/// immediately; it NEVER renders inline. That enqueue-only contract is the
/// "blockiert die Finalisierung nicht" guarantee: finalize stays fast, and the durable Hangfire
/// job re-establishes tenant context and does the CPU-heavy render out-of-band.
/// </summary>
/// <remarks>
/// The job runs on the Api's default-queue Hangfire server (the Api has the Sales/QuestPDF
/// references; the "worker" queue does not — RESEARCH.md Pitfall 2, LOCKED). Rendering "de" by
/// default; the on-demand GET /{id}/pdf endpoint renders other languages on request.
/// </remarks>
public sealed class EnqueuePdfOnFinalize : IDomainEventHandler<InvoiceFinalized>
{
    private readonly IBackgroundJobClient _jobs;

    /// <summary>Creates the handler over the Hangfire client.</summary>
    public EnqueuePdfOnFinalize(IBackgroundJobClient jobs) => _jobs = jobs;

    /// <inheritdoc />
    public Task HandleAsync(InvoiceFinalized domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Enqueue ONLY — never render inline (keeps finalize fast; the job re-establishes tenant).
        _jobs.Enqueue<RenderDocumentPdfJob>(
            j => j.RunAsync(domainEvent.TenantId, domainEvent.DocumentId, "de", CancellationToken.None));

        return Task.CompletedTask;
    }
}
