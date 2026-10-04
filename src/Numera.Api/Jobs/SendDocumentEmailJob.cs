using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
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
    /// Re-establishes tenant context for <paramref name="tenantId"/>, resolves the attachment for
    /// the document referenced by <paramref name="documentEmailId"/> in <paramref name="language"/>
    /// (the rendered PDF, or — when <paramref name="asEInvoice"/> is set — the stored/generated
    /// XRechnung XML), sends it, and advances the <see cref="DocumentEmail"/> row (+ flips
    /// <c>SalesDocument.SentAt</c> on first success) — all under RLS.
    /// </summary>
    /// <param name="tenantId">The tenant whose context is re-established so RLS applies inside the job.</param>
    /// <param name="documentEmailId">The Queued <see cref="DocumentEmail"/> row to send + advance.</param>
    /// <param name="language">The covering-e-mail language ("de"/"en").</param>
    /// <param name="asEInvoice">
    /// When true, the attachment is the KoSIT-validated XRechnung (UBL) XML from
    /// <c>document_einvoice</c> instead of the §14 PDF — the e-invoice Versand (EINV-03). The
    /// endpoint's stage-2 gate has already confirmed the stored verdict is Accepted before enqueue.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(
        Guid tenantId,
        Guid documentEmailId,
        string language,
        bool asEInvoice,
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
            // Resolve the attachment (generate/render-if-absent): NEVER send without one (Pitfall 5).
            string documentNumber;
            EmailAttachment attachment;
            if (asEInvoice)
            {
                var einvoice = services.GetRequiredService<EInvoiceService>();
                var xml = await einvoice.GetOrGenerate(email.DocumentId, EInvoiceFormat.XRechnungUbl, cancellationToken)
                    .ConfigureAwait(false);
                if (xml.Result != EInvoiceService.Outcome.Ok)
                {
                    throw new InvalidOperationException(
                        $"Cannot send e-invoice for document {email.DocumentId}: generate outcome was {xml.Result}.");
                }

                documentNumber = await ResolveDocumentNumberAsync(db, email.DocumentId, cancellationToken)
                    .ConfigureAwait(false);
                attachment = new EmailAttachment(xml.FileName!, xml.Xml!, "application/xml");
            }
            else
            {
                var einvoice = services.GetRequiredService<EInvoiceService>();
                EInvoiceService.XmlResult zugferd = default;
                try
                {
                    zugferd = await einvoice.GetOrGenerate(email.DocumentId, EInvoiceFormat.ZugferdPdfA3, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "ZUGFeRD unavailable for document {DocumentId}; falling back to the rendered PDF.",
                        email.DocumentId);
                }

                if (zugferd.Result == EInvoiceService.Outcome.Ok && zugferd.Xml is { Length: > 0 })
                {
                    documentNumber = await ResolveDocumentNumberAsync(db, email.DocumentId, cancellationToken)
                        .ConfigureAwait(false);
                    attachment = new EmailAttachment($"{documentNumber}.pdf", zugferd.Xml, "application/pdf");
                }
                else
                {
                    var render = await pdf.GetOrRender(email.DocumentId, language, cancellationToken)
                        .ConfigureAwait(false);
                    if (render.Result != DocumentPdfService.Outcome.Ok)
                    {
                        throw new InvalidOperationException(
                            $"Cannot send document {email.DocumentId}: render outcome was {render.Result}.");
                    }

                    documentNumber = render.DocumentNumber!;
                    attachment = new EmailAttachment($"{documentNumber}.pdf", render.PdfBytes!, "application/pdf");
                }
            }

            var document = await db.Set<SalesDocument>().AsNoTracking()
                .FirstAsync(d => d.Id == email.DocumentId, cancellationToken).ConfigureAwait(false);
            var settings = await db.Set<TenantEmailSettings>().AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var content = TenantEmailTemplates.Invoice(settings, document, language, documentNumber);

            await sender.SendAsync(
                new EmailMessage
                {
                    To = email.ToAddress,
                    Subject = content.Subject,
                    HtmlBody = content.HtmlBody,
                    TextBody = content.TextBody,
                    Attachment = attachment,
                },
                cancellationToken).ConfigureAwait(false);

            // Success: advance the send record and flip SalesDocument.SentAt on first send.
            var now = DateTimeOffset.UtcNow;
            email.Status = EmailStatus.Sent;
            email.SentAt = now;
            email.AttemptCount++;
            email.LastError = null;
            email.Subject = content.Subject;

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

    // Reads the finalized document's legal number (RLS-scoped) for the covering e-mail's subject/body.
    private static async Task<string> ResolveDocumentNumberAsync(
        Numera.Platform.Db.NumeraDbContext db,
        Guid documentId,
        CancellationToken ct)
    {
        var number = await db.Set<SalesDocument>()
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => d.DocumentNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return number ?? documentId.ToString();
    }
}
