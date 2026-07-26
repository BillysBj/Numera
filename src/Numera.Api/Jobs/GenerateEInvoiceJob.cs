using Hangfire;

using Numera.Api.Services;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>
/// The Hangfire job that generates + validates a finalized invoice's XRechnung off the finalize
/// hot-path and stores it in <c>document_einvoice</c> (Phase-5 EINV-01/EINV-03). It is enqueued
/// by <c>EnqueueEInvoiceOnFinalize</c> after the finalize transaction commits, and runs on the
/// Api's default-queue Hangfire server — NOT the "worker" queue, whose host lacks the
/// Sales/ZUGFeRD references (RESEARCH.md Pitfall 2, LOCKED).
/// </summary>
/// <remarks>
/// Mirrors <see cref="RenderDocumentPdfJob"/> VERBATIM: it opens its own DI scope and calls
/// <see cref="ICurrentTenant.SetTenant"/> so the <c>TenantConnectionInterceptor</c> pushes
/// <c>app.current_tenant</c> when the scoped <c>NumeraDbContext</c> opens its connection — the
/// read (frozen snapshot) and the store therefore run under RLS exactly as a request.
/// <see cref="AutomaticRetryAttribute"/> covers transient faults; each format is generated
/// idempotently (replace per document+format), so a retry never duplicates.
/// <para>
/// It iterates the SHARED <see cref="EInvoiceService.FinalizeFormats"/> list (UBL + CII in this
/// plan) rather than hardcoding the syntaxes — that is what lets 05-04 add ZUGFeRD PDF/A-3 to the
/// eager set by editing ONLY that list, with no change to this job.
/// </para>
/// </remarks>
[AutomaticRetry(Attempts = 3)]
public sealed class GenerateEInvoiceJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenerateEInvoiceJob> _logger;

    /// <summary>Creates the job over the root scope factory (Hangfire activates it).</summary>
    public GenerateEInvoiceJob(IServiceScopeFactory scopeFactory, ILogger<GenerateEInvoiceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Re-establishes tenant context for <paramref name="tenantId"/> in a fresh scope, then
    /// generates + validates + stores one <c>document_einvoice</c> artifact per
    /// <see cref="EInvoiceService.FinalizeFormats"/> entry for <paramref name="documentId"/>
    /// (RLS applies inside the job).
    /// </summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Re-establish the tenant the interceptor pushes into app.current_tenant (RLS-safe).
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var svc = scope.ServiceProvider.GetRequiredService<EInvoiceService>();

        foreach (var format in EInvoiceService.FinalizeFormats)
        {
            var artifact = await svc.GenerateAndValidate(documentId, format, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Generated {Format} e-invoice for document {DocumentId} ({DocumentNumber}, {ByteSize} bytes): {Status}.",
                format, documentId, artifact.DocumentNumber, artifact.ByteSize, artifact.ValidationStatus);
        }
    }
}
