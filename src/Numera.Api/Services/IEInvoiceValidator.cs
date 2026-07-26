using Numera.Modules.Sales.EInvoice;

namespace Numera.Api.Services;

/// <summary>
/// A single validation finding from the KoSIT report — one rule outcome for the e-invoice.
/// Errors block acceptance; warnings/info inform. Where the rule id is known it is enriched with a
/// human-readable German (authoritative) + English explanation; otherwise the raw KoSIT
/// <paramref name="Message"/> stands on its own ("verständlich erklärt", EINV-03).
/// </summary>
/// <param name="Severity">The finding severity as reported by KoSIT (e.g. <c>error</c>, <c>warning</c>, <c>information</c>).</param>
/// <param name="RuleId">The schematron/business rule id (e.g. <c>BR-DE-15</c>, <c>BR-CO-10</c>), or null if the report carried none.</param>
/// <param name="Message">The raw KoSIT message text — always present, the fallback when no explanation is mapped.</param>
/// <param name="ExplanationDe">A plain-language German explanation of cause + fix for a known rule id, else null (authoritative).</param>
/// <param name="ExplanationEn">A plain-language English explanation for a known rule id, else null.</param>
public sealed record EInvoiceFinding(
    string Severity,
    string? RuleId,
    string Message,
    string? ExplanationDe,
    string? ExplanationEn);

/// <summary>
/// The structured result of validating an e-invoice: the overall
/// <see cref="EInvoiceValidationStatus"/> plus the individual findings and the raw KoSIT report
/// (kept for audit/persistence). This is the SEAM 05-03 (the two-stage send gate) and 05-05
/// (inbound validation) both consume.
/// </summary>
/// <param name="Status">Accepted / Rejected / Unavailable — the canonical verdict.</param>
/// <param name="Findings">The per-rule findings (empty on an unavailable outage).</param>
/// <param name="RawReport">The raw KoSIT XML report, or null when the service was unavailable.</param>
public sealed record EInvoiceValidationResult(
    EInvoiceValidationStatus Status,
    IReadOnlyList<EInvoiceFinding> Findings,
    string? RawReport);

/// <summary>
/// The e-invoice validation seam (Phase-5 EINV-03). Posts an e-invoice XML to the
/// government-authoritative KoSIT validator and returns a structured accept/reject result with
/// human-readable findings — distinguishing a genuine rejection from a service outage. The v1
/// implementation is <see cref="KoSitValidatorClient"/>, pointed at the <c>kosit-validator</c>
/// sidecar. 05-03 wires this into the two-stage gate; this plan builds the seam only.
/// </summary>
public interface IEInvoiceValidator
{
    /// <summary>
    /// Validates <paramref name="xml"/> (a serialized XRechnung UBL/CII document) against the
    /// KoSIT validator and returns the structured verdict. A validator outage yields
    /// <see cref="EInvoiceValidationStatus.Unavailable"/> — never a false
    /// <see cref="EInvoiceValidationStatus.Rejected"/>.
    /// </summary>
    Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default);
}
