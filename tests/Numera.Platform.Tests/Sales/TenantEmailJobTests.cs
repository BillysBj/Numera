using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class TenantEmailJobTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Jobs_use_current_tenant_templates_and_preserve_attachments(bool dunning)
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        var sent = new List<(EmailMessage Message, EmailOptions Options)>();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddScoped<SmtpPasswordProtector>();
        services.Configure<EmailOptions>(o => o.Host = "global.test");
        services.AddScoped<ITenantEmailConfigResolver, TenantEmailConfigResolver>();
        services.AddScoped<IEmailSender>(sp => new CaptureSender(sp.GetRequiredService<ITenantEmailConfigResolver>(), sent));
        services.AddScoped<DocumentPdfService>();
        services.AddScoped<DunningNoticePdfService>();
        await using var provider = services.BuildServiceProvider();
        var document = new SalesDocument
        {
            TenantId = tenantId, DocumentNumber = "RE-42", DocumentType = DocumentType.Rechnung,
            Status = DocumentStatus.Finalized, TotalGross = 119m, DueDate = new(2026, 10, 1),
            RecipientSnapshot = "{\"name\":\"<script>customer</script>\",\"email\":\"customer@example.com\"}",
            IssuerSnapshot = "{\"legalName\":\"Company\"}",
        };
        var email = new DocumentEmail
        {
            TenantId = tenantId, DocumentId = document.Id, ToAddress = "customer@example.com", Subject = "Old default",
        };
        var notice = new DunningNotice
        {
            TenantId = tenantId, DocumentId = document.Id, Level = 1, Fee = 5m, RenderedPdf = [4, 5, 6],
        };
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
            db.AddRange(document, email, notice,
                new DocumentRender { TenantId = tenantId, DocumentId = document.Id, DocumentNumber = "RE-42", Language = "de", PdfBytes = [1, 2, 3] },
                new DunningLevelConfig { TenantId = tenantId, Level = 1, Name = "Mahnung", TemplateTextDe = "Bitte zahlen.", TemplateTextEn = "Please pay." },
                new TenantEmailSettings
                {
                    TenantId = tenantId, Host = "tenant.test", FromAddress = "billing@tenant.test",
                    PasswordCiphertext = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>().Protect("tenant secret"),
                    InvoiceSubject = "Invoice {Rechnungsnummer}", InvoiceBody = "Hello {Kundenname}",
                    DunningSubject = "Reminder {Rechnungsnummer}", DunningBody = "Hello {Kundenname}, {Mahnstufe}",
                },
                new TenantEmailSettings { TenantId = Guid.NewGuid(), Host = "other.test", InvoiceBody = "Other tenant" });
            await db.SaveChangesAsync();
        }

        if (dunning)
            await new SendDunningNoticeJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SendDunningNoticeJob>.Instance)
                .RunAsync(tenantId, notice.Id, "de");
        else
            await new SendDocumentEmailJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SendDocumentEmailJob>.Instance)
                .RunAsync(tenantId, email.Id, "de", false);

        var captured = Assert.Single(sent);
        Assert.Equal("tenant.test", captured.Options.Host);
        Assert.Equal("tenant secret", captured.Options.Password);
        Assert.Equal(dunning ? "Reminder RE-42" : "Invoice RE-42", captured.Message.Subject);
        Assert.Contains("<script>customer</script>", captured.Message.TextBody);
        Assert.DoesNotContain("<script>", captured.Message.HtmlBody);
        Assert.Contains("&lt;script&gt;", captured.Message.HtmlBody);
        Assert.Equal(dunning ? new byte[] { 4, 5, 6 } : [1, 2, 3], captured.Message.Attachment!.Content);
        Assert.Equal(dunning ? "Mahnung-RE-42.pdf" : "RE-42.pdf", captured.Message.Attachment.FileName);
        Assert.Equal("application/pdf", captured.Message.Attachment.ContentType);
        using var verification = provider.CreateScope();
        verification.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);
        var read = verification.ServiceProvider.GetRequiredService<NumeraDbContext>();
        if (dunning)
            Assert.Equal(1, (await read.Set<DunningNotice>().SingleAsync()).Status);
        else
        {
            var record = await read.Set<DocumentEmail>().SingleAsync();
            Assert.Equal(EmailStatus.Sent, record.Status);
            Assert.Equal(captured.Message.Subject, record.Subject);
        }
    }

    [Fact]
    public async Task MailKit_resolves_configuration_on_each_send_without_silently_falling_back()
    {
        var resolver = new FailingResolver();
        var sender = new MailKitEmailSender(Options.Create(new EmailOptions()), resolver);
        var message = new EmailMessage { To = "test@example.com", Subject = "test" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(message));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(message));
        Assert.Equal(2, resolver.Calls);
    }

    private sealed class CaptureSender(ITenantEmailConfigResolver resolver,
        List<(EmailMessage Message, EmailOptions Options)> messages) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            messages.Add((message, await resolver.ResolveAsync(cancellationToken)));
    }

    private sealed class FailingResolver : ITenantEmailConfigResolver
    {
        public int Calls { get; private set; }
        public Task<EmailOptions> ResolveAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Undecryptable tenant configuration");
        }
    }
}
