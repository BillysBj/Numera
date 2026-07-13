using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Email;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>
/// The Hangfire job that e-mails a finalized document's rendered PDF to the customer (Phase-4
/// DOCS-03, success criterion 2). Enqueued by <c>POST /api/documents/{id}/send</c> AFTER the
/// <see cref="DocumentEmail"/> row commits, it runs on the Api's default-queue Hangfire server
/// (which has the Sales/QuestPDF/MailKit references — RESEARCH.md Pitfall 2, LOCKED).
/// </summary>
/// <remarks>
/// Mirrors <see cref="RenderDocumentPdfJob"/> / <c>WelcomeEmailJob</c>: it opens its own DI scope
/// and calls <see cref="ICurrentTenant.SetTenant"/> so the <c>TenantConnectionInterceptor</c>
/// pushes <c>app.current_tenant</c> and every read/write runs under RLS. It renders-if-absent via
/// <see cref="DocumentPdfService.GetOrRender"/> so an e-mail NEVER goes out without the attachment
/// (RESEARCH.md Pitfall 5), sends via <see cref="IEmailSender"/>, then advances the
/// <see cref="DocumentEmail"/> row and flips <c>SalesDocument.SentAt</c> (a whitelisted lifecycle
/// column) on first success. On failure it records the error and rethrows so
/// <see cref="AutomaticRetryAttribute"/> retries transient SMTP faults with backoff.
/// </remarks>
[AutomaticRetry(Attempts = 3)]
public sealed class SendDocumentEmailJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SendDocumentEmailJob> _logger;

    /// <summary>Creates the job over the root scope factory (Hangfire activates it).</summary>
    public SendDocumentEmailJob(IServiceScopeFactory scopeFactory, ILogger<SendDocumentEmailJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Re-establishes tenant context for <paramref name="tenantId"/>, renders-if-absent the PDF for
    /// the document referenced by <paramref name="documentEmailId"/> in <paramref name="language"/>,
    /// sends it as an attachment, and advances the <see cref="DocumentEmail"/> row (+ flips
    /// <c>SalesDocument.SentAt</c> on first success) — all under RLS.
    /// </summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid documentEmailId,
        string language,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Re-establish the tenant the interceptor pushes into app.current_tenant (RLS-safe).
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<Numera.Platform.Db.NumeraDbContext>();
        var pdf = services.GetRequiredService<DocumentPdfService>();
        var sender = services.GetRequiredService<IEmailSender>();

        // Load the send record (RLS-scoped, tracked so we can advance it). A missing row means the
        // enqueue raced a delete or another tenant — nothing to do.
        var email = await db.Set<DocumentEmail>()
            .FirstOrDefaultAsync(e => e.Id == documentEmailId, cancellationToken)
            .ConfigureAwait(false);
        if (email is null)
        {
            _logger.LogWarning(
                "Send job: document_email {DocumentEmailId} not found for tenant {TenantId}.",
                documentEmailId, tenantId);
            return;
        }

        try
        {
            // Render-if-absent: NEVER send without an attachment (Pitfall 5).
            var render = await pdf.GetOrRender(email.DocumentId, language, cancellationToken)
                .ConfigureAwait(false);
            if (render.Result != DocumentPdfService.Outcome.Ok)
            {
                throw new InvalidOperationException(
                    $"Cannot send document {email.DocumentId}: render outcome was {render.Result}.");
            }

            var documentNumber = render.DocumentNumber!;
            var content = DocumentEmailTemplates.Build(language, documentNumber);

            await sender.SendAsync(
                new EmailMessage
                {
                    To = email.ToAddress,
                    Subject = email.Subject ?? content.Subject,
                    HtmlBody = content.HtmlBody,
                    TextBody = content.TextBody,
                    Attachment = new EmailAttachment(
                        $"{documentNumber}.pdf", render.PdfBytes!, "application/pdf"),
                },
                cancellationToken).ConfigureAwait(false);

            // Success: advance the send record and flip SalesDocument.SentAt on first send.
            var now = DateTimeOffset.UtcNow;
            email.Status = EmailStatus.Sent;
            email.SentAt = now;
            email.AttemptCount++;
            email.LastError = null;

            var doc = await db.Set<SalesDocument>()
                .FirstOrDefaultAsync(d => d.Id == email.DocumentId, cancellationToken)
                .ConfigureAwait(false);
            if (doc is not null && doc.SentAt is null)
            {
                // Whitelisted lifecycle columns (the sales_document_immutable trigger permits both).
                doc.SentAt = now;
                doc.Status = DocumentStatus.Sent;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Sent document {DocumentId} ({DocumentNumber}) to {ToAddress} for tenant {TenantId}.",
                email.DocumentId, documentNumber, email.ToAddress, tenantId);
        }
        catch (Exception ex)
        {
            // Record the failure, then rethrow so Hangfire's AutomaticRetry handles transient faults.
            email.Status = EmailStatus.Failed;
            email.AttemptCount++;
            email.LastError = ex.Message;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogError(
                ex, "Failed to send document {DocumentId} for tenant {TenantId} (attempt {Attempt}).",
                email.DocumentId, tenantId, email.AttemptCount);
            throw;
        }
    }
}
