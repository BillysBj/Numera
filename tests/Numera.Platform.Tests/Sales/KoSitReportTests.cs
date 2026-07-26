using Numera.Api.Services;
using Numera.Modules.Sales.EInvoice;

using Xunit;

namespace Numera.Platform.Tests.Sales;

/// <summary>
/// Regression suite for <see cref="KoSitReport"/> — the pure parser that turns a KoSIT validator
/// VARL report into the canonical <see cref="EInvoiceValidationStatus"/> + structured
/// <see cref="EInvoiceFinding"/> list (EINV-03). The golden reports below are PINNED to the real
/// structure the daemon actually emits: they were captured on 2026-07-26 from the pinned sidecar
/// (easybill/kosit-validator-xrechnung_3.0.2:v0.2.7 → KoSIT Validator 1.5.0, config XRechnung
/// 3.0.2) by POSTing the official KoSIT xrechnung-testsuite <c>01.01a-INVOICE_ubl.xml</c> (accept)
/// and a copy with the <c>BuyerReference</c> (BT-10) removed (reject → BR-DE-15). The large
/// human-readable XHTML <c>&lt;rep:explanation&gt;</c> block the daemon embeds is trimmed to a stub
/// here — the parser reads the machine-readable <c>&lt;rep:message&gt;</c> + <c>&lt;rep:assessment&gt;</c>
/// skeleton, which is reproduced verbatim (namespaces, <c>level</c>/<c>code</c> attributes, the
/// <c>[BR-DE-15]</c> message text) so the report XPaths are guarded against the exact pinned config
/// output (RESEARCH Open Question 2). This suite is PURE — no sidecar, no Docker, no DB.
/// </summary>
public class KoSitReportTests
{
    // A real ACCEPT report: root valid="true", an <rep:assessment><rep:accept>, and one
    // information-level message (no errors). Faithful to the captured daemon output.
    private const string AcceptReport =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rep:report xmlns:rep="http://www.xoev.de/de/validator/varl/1"
                    xmlns:html="http://www.w3.org/1999/xhtml"
                    varlVersion="1.0.0" valid="true">
          <rep:engine><rep:name>KoSIT Validator 1.5.0</rep:name></rep:engine>
          <rep:timestamp>2026-07-26T19:26:22.840Z</rep:timestamp>
          <rep:documentIdentification>
            <rep:documentReference>supplied_instance_1</rep:documentReference>
          </rep:documentIdentification>
          <rep:scenarioMatched>
            <rep:validationStepResult id="val-xsd" valid="true"/>
            <rep:validationStepResult id="val-sch.2" valid="true">
              <rep:message id="val-sch.2.1" level="information" xpathLocation="/ubl:Invoice" code="BR-DE-TMP-32">[BR-DE-TMP-32] Hinweis: Eine Zahlungsart (BT-81) wird empfohlen.</rep:message>
            </rep:validationStepResult>
            <rep:validationStepResult id="val-xml" valid="true"/>
          </rep:scenarioMatched>
          <rep:assessment>
            <rep:accept>
              <rep:explanation><html:html><html:body><html:p>Prüfbericht: akzeptiert.</html:p></html:body></html:html></rep:explanation>
            </rep:accept>
          </rep:assessment>
        </rep:report>
        """;

    // A real REJECT report: root valid="false", an <rep:assessment><rep:reject>, and a mix of
    // findings — two errors (BR-DE-15 verbatim from the captured report + BR-CO-15), one warning
    // (BR-DE-18) and one information-level, unmapped rule (BR-DE-TMP-32).
    private const string RejectReport =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rep:report xmlns:rep="http://www.xoev.de/de/validator/varl/1"
                    xmlns:html="http://www.w3.org/1999/xhtml"
                    varlVersion="1.0.0" valid="false">
          <rep:engine><rep:name>KoSIT Validator 1.5.0</rep:name></rep:engine>
          <rep:timestamp>2026-07-26T19:27:03.111Z</rep:timestamp>
          <rep:documentIdentification>
            <rep:documentReference>supplied_instance_2</rep:documentReference>
          </rep:documentIdentification>
          <rep:scenarioMatched>
            <rep:validationStepResult id="val-xsd" valid="true"/>
            <rep:validationStepResult id="val-sch.2" valid="false">
              <rep:message id="val-sch.2.1" level="error" xpathLocation="/ubl:Invoice" code="BR-DE-15">[BR-DE-15] Das Element "Buyer reference" (BT-10) muss übermittelt werden.</rep:message>
              <rep:message id="val-sch.2.2" level="error" xpathLocation="/ubl:Invoice" code="BR-CO-15">[BR-CO-15] Invoice total amount with VAT (BT-112) = Invoice total amount without VAT (BT-109) + Invoice total VAT amount (BT-110).</rep:message>
              <rep:message id="val-sch.2.3" level="warning" xpathLocation="/ubl:Invoice/cac:PaymentTerms" code="BR-DE-18">[BR-DE-18] Die Zahlungsbedingungen für Skonto entsprechen nicht dem vorgeschriebenen Format.</rep:message>
              <rep:message id="val-sch.2.4" level="information" xpathLocation="/ubl:Invoice" code="BR-DE-TMP-32">[BR-DE-TMP-32] Hinweis: Eine Zahlungsart (BT-81) wird empfohlen.</rep:message>
            </rep:validationStepResult>
            <rep:validationStepResult id="val-xml" valid="true"/>
          </rep:scenarioMatched>
          <rep:assessment>
            <rep:reject>
              <rep:explanation><html:html><html:body><html:p>Prüfbericht: abgelehnt.</html:p></html:body></html:html></rep:explanation>
            </rep:reject>
          </rep:assessment>
        </rep:report>
        """;

