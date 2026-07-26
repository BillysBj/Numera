using System.Xml.Linq;

using Numera.Modules.Sales.EInvoice;

namespace Numera.Api.Services;

/// <summary>
/// Pure parser for the KoSIT validator's XML report (the VARL report the daemon returns for a
/// POSTed e-invoice). Turns the report into the canonical <see cref="EInvoiceValidationStatus"/>
/// (from the report's computed <c>accept</c>/<c>reject</c> assessment) plus a list of
/// <see cref="EInvoiceFinding"/>, each enriched — where the rule id is known — with a plain-language
/// German (authoritative) + English explanation, falling back to the raw KoSIT message otherwise.
/// </summary>
/// <remarks>
/// Parsing is deliberately namespace-tolerant: it matches elements by LOCAL NAME (the KoSIT VARL
/// report uses the <c>http://www.xoev.de/de/validator/varl/1</c> namespace, but the exact prefix
/// and any embedded SVRL wrapper can vary by config version — RESEARCH Open Question 2). The
/// parser is pinned by <c>KoSitReportTests</c> against a captured golden report so the XPaths are
/// regression-guarded against the exact 2026-01-31 / XRechnung 3.0.2 config output.
/// </remarks>
public static class KoSitReport
{
    /// <summary>The XML namespace of the KoSIT VARL report (its root element's namespace).</summary>
    private const string VarlNamespace = "http://www.xoev.de/de/validator/varl/1";

