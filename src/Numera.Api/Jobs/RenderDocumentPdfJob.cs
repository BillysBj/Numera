using Hangfire;

using Numera.Api.Services;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>
/// The Hangfire job that renders a finalized document's §14 PDF off the finalize hot-path and
/// stores it in <c>document_render</c> (Phase-4 DOCS-02, success criterion 3). It is enqueued by
/// <c>EnqueuePdfOnFinalize</c> after the finalize transaction commits, and runs on the Api's
/// default-queue Hangfire server — NOT the "worker" queue, whose host lacks the Sales/QuestPDF
/// references (RESEARCH.md Pitfall 2, LOCKED).
/// </summary>
/// <remarks>
/// Mirrors <see cref="WelcomeEmailJob"/> VERBATIM: it opens its own DI scope and calls
/// <see cref="ICurrentTenant.SetTenant"/> so the <c>TenantConnectionInterceptor</c> pushes
/// <c>app.current_tenant</c> when the scoped <c>NumeraDbContext</c> opens its connection — the
/// read (frozen snapshot + live logo) and the store therefore run under RLS exactly as a request.
/// <see cref="AutomaticRetryAttribute"/> covers transient faults; the render is idempotent
/// (replace per document+language), so a retry never duplicates.
/// </remarks>
[AutomaticRetry(Attempts = 3)]
public sealed class RenderDocumentPdfJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RenderDocumentPdfJob> _logger;

    /// <summary>Creates the job over the root scope factory (Hangfire activates it).</summary>
    public RenderDocumentPdfJob(IServiceScopeFactory scopeFactory, ILogger<RenderDocumentPdfJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Re-establishes tenant context for <paramref name="tenantId"/> in a fresh scope, then
    /// renders + stores the PDF for <paramref name="documentId"/> in <paramref name="language"/>
    /// via <see cref="DocumentPdfService.RenderAndStore"/> (RLS applies inside the job).
    /// </summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid documentId,
        string language,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Re-establish the tenant the interceptor pushes into app.current_tenant (RLS-safe).
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var pdf = scope.ServiceProvider.GetRequiredService<DocumentPdfService>();
        var render = await pdf.RenderAndStore(documentId, language, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Rendered PDF for document {DocumentId} ({DocumentNumber}, {ByteSize} bytes) of tenant {TenantId}.",
            documentId, render.DocumentNumber, render.ByteSize, tenantId);
    }
}
