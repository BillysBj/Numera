using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace Numera.Api.Services;

/// <summary>
/// The v1 <see cref="IEmailSender"/> over MailKit/MimeKit (RESEARCH.md: <c>System.Net.Mail.SmtpClient</c>
/// is obsolete in .NET 9+; MailKit is the recommended replacement). It builds a
/// <c>multipart/alternative</c> body (HTML + plain text) with the rendered PDF attached via
/// <see cref="BodyBuilder"/> and sends it over SMTP — pointed at Mailpit locally + in tests
/// (host/port/from from <see cref="EmailOptions"/>), a real relay in production.
/// </summary>
/// <remarks>
/// Requires TLS when <see cref="EmailOptions.UseSsl"/> is set (implicit on 465, STARTTLS otherwise),
/// else <see cref="SecureSocketOptions.None"/> (Mailpit speaks plain SMTP on :1025).
/// Authentication runs only when a <see cref="EmailOptions.Username"/> is configured (Mailpit
/// accepts anonymously). Registered scoped so the send job resolves it per scope.
/// </remarks>
public sealed class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ITenantEmailConfigResolver? _resolver;

    /// <summary>Production resolves tenant options per send; global-only construction remains supported.</summary>
    public MailKitEmailSender(IOptions<EmailOptions> options, ITenantEmailConfigResolver? resolver = null)
    {
        _options = options.Value;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = _resolver is null ? _options : await _resolver.ResolveAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await SendCoreAsync(message, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SMTP servers may echo submitted credentials in errors. Do not persist/log those
            // via the jobs' LastError fields or expose them through the test endpoint.
            var error = ex.Message;
            if (!string.IsNullOrEmpty(options.Password))
            {
                error = error.Replace(options.Password, "[redacted]", StringComparison.Ordinal);
                error = error.Replace(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(options.Password)),
                    "[redacted]", StringComparison.Ordinal);
            }

            throw new InvalidOperationException(error);
        }
    }

    private static async Task SendCoreAsync(EmailMessage message, EmailOptions options, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        if (message.Attachment is { } attachment)
        {
            body.Attachments.Add(
                attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        using var smtp = new SmtpClient();
        var socketOptions = options.UseSsl
            ? (options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;
        await smtp.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            await smtp.AuthenticateAsync(options.Username, options.Password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await smtp.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
