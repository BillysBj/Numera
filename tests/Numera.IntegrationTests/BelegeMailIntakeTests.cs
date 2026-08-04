using System.Text;
using System.Diagnostics;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MimeKit;
using MimeKit.Utils;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

public sealed class GreenMailFixture : IAsyncLifetime
{
    private readonly IContainer _greenMail = new ContainerBuilder("greenmail/standalone:2.1.3")
        .WithEnvironment(
            "GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled")
        .WithPortBinding(3143, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3143))
        .Build();

    public string Host => _greenMail.Hostname;

    public ushort ImapPort => _greenMail.GetMappedPublicPort(3143);

    public async Task InitializeAsync() => await _greenMail.StartAsync();

    public async Task DisposeAsync() => await _greenMail.DisposeAsync();
}

/// <summary>GreenMail IMAP + real-Postgres proof of per-tenant receipt mail intake.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BelegeMailIntakeTests(PostgresFixture fixture, GreenMailFixture greenMail)
    : IClassFixture<GreenMailFixture>
{
    private const string InboxPassword = "secret";
    private readonly GreenMailFixture _greenMail = greenMail;

    [Fact]
    public async Task Benign_pdf_is_routed_archived_audited_and_recorded()
    {
        var mailbox = MailboxIdentity.Create();
        var tenant = Guid.CreateVersion7();
        var token = await SeedMailboxAsync(tenant);
        var testMarker = Guid.NewGuid().ToString("N");
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\nNumera mail receipt {testMarker}\n%%EOF");
        var contentHash = new ReceiptDeduplicator().ComputeHash(pdf);
        var messageId = MimeUtils.GenerateMessageId(mailbox.Domain);
        await DeliverAsync(
            _greenMail, mailbox, token, messageId, "receipt.pdf", "application/pdf", pdf);
        await WaitForImapMessageAsync(_greenMail, mailbox, messageId);

        await using var provider = BuildProvider(_greenMail, mailbox);
        await provider.GetRequiredService<PollBelegMailboxJob>().RunAsync();
        await WaitForIngestCommitAsync(tenant, messageId, contentHash);

        Guid receiptId;
        await using (var read = fixture.CreateAppContext(tenant))
        {
            var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync(
                candidate => candidate.Source == ReceiptSource.Email
                    && candidate.ContentHash == contentHash);
            var archive = await read.Set<ReceiptArchive>().AsNoTracking().SingleAsync(
                candidate => candidate.ReceiptId == receipt.Id
                    && candidate.ContentHash == contentHash);
            var processed = await read.Set<ProcessedBelegeMail>().AsNoTracking().SingleAsync(
                candidate => candidate.MessageId == messageId);
            Assert.Equal(ReceiptSource.Email, receipt.Source);
            Assert.Equal(ReceiptStatus.Captured, receipt.Status);
            Assert.Equal(archive.Id, receipt.ArchiveId);
            Assert.Equal(pdf, archive.OriginalBytes);
            Assert.Null(archive.UploadedByUserId);
            Assert.Equal(messageId, processed.MessageId);
            Assert.NotNull(processed.ContentHash);
            Assert.Contains(
                await read.Set<AuditEvent>().AsNoTracking()
                    .Where(audit => audit.EntityId == receipt.Id)
                    .ToListAsync(),
                audit => audit.ActorUserId == BelegeMailboxSystemCurrentUserId);
            receiptId = receipt.Id;
        }

        var extract = new ExtractReceiptJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExtractReceiptJob>.Instance);
        await extract.RunAsync(tenant, receiptId);
        await using var extractedRead = fixture.CreateAppContext(tenant);
        Assert.Equal(
            ReceiptStatus.Extracted,
            await extractedRead.Set<Receipt>()
                .Where(receipt => receipt.Id == receiptId)
                .Select(receipt => receipt.Status)
                .SingleAsync());
        await PurgeInboxAsync(_greenMail, mailbox);
    }

    [Fact]
    public async Task ReDelivered_message_id_does_not_create_a_second_receipt()
    {
        var mailbox = MailboxIdentity.Create();
        var tenant = Guid.CreateVersion7();
        var token = await SeedMailboxAsync(tenant);
        var testMarker = Guid.NewGuid().ToString("N");
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\nidempotent mail receipt {testMarker}\n%%EOF");
        var contentHash = new ReceiptDeduplicator().ComputeHash(pdf);
        var messageId = MimeUtils.GenerateMessageId(mailbox.Domain);
        await using var provider = BuildProvider(_greenMail, mailbox);

        await DeliverAsync(_greenMail, mailbox, token, messageId, "same.pdf", "application/pdf", pdf);
        await WaitForImapMessageAsync(_greenMail, mailbox, messageId);
        await provider.GetRequiredService<PollBelegMailboxJob>().RunAsync();
        // The first delivery must be durably recorded before the same Message-Id is sent again.
        await WaitForIngestCommitAsync(tenant, messageId, contentHash);
        await PurgeInboxAsync(_greenMail, mailbox);
        await DeliverAsync(_greenMail, mailbox, token, messageId, "same.pdf", "application/pdf", pdf);
        await WaitForImapMessageAsync(_greenMail, mailbox, messageId);
        await provider.GetRequiredService<PollBelegMailboxJob>().RunAsync();
        await WaitForIdempotentStateAsync(tenant, messageId, contentHash);

        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(
            1,
            await read.Set<Receipt>().CountAsync(
                candidate => candidate.Source == ReceiptSource.Email
                    && candidate.ContentHash == contentHash));
        Assert.Equal(
            1,
            await read.Set<ReceiptArchive>().CountAsync(
                candidate => candidate.ContentHash == contentHash));
        Assert.Equal(
            1,
            await read.Set<ProcessedBelegeMail>().CountAsync(
                candidate => candidate.MessageId == messageId));
        await PurgeInboxAsync(_greenMail, mailbox);
    }

    [Fact]
    public async Task Tenant_b_token_is_invisible_to_tenant_a_under_rls()
    {
        var mailbox = MailboxIdentity.Create();
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        _ = await SeedMailboxAsync(tenantA);
        var tokenB = await SeedMailboxAsync(tenantB);
        var testMarker = Guid.NewGuid().ToString("N");
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\ntenant B only {testMarker}\n%%EOF");
        var contentHash = new ReceiptDeduplicator().ComputeHash(pdf);
        var messageId = MimeUtils.GenerateMessageId(mailbox.Domain);
        await DeliverAsync(
            _greenMail,
            mailbox,
            tokenB,
            messageId,
            "tenant-b.pdf",
            "application/pdf",
            pdf);
        await WaitForImapMessageAsync(_greenMail, mailbox, messageId);

        await using var provider = BuildProvider(_greenMail, mailbox);
        await provider.GetRequiredService<PollBelegMailboxJob>().RunAsync();
        await WaitForIngestCommitAsync(tenantB, messageId, contentHash);

        await using (var readB = fixture.CreateAppContext(tenantB))
        {
            Assert.Equal(
                1,
                await readB.Set<Receipt>().CountAsync(
                    candidate => candidate.Source == ReceiptSource.Email
                        && candidate.ContentHash == contentHash));
            Assert.Equal(
                1,
                await readB.Set<ProcessedBelegeMail>().CountAsync(
                    candidate => candidate.MessageId == messageId));
        }

        await using var readA = fixture.CreateAppContext(tenantA);
        Assert.Equal(
            0,
            await readA.Set<Receipt>()
                .IgnoreQueryFilters()
                .CountAsync(receipt => receipt.Source == ReceiptSource.Email
                    && receipt.ContentHash == contentHash));
        await PurgeInboxAsync(_greenMail, mailbox);
    }

    [Fact]
    public async Task Eicar_attachment_is_quarantined_without_a_bookable_archive()
    {
        var mailbox = MailboxIdentity.Create();
        var tenant = Guid.CreateVersion7();
        var token = await SeedMailboxAsync(tenant);
        var eicar = Encoding.ASCII.GetBytes(
            $"X5O!P%@AP[4\\PZX54(P^)7CC)7}}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*-{Guid.NewGuid():N}");
        var contentHash = new ReceiptDeduplicator().ComputeHash(eicar);
        var messageId = MimeUtils.GenerateMessageId(mailbox.Domain);
        await DeliverAsync(
            _greenMail,
            mailbox,
            token,
            messageId,
            "eicar.pdf",
            "application/pdf",
            eicar);
        await WaitForImapMessageAsync(_greenMail, mailbox, messageId);

        await using var provider = BuildProvider(_greenMail, mailbox);
        await provider.GetRequiredService<PollBelegMailboxJob>().RunAsync();
        await WaitForIngestCommitAsync(tenant, messageId, contentHash);

        await using var read = fixture.CreateAppContext(tenant);
        var receipt = await read.Set<Receipt>().AsNoTracking().SingleAsync(
            candidate => candidate.Source == ReceiptSource.Email
                && candidate.ContentHash == contentHash);
        Assert.Equal(ReceiptStatus.Quarantined, receipt.Status);
        Assert.Null(receipt.ArchiveId);
        Assert.Null(receipt.JournalEntryId);
        Assert.Equal(
            0,
            await read.Set<ReceiptArchive>().CountAsync(
                candidate => candidate.ContentHash == contentHash));
        Assert.Equal(
            1,
            await read.Set<ProcessedBelegeMail>().CountAsync(
                candidate => candidate.MessageId == messageId));
        await PurgeInboxAsync(_greenMail, mailbox);
    }

    private static Guid BelegeMailboxSystemCurrentUserId =>
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private async Task<string> SeedMailboxAsync(Guid tenantId)
    {
        var token = TenantBelegeMailbox.GenerateAddressToken();
        await using var db = fixture.CreateAppContext(tenantId);
        db.Add(new TenantBelegeMailbox
        {
            TenantId = tenantId,
            AddressToken = token,
            LocalPart = TenantBelegeMailbox.BuildLocalPart(token),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return token;
    }

    private ServiceProvider BuildProvider(GreenMailFixture greenMail, MailboxIdentity mailbox)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddScoped<ICurrentUser, BelegeMailboxSystemCurrentUser>();
        services.AddDbContext<NumeraDbContext>(options =>
            options.UseNpgsql(fixture.AppConnectionString));
        services.AddScoped<IAttachmentScanner, NoopAttachmentScanner>();
        services.AddScoped<ReceiptDeduplicator>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IBackgroundJobClient, RecordingJobClient>();
        services.AddScoped<ReceiptIngestService>();
        services.AddScoped<IReceiptExtractor, StubReceiptExtractor>();
        services.AddScoped<SupplierMatcher>();
        services.Configure<BelegeMailboxOptions>(options =>
        {
            options.Host = greenMail.Host;
            options.Port = greenMail.ImapPort;
            options.UseSsl = false;
            options.Username = mailbox.InboxUser;
            options.Password = InboxPassword;
            options.Domain = mailbox.Domain;
        });
        services.AddTransient<ImapClient>();
        services.AddTransient<PollBelegMailboxJob>();
        return services.BuildServiceProvider();
    }

    private static async Task DeliverAsync(
        GreenMailFixture greenMail,
        MailboxIdentity mailbox,
        string token,
        string messageId,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        var message = new MimeMessage
        {
            MessageId = messageId,
            Subject = "Weitergeleiteter Beleg",
        };
        message.From.Add(MailboxAddress.Parse("supplier@example.test"));
        message.To.Add(MailboxAddress.Parse($"belege-{token}@{mailbox.Domain}"));
        var body = new BodyBuilder { TextBody = "Beleg im Anhang" };
        body.Attachments.Add(fileName, bytes, ContentType.Parse(contentType));
        message.Body = body.ToMessageBody();

        // Inject the message straight into the polled INBOX via IMAP APPEND. APPEND is
        // synchronous, so there is no asynchronous SMTP->IMAP delivery race (the sole
        // source of prior flakiness). The message lands UNSEEN, exactly as the poll expects.
        using var imap = await OpenInboxAsync(greenMail, mailbox, FolderAccess.ReadWrite);
        await imap.Inbox.AppendAsync(message, MessageFlags.None);
        await imap.DisconnectAsync(true);
    }

    private static Task WaitForImapMessageAsync(
        GreenMailFixture greenMail,
        MailboxIdentity mailbox,
        string messageId)
        => WaitUntilAsync(
            async () =>
            {
                using var imap = await OpenInboxAsync(
                    greenMail,
                    mailbox,
                    FolderAccess.ReadOnly);
                var messages = await imap.Inbox.SearchAsync(SearchQuery.All);
                foreach (var uid in messages)
                {
                    var message = await imap.Inbox.GetMessageAsync(uid);
                    if (string.Equals(
                            message.MessageId,
                            messageId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await imap.DisconnectAsync(true);
                        return true;
                    }
                }

                await imap.DisconnectAsync(true);
                return false;
            },
            TimeSpan.FromSeconds(10),
            $"Message {messageId} did not become visible in GreenMail IMAP.");

    private Task WaitForIngestCommitAsync(
        Guid tenantId,
        string messageId,
        string contentHash)
        => WaitUntilAsync(
            async () =>
            {
                await using var db = fixture.CreateAppContext(tenantId);
                var receiptExists = await db.Set<Receipt>()
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.Source == ReceiptSource.Email
                        && candidate.ContentHash == contentHash);
                var processedExists = await db.Set<ProcessedBelegeMail>()
                    .AsNoTracking()
                    .AnyAsync(candidate => candidate.MessageId == messageId);
                return receiptExists && processedExists;
            },
            TimeSpan.FromSeconds(10),
            $"Receipt and processed-mail row for {messageId} were not visible for tenant {tenantId}.");

    private Task WaitForIdempotentStateAsync(
        Guid tenantId,
        string messageId,
        string contentHash)
        => WaitUntilAsync(
            async () =>
            {
                await using var db = fixture.CreateAppContext(tenantId);
                var receiptCount = await db.Set<Receipt>()
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.Source == ReceiptSource.Email
                        && candidate.ContentHash == contentHash);
                var processedCount = await db.Set<ProcessedBelegeMail>()
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.MessageId == messageId);
                return receiptCount == 1 && processedCount == 1;
            },
            TimeSpan.FromSeconds(10),
            $"Idempotent state for {messageId} was not visible for tenant {tenantId}.");

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string timeoutMessage)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException(timeoutMessage);
    }

    private static async Task PurgeInboxAsync(
        GreenMailFixture greenMail,
        MailboxIdentity mailbox)
    {
        using var imap = await OpenInboxAsync(greenMail, mailbox, FolderAccess.ReadWrite);
        var messages = await imap.Inbox.SearchAsync(SearchQuery.All);
        if (messages.Count > 0)
        {
            await imap.Inbox.AddFlagsAsync(messages, MessageFlags.Deleted, silent: true);
            await imap.Inbox.ExpungeAsync();
        }

        await imap.DisconnectAsync(true);
    }

    private static async Task<ImapClient> OpenInboxAsync(
        GreenMailFixture greenMail,
        MailboxIdentity mailbox,
        FolderAccess access)
    {
        var imap = new ImapClient();
        try
        {
            await imap.ConnectAsync(
                greenMail.Host,
                greenMail.ImapPort,
                SecureSocketOptions.None);
            await imap.AuthenticateAsync(mailbox.InboxUser, InboxPassword);
            await imap.Inbox.OpenAsync(access);
            return imap;
        }
        catch
        {
            imap.Dispose();
            throw;
        }
    }

    private sealed record MailboxIdentity(string Domain, string InboxUser, string InboxAddress)
    {
        public static MailboxIdentity Create()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var domain = $"{suffix}.numera.test";
            var user = $"inbox{suffix}";
            return new MailboxIdentity(domain, user, $"{user}@{domain}");
        }
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.CreateVersion7().ToString();

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
