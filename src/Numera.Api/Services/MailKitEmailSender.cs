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
/// Connects with <see cref="SecureSocketOptions.StartTls"/> when <see cref="EmailOptions.UseSsl"/>
/// is set, else <see cref="SecureSocketOptions.None"/> (Mailpit speaks plain SMTP on :1025).
/// Authentication runs only when a <see cref="EmailOptions.Username"/> is configured (Mailpit
/// accepts anonymously). Registered scoped so the send job resolves it per scope.
/// </remarks>
public sealed class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    /// <summary>Creates the sender over the bound <see cref="EmailOptions"/>.</summary>
    public MailKitEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
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
        var socketOptions = _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await smtp.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await smtp.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await smtp.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
