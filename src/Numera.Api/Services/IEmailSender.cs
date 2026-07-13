namespace Numera.Api.Services;

/// <summary>
/// A single file attachment on an outbound <see cref="EmailMessage"/> — the raw bytes plus the
/// filename the recipient sees and its MIME content type (e.g. <c>application/pdf</c>).
/// </summary>
/// <param name="FileName">The attachment filename shown to the recipient (e.g. <c>RE-2026-00001.pdf</c>).</param>
/// <param name="Content">The attachment bytes (the rendered PDF).</param>
/// <param name="ContentType">The MIME type (e.g. <c>application/pdf</c>).</param>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// A provider-agnostic outbound e-mail: recipient, subject, an HTML + plain-text body and an
/// optional single attachment. Deliberately minimal — the seam a MailKit/SMTP, SendGrid or
/// Postmark implementation fills in (Phase-4 DOCS-03).
/// </summary>
public sealed class EmailMessage
{
    /// <summary>The recipient address the message is delivered to. Required.</summary>
    public required string To { get; init; }

    /// <summary>The subject line (bilingual, resolved by document language).</summary>
    public required string Subject { get; init; }

    /// <summary>The HTML body part (multipart/alternative with <see cref="TextBody"/>).</summary>
    public string? HtmlBody { get; init; }

    /// <summary>The plain-text body part (multipart/alternative with <see cref="HtmlBody"/>).</summary>
    public string? TextBody { get; init; }

    /// <summary>The single attachment (the rendered invoice PDF), or null for a body-only message.</summary>
    public EmailAttachment? Attachment { get; init; }
}

/// <summary>
/// The e-mail dispatch seam (Phase-4 DOCS-03). A finalized document's rendered PDF is sent to the
/// customer through this interface, so the transport (MailKit → SMTP for v1, a hosted API later)
/// is swappable without touching the send job. The v1 implementation is
/// <see cref="MailKitEmailSender"/>, pointed at Mailpit locally + in tests.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="message"/> (with its optional attachment) over the transport.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
