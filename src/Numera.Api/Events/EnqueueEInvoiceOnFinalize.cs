using Hangfire;

using Numera.Api.Jobs;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Events;

namespace Numera.Api.Events;

/// <summary>
/// The finalize → e-invoice hook (Phase-5 EINV-01/EINV-03). Registered in DI as an
/// <see cref="IDomainEventHandler{T}"/> for <see cref="InvoiceFinalized"/>, so the
/// <c>InProcessDomainEventPublisher</c> — which fires AFTER the finalize transaction commits
/// (<c>SalesDocumentEndpoints</c> finalize) — invokes it. It ENQUEUES the generate job and
/// returns immediately; it NEVER generates inline. That enqueue-only contract keeps finalize
/// fast; the durable Hangfire job re-establishes tenant context and does the XRechnung
/// generation + KoSIT validation out-of-band.
/// </summary>
/// <remarks>
/// <para>
/// This handler COEXISTS with <see cref="EnqueuePdfOnFinalize"/>: the publisher fans out to ALL
/// registered handlers for the event, so a single finalize enqueues both the PDF render job and
/// this e-invoice job with no change to <c>FinalizeCoreAsync</c>.
/// </para>
/// <para>
/// Only a <see cref="DocumentType.Rechnung"/> is e-invoiced in v1: the mapper (05-01) is type-380
/// only, and Storno/Gutschrift e-invoicing (BT-3 381/384) is deliberately out of scope — the
/// document type carried on the event is the seam that gates this. The job runs on the Api's
/// default-queue Hangfire server (the Api has the Sales/ZUGFeRD references; the "worker" queue
/// does not — RESEARCH.md Pitfall 2, LOCKED).
/// </para>
/// </remarks>
public sealed class EnqueueEInvoiceOnFinalize : IDomainEventHandler<InvoiceFinalized>
{
    private readonly IBackgroundJobClient _jobs;

    /// <summary>Creates the handler over the Hangfire client.</summary>
    public EnqueueEInvoiceOnFinalize(IBackgroundJobClient jobs) => _jobs = jobs;

    /// <inheritdoc />
    public Task HandleAsync(InvoiceFinalized domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // v1 scope: only a Rechnung (type 380) is e-invoiced. Storno/Gutschrift are a documented seam.
        if (domainEvent.DocumentType != DocumentType.Rechnung)
        {
            return Task.CompletedTask;
        }

        // Enqueue ONLY — never generate inline (keeps finalize fast; the job re-establishes tenant).
        _jobs.Enqueue<GenerateEInvoiceJob>(
            j => j.RunAsync(domainEvent.TenantId, domainEvent.DocumentId, CancellationToken.None));

        return Task.CompletedTask;
    }
}