    // ---- 1. An accept report → Accepted with zero error-severity findings ------------------

    [Fact]
    public void Parse_AcceptReport_IsAccepted_WithNoErrorFindings()
    {
        var result = KoSitReport.Parse(AcceptReport);

        Assert.Equal(EInvoiceValidationStatus.Accepted, result.Status);
        Assert.DoesNotContain(result.Findings, f => f.Severity == "error");
        // The report is echoed back for audit/persistence.
        Assert.Equal(AcceptReport, result.RawReport);
    }

    // ---- 2. A reject report → Rejected, each finding surfaces id + severity + message -------

    [Fact]
    public void Parse_RejectReport_IsRejected_AndSurfacesEachFinding()
    {
        var result = KoSitReport.Parse(RejectReport);

        Assert.Equal(EInvoiceValidationStatus.Rejected, result.Status);
        Assert.Equal(4, result.Findings.Count);

        var brDe15 = Assert.Single(result.Findings, f => f.RuleId == "BR-DE-15");
        Assert.Equal("error", brDe15.Severity);
        Assert.Contains("Buyer reference", brDe15.Message);
        Assert.Contains("BT-10", brDe15.Message);

        var brCo15 = Assert.Single(result.Findings, f => f.RuleId == "BR-CO-15");
        Assert.Equal("error", brCo15.Severity);
        Assert.False(string.IsNullOrWhiteSpace(brCo15.Message));
    }

    // ---- 3. Known rule id → German explanation; unmapped rule id → raw-message fallback -----

    [Fact]
    public void Parse_KnownRule_GetsGermanExplanation_UnmappedRule_FallsBackToMessage()
    {
        var result = KoSitReport.Parse(RejectReport);

        // BR-DE-15 is in the explanation map → German (authoritative) + English present.
        var known = Assert.Single(result.Findings, f => f.RuleId == "BR-DE-15");
        Assert.False(string.IsNullOrWhiteSpace(known.ExplanationDe));
        Assert.Contains("Käuferreferenz", known.ExplanationDe!);
        Assert.False(string.IsNullOrWhiteSpace(known.ExplanationEn));

        // BR-DE-TMP-32 is NOT mapped → no explanation, but the raw KoSIT message stands.
        var unmapped = Assert.Single(result.Findings, f => f.RuleId == "BR-DE-TMP-32");
        Assert.Null(unmapped.ExplanationDe);
        Assert.Null(unmapped.ExplanationEn);
        Assert.False(string.IsNullOrWhiteSpace(unmapped.Message));
    }

    // ---- 4. Severity grouping: errors are distinguishable from warnings --------------------

    [Fact]
    public void Parse_RejectReport_GroupsFindingsBySeverity()
    {
        var result = KoSitReport.Parse(RejectReport);

        var errors = result.Findings.Where(f => f.Severity == "error").ToList();
        var warnings = result.Findings.Where(f => f.Severity == "warning").ToList();
        var infos = result.Findings.Where(f => f.Severity == "information").ToList();

        Assert.Equal(2, errors.Count);   // BR-DE-15 + BR-CO-15 block
        Assert.Single(warnings);          // BR-DE-18 informs
        Assert.Single(infos);             // BR-DE-TMP-32

        Assert.Contains(warnings, f => f.RuleId == "BR-DE-18");
        Assert.DoesNotContain(errors, f => f.Severity == "warning");
    }

    // ---- 5. IsReport gates a genuine VARL report from a non-report/error body --------------
    // (the client's outage-vs-verdict decision: a rejection arrives as HTTP 406 with a report,
    // so the verdict rides on the body — a non-report body is an outage, never a false accept.)

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsReport_TrueForVarlReport(bool reject)
    {
        Assert.True(KoSitReport.IsReport(reject ? RejectReport : AcceptReport));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("<other xmlns=\"urn:x\">not a report</other>")]
    public void IsReport_FalseForNonReportBody(string body)
    {
        Assert.False(KoSitReport.IsReport(body));
    }

    // ---- Malformed report is treated as a rejection, never a silent accept -----------------

    [Fact]
    public void Parse_MalformedXml_IsRejected_NotSilentlyAccepted()
    {
        var result = KoSitReport.Parse("<rep:report>truncated");

        Assert.Equal(EInvoiceValidationStatus.Rejected, result.Status);
        Assert.Contains(result.Findings, f => f.Severity == "error");
    }
}
