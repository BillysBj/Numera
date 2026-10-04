using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class TenantEmailJobTests
{
    static TenantEmailJobTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    [Theory]
    [InlineData(false, "generated")]
    [InlineData(false, "cached")]
    [InlineData(false, "non-ok")]
    [InlineData(false, "empty")]
    [InlineData(false, "throws")]
    [InlineData(true, "dunning")]
    public async Task Jobs_use_current_tenant_templates_and_preserve_attachments(bool dunning, string artifact)
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
        var validator = new TestEInvoiceValidator { Throws = artifact == "throws" };
        services.AddSingleton<IEInvoiceValidator>(validator);
        services.AddScoped<EInvoiceService>();
        services.AddScoped<DunningNoticePdfService>();
        await using var provider = services.BuildServiceProvider();
        var document = new SalesDocument
        {
            TenantId = tenantId, DocumentNumber = "RE-42", DocumentType = DocumentType.Rechnung,
            // A stored plain render lets the non-OK case exercise recovery when no hybrid is available.
            Status = artifact == "non-ok" ? DocumentStatus.Draft : DocumentStatus.Finalized,
            DocumentDate = new(2026, 9, 1), TotalGross = 119m, DueDate = new(2026, 10, 1),
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
            if (artifact == "cached")
            {
                var generated = await scope.ServiceProvider.GetRequiredService<EInvoiceService>()
                    .GetOrGenerate(document.Id, EInvoiceFormat.ZugferdPdfA3, CancellationToken.None);
                Assert.Equal(EInvoiceService.Outcome.Ok, generated.Result);
            }
            else if (artifact == "empty")
            {
                db.Add(new EInvoiceArtifact
                {
                    TenantId = tenantId, DocumentId = document.Id, DocumentNumber = "RE-42",
                    Format = EInvoiceFormat.ZugferdPdfA3, Xml = [],
                });
                await db.SaveChangesAsync();
            }
            else if (artifact == "non-ok")
            {
                var unavailable = await scope.ServiceProvider.GetRequiredService<EInvoiceService>()
                    .GetOrGenerate(document.Id, EInvoiceFormat.ZugferdPdfA3, CancellationToken.None);
                Assert.Equal(EInvoiceService.Outcome.NotFinalized, unavailable.Result);
            }
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
        Assert.NotNull(captured.Message.Attachment);
        Assert.Equal(dunning ? "Mahnung-RE-42.pdf" : "RE-42.pdf", captured.Message.Attachment.FileName);
        Assert.Equal("application/pdf", captured.Message.Attachment.ContentType);
        using var verification = provider.CreateScope();
        verification.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);
        var read = verification.ServiceProvider.GetRequiredService<NumeraDbContext>();
        if (artifact is "generated" or "cached")
        {
            // Require the job to have stored/used the hybrid, not silently fallen back to the plain render.
            var stored = await read.Set<EInvoiceArtifact>().SingleAsync(a => a.Format == EInvoiceFormat.ZugferdPdfA3);
            var hybrid = await verification.ServiceProvider.GetRequiredService<EInvoiceService>()
                .GetOrGenerate(document.Id, EInvoiceFormat.ZugferdPdfA3, CancellationToken.None);
            Assert.Equal(EInvoiceService.Outcome.Ok, hybrid.Result);
            Assert.Equal(stored.Xml, hybrid.Xml);
            Assert.Equal(hybrid.Xml, captured.Message.Attachment.Content);
            Assert.Equal("%PDF"u8.ToArray(), captured.Message.Attachment.Content[..4]);
        }
        else
        {
            Assert.Equal(dunning ? new byte[] { 4, 5, 6 } : [1, 2, 3], captured.Message.Attachment.Content);
        }
        if (artifact == "throws")
            Assert.Equal(1, validator.Calls);
        if (dunning)
            Assert.Equal(1, (await read.Set<DunningNotice>().SingleAsync()).Status);
        else
        {
            var record = await read.Set<DocumentEmail>().SingleAsync();
            Assert.Equal(EmailStatus.Sent, record.Status);
            Assert.Equal(captured.Message.Subject, record.Subject);
            Assert.Equal(1, record.AttemptCount);
            Assert.Null(record.LastError);
            Assert.NotNull(record.SentAt);
            var sentDocument = await read.Set<SalesDocument>().SingleAsync();
            Assert.Equal(DocumentStatus.Sent, sentDocument.Status);
            Assert.NotNull(sentDocument.SentAt);
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

    private sealed class TestEInvoiceValidator : IEInvoiceValidator
    {
        public bool Throws { get; init; }
        public int Calls { get; private set; }

        public Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throws)
                throw new InvalidOperationException("ZUGFeRD validation unavailable");
            return Task.FromResult(new EInvoiceValidationResult(EInvoiceValidationStatus.Accepted, [], null));
        }
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
