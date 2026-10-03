using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Pdf;

namespace Numera.Api.Services;

/// <summary>Plain-text templates. Unknown placeholders stay literal; substitution is one pass.</summary>
public static partial class TenantEmailTemplates
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static DocumentEmailTemplates.Content Invoice(TenantEmailSettings? settings,
        SalesDocument document, string language, string documentNumber)
    {
        var defaults = DocumentEmailTemplates.Build(language, documentNumber);
        return Render(settings?.InvoiceSubject, settings?.InvoiceBody, defaults,
            Values(document, language, documentNumber));
    }

    public static DocumentEmailTemplates.Content Dunning(TenantEmailSettings? settings,
        SalesDocument document, string language, string levelName, decimal fee)
    {
        var text = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? $"Please find the dunning notice for invoice {document.DocumentNumber} attached."
            : $"Anbei erhalten Sie die Mahnung zur Rechnung {document.DocumentNumber}.";
        var defaults = new DocumentEmailTemplates.Content($"{levelName} – {document.DocumentNumber}",
            $"<p>{WebUtility.HtmlEncode(text)}</p>", text);
        var values = Values(document, language, document.DocumentNumber ?? string.Empty);
        values["Mahnstufe"] = levelName;
        values["Mahngebühr"] = fee.ToString("C", German);
        return Render(settings?.DunningSubject, settings?.DunningBody, defaults, values);
    }

    public static DocumentEmailTemplates.Content Render(string? subject, string? body,
        DocumentEmailTemplates.Content defaults, IReadOnlyDictionary<string, string> values)
    {
        var text = string.IsNullOrEmpty(body) ? defaults.TextBody : Replace(body, values);
        // Escape the complete rendered body, including literal template markup and every value.
        var html = string.IsNullOrEmpty(body) ? defaults.HtmlBody
            : "<p>" + WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace('\r', '\n')
                .Replace("\n", "<br/>") + "</p>";
        var renderedSubject = string.IsNullOrEmpty(subject) ? defaults.Subject : Replace(subject, values);
        // Names/numbers come from customer data; never let them create additional mail headers.
        return new(renderedSubject.Replace('\r', ' ').Replace('\n', ' '), html, text);
    }

    private static string Replace(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value : match.Value);

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> Values(SalesDocument document, string language, string number)
    {
        var frozen = SnapshotReader.FromDocument(document, language: language);
        return new(StringComparer.Ordinal)
        {
            ["Rechnungsnummer"] = number,
            ["Belegart"] = DocumentLabel(document.DocumentType, language),
            ["Betrag"] = document.TotalGross.ToString("C", German),
            ["Fälligkeitsdatum"] = document.DueDate?.ToString("dd.MM.yyyy", German) ?? string.Empty,
            ["Kundenname"] = frozen.Recipient.Name ?? string.Empty,
            ["Firmenname"] = frozen.Issuer.LegalName ?? string.Empty,
        };
    }

    private static string DocumentLabel(DocumentType type, string language)
    {
        var english = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        return type switch
        {
            DocumentType.Angebot => english ? "Quote" : "Angebot",
            DocumentType.Auftragsbestaetigung => english ? "Order Confirmation" : "Auftragsbestätigung",
            DocumentType.Lieferschein => english ? "Delivery Note" : "Lieferschein",
            DocumentType.Rechnung => english ? "Invoice" : "Rechnung",
            DocumentType.Storno => english ? "Cancellation" : "Stornorechnung",
            DocumentType.Gutschrift => english ? "Credit Note" : "Gutschrift",
            DocumentType.Abschlagsrechnung => english ? "Down Payment Invoice" : "Abschlagsrechnung",
            DocumentType.Schlussrechnung => english ? "Final Invoice" : "Schlussrechnung",
            _ => english ? "Document" : "Beleg",
        };
    }
}