    /// <summary>
    /// Cheap gate the client uses to tell a genuine KoSIT report (which may arrive with a non-2xx
    /// status — a rejection is HTTP 406) from an error/non-report body (an outage). Returns true
    /// only when <paramref name="body"/> parses as XML whose root is the VARL <c>report</c> element.
    /// This is what stops a stray 200 with unrelated XML from being trusted as an "Accepted" verdict.
    /// </summary>
    public static bool IsReport(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var root = XDocument.Parse(body).Root;
            return root is not null
                && string.Equals(root.Name.LocalName, "report", StringComparison.OrdinalIgnoreCase)
                && string.Equals(root.Name.NamespaceName, VarlNamespace, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a KoSIT XML report string into a structured verdict + findings.
    /// </summary>
    /// <param name="reportXml">The raw KoSIT report XML.</param>
    /// <returns>
    /// A result whose <see cref="EInvoiceValidationResult.Status"/> is
    /// <see cref="EInvoiceValidationStatus.Accepted"/> or <see cref="EInvoiceValidationStatus.Rejected"/>
    /// (never <see cref="EInvoiceValidationStatus.Unavailable"/> — that is the client's outage signal,
    /// not a parse outcome), the enriched findings, and the raw report echoed back.
    /// </returns>
    public static EInvoiceValidationResult Parse(string reportXml)
    {
        ArgumentNullException.ThrowIfNull(reportXml);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(reportXml);
        }
        catch (System.Xml.XmlException ex)
        {
            // A non-XML / truncated body from the sidecar is treated as a rejection carrying the
            // parse failure as its single finding — it is emphatically NOT a silent "accept".
            var finding = new EInvoiceFinding(
                "error", null,
                $"Der KoSIT-Bericht konnte nicht gelesen werden: {ex.Message}",
                "Der Validierungsbericht war kein gültiges XML — die Prüfung gilt als nicht bestanden.",
                "The validation report was not valid XML — the check is treated as not passed.");
            return new EInvoiceValidationResult(EInvoiceValidationStatus.Rejected, [finding], reportXml);
        }

        var findings = ExtractFindings(doc);
        var status = DetermineStatus(doc, findings);
        return new EInvoiceValidationResult(status, findings, reportXml);
    }

    /// <summary>
    /// Reads the report's computed acceptance. The KoSIT VARL report carries an
    /// <c>&lt;assessment&gt;</c> with either an <c>&lt;accept/&gt;</c> or a <c>&lt;reject/&gt;</c>
    /// child — that verdict is authoritative. If (defensively) no assessment is present, any
    /// error-severity finding forces a rejection.
    /// </summary>
    private static EInvoiceValidationStatus DetermineStatus(XDocument doc, IReadOnlyList<EInvoiceFinding> findings)
    {
        var assessment = Descendants(doc, "assessment").FirstOrDefault();
        if (assessment is not null)
        {
            if (LocalDescendants(assessment, "reject").Any())
            {
                return EInvoiceValidationStatus.Rejected;
            }

            if (LocalDescendants(assessment, "accept").Any())
            {
                return EInvoiceValidationStatus.Accepted;
            }
        }

        // No explicit assessment: fall back to severity — any hard error blocks acceptance.
        return findings.Any(f => IsError(f.Severity))
            ? EInvoiceValidationStatus.Rejected
            : EInvoiceValidationStatus.Accepted;
    }

    /// <summary>
    /// Extracts each finding from the report. The KoSIT VARL report emits
    /// <c>&lt;message level="error" code="BR-DE-15"&gt;text&lt;/message&gt;</c> elements; as a
    /// fallback the parser also reads embedded SVRL <c>&lt;failed-assert&gt;</c> elements
    /// (<c>@role</c> severity, <c>@id</c> rule id, <c>&lt;text&gt;</c> message).
    /// </summary>
    private static List<EInvoiceFinding> ExtractFindings(XDocument doc)
    {
        var findings = new List<EInvoiceFinding>();

        foreach (var message in Descendants(doc, "message"))
        {
            var severity = NormalizeSeverity(
                Attr(message, "level") ?? Attr(message, "severity") ?? Attr(message, "role"));
            var ruleId = FirstNonEmpty(Attr(message, "code"), Attr(message, "id"), Attr(message, "ruleId"));
            var text = CollapseWhitespace(message.Value);
            findings.Add(BuildFinding(severity, ruleId, text));
        }

        // SVRL fallback (only if the VARL <message> elements were absent for this config version).
        if (findings.Count == 0)
        {
            foreach (var failed in Descendants(doc, "failed-assert").Concat(Descendants(doc, "successful-report")))
            {
                var severity = NormalizeSeverity(Attr(failed, "role") ?? Attr(failed, "flag"));
                var ruleId = FirstNonEmpty(Attr(failed, "id"), Attr(failed, "code"));
                var textNode = LocalDescendants(failed, "text").FirstOrDefault();
                var text = CollapseWhitespace(textNode?.Value ?? failed.Value);
                findings.Add(BuildFinding(severity, ruleId, text));
            }
        }

        return findings;
    }

    private static EInvoiceFinding BuildFinding(string severity, string? ruleId, string message)
    {
        (string? de, string? en) = ruleId is not null && RuleExplanations.TryGetValue(ruleId, out var pair)
            ? pair
            : (null, null);
        return new EInvoiceFinding(severity, ruleId, message, de, en);
    }

    // ---- KoSIT / EN 16931 rule-id explanation map (DE authoritative + EN) -------------------
    // A private, extendable data fill. Covers the common German-CIUS (BR-DE-*) + EN 16931
    // coherence (BR-CO-*) rules the mapper (05-01) most often trips; unmapped ids fall back to
    // the raw KoSIT message. The German text is the legally load-bearing part.
    private static readonly IReadOnlyDictionary<string, (string De, string En)> RuleExplanations =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["BR-DE-1"] = (
                "Die Zahlungsart (BT-81) fehlt — bei einer XRechnung ist mindestens eine Zahlungsanweisung Pflicht.",
                "The payment means (BT-81) is missing — an XRechnung requires at least one payment instruction."),
            ["BR-DE-2"] = (
                "Die Kontaktangaben des Verkäufers (BG-6: Name, Telefon, E-Mail) fehlen — für eine XRechnung sind sie Pflicht.",
                "The seller contact group (BG-6: name, phone, e-mail) is missing — an XRechnung requires it."),
            ["BR-DE-3"] = (
                "Die Straße des Verkäufers (BT-35) fehlt.",
                "The seller address line 1 (BT-35) is missing."),
            ["BR-DE-4"] = (
                "Der Ort des Verkäufers (BT-37) fehlt.",
                "The seller city (BT-37) is missing."),
            ["BR-DE-5"] = (
                "Der Name des Kontakts beim Verkäufer (BT-41) fehlt.",
                "The seller contact name (BT-41) is missing."),
            ["BR-DE-6"] = (
                "Die Telefonnummer des Kontakts beim Verkäufer (BT-42) fehlt.",
                "The seller contact telephone number (BT-42) is missing."),
            ["BR-DE-7"] = (
                "Die E-Mail-Adresse des Kontakts beim Verkäufer (BT-43) fehlt.",
                "The seller contact e-mail address (BT-43) is missing."),
            ["BR-DE-15"] = (
                "Die Käuferreferenz / Leitweg-ID (BT-10) fehlt — für eine XRechnung ist sie immer Pflicht.",
                "The buyer reference / Leitweg-ID (BT-10) is missing — it is always mandatory for an XRechnung."),
            ["BR-DE-16"] = (
                "Es fehlt eine der Steuer-Identifikationen des Verkäufers (USt-IdNr. BT-31 oder Steuernummer BT-32).",
                "One of the seller's tax identifiers (VAT ID BT-31 or tax number BT-32) is missing."),
            ["BR-DE-17"] = (
                "Der Rechnungstyp (BT-3) ist für eine XRechnung nicht zulässig (erlaubt u. a. 380/381/384).",
                "The invoice type code (BT-3) is not allowed for an XRechnung (permitted are e.g. 380/381/384)."),
            ["BR-DE-18"] = (
                "Skonto-/Verzugsangaben müssen dem vorgeschriebenen Textformat entsprechen.",
                "Payment discount/penalty terms must follow the prescribed text format."),
            ["BR-DE-21"] = (
                "Die Kennung der Rechnung (Spezifikation BT-24) muss die XRechnung-Kennung enthalten.",
                "The specification identifier (BT-24) must contain the XRechnung identifier."),
            ["BR-DE-23"] = (
                "Zu einer Überweisung (BG-16) muss die IBAN (BT-84) angegeben sein.",
                "For a credit transfer (BG-16) the receiving account IBAN (BT-84) must be provided."),
            ["BR-DE-26"] = (
                "Bei einer Rechnungskorrektur muss auf die ursprüngliche Rechnung verwiesen werden.",
                "A corrective invoice must reference the preceding invoice."),
            ["BR-CO-10"] = (
                "Die Summe der Positionsnettobeträge (BT-106) stimmt nicht mit der Summe der Zeilen überein.",
                "The sum of line net amounts (BT-106) does not equal the total of the invoice lines."),
            ["BR-CO-13"] = (
                "Der Rechnungsgesamtbetrag ohne USt (BT-109) ist rechnerisch nicht schlüssig (Netto − Nachlass + Zuschlag).",
                "The invoice total without VAT (BT-109) is not coherent (net − allowances + charges)."),
            ["BR-CO-15"] = (
                "Der Bruttobetrag (BT-112) muss dem Nettobetrag (BT-109) plus dem USt-Betrag (BT-110) entsprechen.",
                "The invoice total with VAT (BT-112) must equal the total without VAT (BT-109) plus the VAT amount (BT-110)."),
            ["BR-CO-25"] = (
                "Ist der Zahlbetrag positiv, muss entweder ein Fälligkeitsdatum (BT-9) oder eine Zahlungsbedingung (BT-20) angegeben sein.",
                "When an amount is due for payment, either a due date (BT-9) or payment terms (BT-20) must be given."),
            ["BR-16"] = (
                "Eine Rechnung muss mindestens eine Rechnungsposition (BG-25) enthalten.",
                "An invoice must contain at least one invoice line (BG-25)."),
        };

    // ---- namespace-tolerant XML helpers ---------------------------------------------------

    private static IEnumerable<XElement> Descendants(XDocument doc, string localName) =>
        doc.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> LocalDescendants(XElement element, string localName) =>
        element.DescendantsAndSelf().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string? Attr(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string NormalizeSeverity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "error"; // A finding without a stated level is treated conservatively as an error.
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "fatal" or "error" or "err" => "error",
            "warning" or "warn" => "warning",
            "information" or "info" or "notice" => "information",
            var other => other,
        };
    }

    private static bool IsError(string severity) =>
        string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);

    private static string CollapseWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
