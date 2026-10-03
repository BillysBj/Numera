namespace Numera.Api.Services;

/// <summary>
/// The bilingual (de/en) subject + body copy for a document-delivery e-mail (Phase-4 DOCS-03).
/// The legal content lives in the attached §14 PDF; this is only the covering-message text, keyed
/// by the send language so the endpoint (which stores the subject) and the send job (which builds
/// the full message) render identical wording.
/// </summary>
public static class DocumentEmailTemplates
{
    /// <summary>The rendered subject + HTML/text body for one send.</summary>
    /// <param name="Subject">The subject line.</param>
    /// <param name="HtmlBody">The HTML body part.</param>
    /// <param name="TextBody">The plain-text body part.</param>
    public readonly record struct Content(string Subject, string HtmlBody, string TextBody);

    /// <summary>
    /// Builds the covering-message copy for <paramref name="documentNumber"/> in
    /// <paramref name="language"/> (<c>en</c> → English, anything else → German default).
    /// </summary>
    public static Content Build(string language, string documentNumber)
    {
        var isEnglish = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var htmlNumber = System.Net.WebUtility.HtmlEncode(documentNumber);
        if (isEnglish)
        {
            return new Content(
                Subject: $"Invoice {documentNumber}",
                HtmlBody:
                    $"<p>Dear Sir or Madam,</p>"
                    + $"<p>please find attached your invoice <strong>{htmlNumber}</strong> as a PDF.</p>"
                    + "<p>Kind regards,<br/>Numera</p>",
                TextBody:
                    "Dear Sir or Madam,\r\n\r\n"
                    + $"please find attached your invoice {documentNumber} as a PDF.\r\n\r\n"
                    + "Kind regards,\r\nNumera");
        }

        return new Content(
            Subject: $"Rechnung {documentNumber}",
            HtmlBody:
                "<p>Sehr geehrte Damen und Herren,</p>"
                + $"<p>anbei erhalten Sie Ihre Rechnung <strong>{htmlNumber}</strong> als PDF.</p>"
                + "<p>Mit freundlichen Grüßen<br/>Numera</p>",
            TextBody:
                "Sehr geehrte Damen und Herren,\r\n\r\n"
                + $"anbei erhalten Sie Ihre Rechnung {documentNumber} als PDF.\r\n\r\n"
                + "Mit freundlichen Grüßen\r\nNumera");
    }
}
