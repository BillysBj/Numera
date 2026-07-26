using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Email;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The Phase-4 send hard gate (DOCS-03, success criterion 2): proves end-to-end on real
/// postgres:18 (as the non-BYPASSRLS <c>numera_app</c> role) + a real Mailpit SMTP container that
/// a finalized document can be e-mailed to the customer with its rendered §14 PDF attached — the
/// message is actually captured by Mailpit (asserted via its HTTP API), the <c>document_email</c>
/// row advances to <see cref="EmailStatus.Sent"/> and <c>SalesDocument.SentAt</c> flips.
/// </summary>
/// <remarks>
/// The finalize runs through <see cref="SalesTestData.FinalizeAsync"/> (the shipped
/// <c>FinalizeCoreAsync</c> via the 03-11 InternalsVisibleTo seam), so a genuine frozen snapshot
/// exists. The send runs through the ACTUAL <see cref="SendDocumentEmailJob"/> over a minimal DI
/// container (scoped tenant + DbContext + <see cref="DocumentPdfService"/> + the real
/// <see cref="MailKitEmailSender"/> pointed at the Mailpit container) — its own scope +
/// <see cref="ICurrentTenant.SetTenant"/>, exactly like production, so RLS applies and MailKit
/// really sends over SMTP.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DocumentEmailSendTests
{
    static DocumentEmailSendTests()
    {
        // QuestPDF throws on first render without an acknowledged license (Pitfall 1) — the send
        // job renders-if-absent, so the license must be set here too.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private readonly PostgresFixture _fixture;

    public DocumentEmailSendTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Send_delivers_the_pdf_to_mailpit_and_flips_document_email_and_document_sentat()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);
        const string recipient = "kunde@example.com";

        // A real Mailpit SMTP sink (1025) + HTTP API (8025), started for this test only.
        await using var mailpit = new ContainerBuilder("axllent/mailpit:latest")
            .WithPortBinding(1025, assignRandomHostPort: true)
            .WithPortBinding(8025, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/messages")))
            .Build();
        await mailpit.StartAsync();

        var smtpHost = mailpit.Hostname;
        var smtpPort = mailpit.GetMappedPublicPort(1025);
        var apiBase = $"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}";

        // Finalize a real invoice → RE-2026-00001 with a genuine frozen snapshot + BG-23 breakdown.
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        // Record a Queued document_email row exactly as POST /api/documents/{id}/send would.
        Guid emailId;
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            var email = new DocumentEmail
            {
                TenantId = tenant,
                DocumentId = docId,
                ToAddress = recipient,
                Subject = "Rechnung RE-2026-00001",
                Status = EmailStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Add(email);
            await db.SaveChangesAsync();
            emailId = email.Id;
        }

        // Run the REAL send job (fresh scope + tenant re-establishment + MailKit → Mailpit).
        await RunSendJobAsync(tenant, emailId, smtpHost, smtpPort);

        // (1) Mailpit received exactly one message, to the recipient, with the expected subject.
        using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
        var (messageId, toAddress, subject) = await WaitForSingleMessageAsync(http);
        Assert.Equal(recipient, toAddress);
        Assert.Equal("Rechnung RE-2026-00001", subject);

        // (2) …and it carries the rendered PDF attachment named {documentNumber}.pdf.
        var detailJson = await http.GetStringAsync($"/api/v1/message/{messageId}");
        using var detail = JsonDocument.Parse(detailJson);
        var attachments = detail.RootElement.GetProperty("Attachments");
        var attachment = Assert.Single(attachments.EnumerateArray());
        Assert.Equal("RE-2026-00001.pdf", attachment.GetProperty("FileName").GetString());
        Assert.Contains(
            "pdf",
            attachment.GetProperty("ContentType").GetString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        // (3) document_email flipped to Sent (SentAt set, one attempt), SalesDocument.SentAt is set.
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var email = await read.Set<DocumentEmail>().AsNoTracking().FirstAsync(e => e.Id == emailId);
            Assert.Equal(EmailStatus.Sent, email.Status);
            Assert.NotNull(email.SentAt);
            Assert.Equal(1, email.AttemptCount);
            Assert.Null(email.LastError);

            var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.NotNull(doc.SentAt);
            Assert.Equal(DocumentStatus.Sent, doc.Status);
        }

        // (4) document_email is RLS-isolated — another tenant sees none of these rows.
        await using (var readOther = _fixture.CreateAppContext(otherTenant))
        {
            Assert.Equal(0, await readOther.Set<DocumentEmail>().IgnoreQueryFilters()
                .CountAsync(e => e.DocumentId == docId));
        }
    }

    // Runs the REAL SendDocumentEmailJob over a minimal DI container mirroring the Api host: a
    // scoped ICurrentTenant + NumeraDbContext (self-registers the tenant interceptor) +
    // DocumentPdfService + the real MailKitEmailSender pointed at the Mailpit container. The job
    // opens its own scope and SetTenant, so RLS applies inside it exactly as in production.
    private async Task RunSendJobAsync(Guid tenant, Guid emailId, string smtpHost, int smtpPort)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(o => o.UseNpgsql(_fixture.AppConnectionString));
        services.AddScoped<DocumentPdfService>();
        services.Configure<EmailOptions>(o =>
        {
            o.Host = smtpHost;
            o.Port = smtpPort;
            o.UseSsl = false;
            o.FromAddress = "noreply@numera.local";
            o.FromName = "Numera";
        });
        services.AddScoped<IEmailSender, MailKitEmailSender>();

        await using var provider = services.BuildServiceProvider();
        var job = new SendDocumentEmailJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SendDocumentEmailJob>.Instance);

        await job.RunAsync(tenant, emailId, "de", false, CancellationToken.None);
    }

    // Polls Mailpit's HTTP API until exactly one message is present (SMTP delivery completes when
    // MailKit's SendAsync returns, so this is near-instant; the short poll only guards jitter).
    private static async Task<(string Id, string? To, string? Subject)> WaitForSingleMessageAsync(HttpClient http)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var listJson = await http.GetStringAsync("/api/v1/messages");
            using var doc = JsonDocument.Parse(listJson);
            var messages = doc.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() == 1)
            {
                var msg = messages[0];
                var to = msg.GetProperty("To")[0].GetProperty("Address").GetString();
                return (msg.GetProperty("ID").GetString()!, to, msg.GetProperty("Subject").GetString());
            }

            Assert.True(messages.GetArrayLength() <= 1, "Mailpit received more than one message.");
            await Task.Delay(250);
        }

        throw new Xunit.Sdk.XunitException("Mailpit did not receive the message within the timeout.");
    }
}
