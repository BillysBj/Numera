using System.Buffers.Binary;
using System.Security.Cryptography;

using Hangfire;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MimeKit;

using Npgsql;

using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales.Belege;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Polls the shared inbox and routes receipt attachments to exactly one tenant.</summary>
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class PollBelegMailboxJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImapClient _imap;
    private readonly BelegeMailboxOptions _options;
    private readonly ILogger<PollBelegMailboxJob> _logger;

    /// <summary>Creates the worker-queue IMAP poll.</summary>
    public PollBelegMailboxJob(
        IServiceScopeFactory scopeFactory,
        ImapClient imap,
        IOptions<BelegeMailboxOptions> options,
        ILogger<PollBelegMailboxJob> logger)
    {
        _scopeFactory = scopeFactory;
        _imap = imap;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Fetches unseen mail, resolves its routing secret, and invokes shared ingest.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var domain = _options.Domain.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(domain))
        {
            _logger.LogWarning("Receipt mailbox poll skipped because Host or Domain is not configured.");
            return;
        }

        try
        {
            var socketOptions = _options.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;
            await _imap.ConnectAsync(_options.Host, _options.Port, socketOptions, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await _imap.AuthenticateAsync(_options.Username, _options.Password, ct)
                    .ConfigureAwait(false);
            }

            var inbox = _imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
            var unseen = await inbox.SearchAsync(SearchQuery.NotSeen, ct).ConfigureAwait(false);
            foreach (var uid in unseen)
            {
                ct.ThrowIfCancellationRequested();
                var message = await inbox.GetMessageAsync(uid, ct).ConfigureAwait(false);
                var tenantId = await ResolveTenantAsync(message, domain, ct).ConfigureAwait(false);
                if (tenantId is null)
                {
                    _logger.LogWarning(
                        "Incoming receipt mail {MessageId} has no unambiguous active tenant recipient; marking seen without ingest.",
                        message.MessageId);
                    await MarkSeenAsync(inbox, uid, ct).ConfigureAwait(false);
                    continue;
                }

                var attachments = await ReadSupportedAttachmentsAsync(message, ct).ConfigureAwait(false);
                var contentHash = ComputeContentHash(message, attachments);
                var messageId = string.IsNullOrWhiteSpace(message.MessageId)
                    ? $"<sha256-{contentHash}@numera.local>"
                    : message.MessageId.Trim();

                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId.Value);
                var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
                var alreadyProcessed = await db.Set<ProcessedBelegeMail>()
                    .AsNoTracking()
                    .AnyAsync(
                        processed => processed.MessageId == messageId
                            || processed.ContentHash == contentHash,
                        ct)
                    .ConfigureAwait(false);
                if (alreadyProcessed)
                {
                    _logger.LogInformation(
                        "Incoming receipt mail {MessageId} for tenant {TenantId} was already processed.",
                        messageId, tenantId);
                    await MarkSeenAsync(inbox, uid, ct).ConfigureAwait(false);
                    continue;
                }

                var ingest = scope.ServiceProvider.GetRequiredService<ReceiptIngestService>();
                foreach (var attachment in attachments)
                {
                    await ingest.IngestAsync(
                        attachment.Bytes,
                        attachment.FileName,
                        attachment.ContentType,
                        ReceiptSource.Email,
                        uploadedByUserId: null,
                        ct).ConfigureAwait(false);
                }

                db.Add(new ProcessedBelegeMail
                {
                    TenantId = tenantId.Value,
                    MessageId = messageId,
                    ContentHash = contentHash,
                    ProcessedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await MarkSeenAsync(inbox, uid, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Processed receipt mail {MessageId} for tenant {TenantId} with {AttachmentCount} supported attachments.",
                    messageId, tenantId, attachments.Count);
            }
        }
        finally
        {
            if (_imap.IsConnected)
            {
                await _imap.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task<Guid?> ResolveTenantAsync(
        MimeMessage message,
        string domain,
        CancellationToken ct)
    {
        var tokens = RecipientMailboxes(message)
            .Where(address => string.Equals(address.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .Select(address => address.LocalPart)
            .Where(localPart => localPart.StartsWith("belege-", StringComparison.OrdinalIgnoreCase))
            .Select(localPart => localPart["belege-".Length..])
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        Guid? resolvedTenant = null;
        foreach (var token in tokens)
        {
            var matches = await ResolveTokenAsync(token, ct).ConfigureAwait(false);
            if (matches.Count != 1
                || resolvedTenant is { } existing && existing != matches[0])
            {
                return null;
            }

            resolvedTenant = matches[0];
        }

        return resolvedTenant;
    }

    private async Task<IReadOnlyList<Guid>> ResolveTokenAsync(string token, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT tenant_id FROM public.resolve_belege_mailbox(@token)";
            command.Parameters.Add(new NpgsqlParameter<string>("token", token));
            var matches = new List<Guid>(capacity: 2);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                matches.Add(reader.GetGuid(0));
            }

            return matches;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static IEnumerable<MailboxAddress> RecipientMailboxes(MimeMessage message)
    {
        foreach (var deliveredTo in message.Headers
                     .Where(header => string.Equals(
                         header.Field,
                         "Delivered-To",
                         StringComparison.OrdinalIgnoreCase)))
        {
            if (InternetAddressList.TryParse(deliveredTo.Value, out var parsed))
            {
                foreach (var mailbox in parsed.Mailboxes)
                {
                    yield return mailbox;
                }
            }
        }

        foreach (var mailbox in message.To.Mailboxes)
        {
            yield return mailbox;
        }
    }

    private static async Task<List<ReceiptAttachment>> ReadSupportedAttachmentsAsync(
        MimeMessage message,
        CancellationToken ct)
    {
        var attachments = new List<ReceiptAttachment>();
        var index = 0;
        foreach (var entity in message.Attachments)
        {
            if (entity is not MimePart part)
            {
                continue;
            }

            index++;
            var fileName = string.IsNullOrWhiteSpace(part.FileName)
                ? $"attachment-{index}"
                : Path.GetFileName(part.FileName);
            var contentType = part.ContentType.MimeType;
            if (part.Content is null
                || ReceiptIngestService.ResolveContentType(contentType, fileName) is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await part.Content.DecodeToAsync(buffer, ct).ConfigureAwait(false);
            attachments.Add(new ReceiptAttachment(fileName, contentType, buffer.ToArray()));
        }

        return attachments;
    }

    private static string ComputeContentHash(
        MimeMessage message,
        IReadOnlyCollection<ReceiptAttachment> attachments)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (attachments.Count == 0)
        {
            using var raw = new MemoryStream();
            message.WriteTo(raw);
            hash.AppendData(raw.GetBuffer().AsSpan(0, checked((int)raw.Length)));
        }
        else
        {
            Span<byte> length = stackalloc byte[sizeof(int)];
            foreach (var attachment in attachments)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, attachment.Bytes.Length);
                hash.AppendData(length);
                hash.AppendData(attachment.Bytes);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static Task MarkSeenAsync(IMailFolder inbox, UniqueId uid, CancellationToken ct)
        => inbox.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, ct);

    private sealed record ReceiptAttachment(string FileName, string ContentType, byte[] Bytes);
}
