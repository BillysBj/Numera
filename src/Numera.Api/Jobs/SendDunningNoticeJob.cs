using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Renders and sends one tenant-scoped dunning notice on the Api default queue.</summary>
[AutomaticRetry(Attempts = 3)]
public sealed class SendDunningNoticeJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SendDunningNoticeJob> _logger;

    public SendDunningNoticeJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SendDunningNoticeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(
        Guid tenantId,
        Guid noticeId,
        string language,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<Numera.Platform.Db.NumeraDbContext>();
        var sender = services.GetRequiredService<IEmailSender>();
        var notice = await db.Set<DunningNotice>()
            .FirstOrDefaultAsync(x => x.Id == noticeId, cancellationToken)
            .ConfigureAwait(false);
        if (notice is null)
        {
            _logger.LogWarning(
                "Dunning send job: notice {NoticeId} not found for tenant {TenantId}.",
                noticeId, tenantId);
            return;
        }

        try
        {
            var document = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .Include(x => x.TaxBreakdown)
                .FirstAsync(x => x.Id == notice.DocumentId, cancellationToken)
                .ConfigureAwait(false);
            var config = await db.Set<DunningLevelConfig>()
                .AsNoTracking()
                .FirstAsync(x => x.Level == notice.Level, cancellationToken)
                .ConfigureAwait(false);
            var frozen = SnapshotReader.FromDocument(document, language: language);
            var pdf = await services.GetRequiredService<DunningNoticePdfService>()
                .GetOrRenderAsync(notice.Id, language, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Dunning notice {notice.Id} no longer exists.");
            var partnerEmail = document.PartnerId is { } partnerId
                ? await db.Set<BusinessPartner>()
                    .AsNoTracking()
                    .Where(x => x.Id == partnerId)
                    .Select(x => x.Email)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var recipient = string.IsNullOrWhiteSpace(partnerEmail)
                ? frozen.Recipient.Email
                : partnerEmail;
            if (string.IsNullOrWhiteSpace(recipient))
            {
                throw new InvalidOperationException(
                    $"No recipient e-mail is available for dunning notice {notice.Id}.");
            }

            var settings = await db.Set<TenantEmailSettings>().AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var content = TenantEmailTemplates.Dunning(settings, document, language, config.Name, notice.Fee);
            await sender.SendAsync(new EmailMessage
            {
                To = recipient,
                Subject = content.Subject,
                TextBody = content.TextBody,
                HtmlBody = content.HtmlBody,
                Attachment = new EmailAttachment(
                    $"{config.Name}-{document.DocumentNumber}.pdf",
                    pdf,
                    "application/pdf"),
            }, cancellationToken).ConfigureAwait(false);

            notice.Status = 1;
            notice.SentAt = DateTimeOffset.UtcNow;
            notice.LastError = null;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            notice.Status = 2;
            notice.LastError = ex.Message;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Failed to send dunning notice {NoticeId} for tenant {TenantId}.",
                noticeId,
                tenantId);
            throw;
        }
    }
}
