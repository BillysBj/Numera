namespace Numera.Api.Services;

/// <summary>
/// Configuration for the KoSIT e-invoice validation sidecar, bound from the
/// <c>EInvoiceValidation</c> configuration section (mirrors <see cref="EmailOptions"/>).
/// Locally + in tests this points at the <c>kosit-validator</c> docker-compose service
/// (daemon HTTP on host <c>:8081</c>); production overrides <see cref="BaseUrl"/> to the
/// deployed validator (and rebuilds the sidecar from the pinned official JAR + config).
/// </summary>
public sealed class EInvoiceValidationOptions
{
    /// <summary>The configuration section name (<c>EInvoiceValidation</c>).</summary>
    public const string SectionName = "EInvoiceValidation";

    /// <summary>
    /// The base URL of the KoSIT validator daemon (dev/test: <c>http://localhost:8081</c>).
    /// The invoice XML is POSTed to <see cref="ValidationPath"/> under this base.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8081";

    /// <summary>
    /// The path (relative to <see cref="BaseUrl"/>) the daemon serves for POST-a-document
    /// validation. The KoSIT daemon validates the request body posted to the root path.
    /// </summary>
    public string ValidationPath { get; set; } = "/";

    /// <summary>
    /// The HTTP timeout in seconds for a validation call. Kept sane so a hung/overloaded
    /// sidecar surfaces as <c>Unavailable</c> (never a stalled send) rather than blocking.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Informational: the pinned validation profile the sidecar is configured for, surfaced in
    /// diagnostics/health so a config-version drift (RESEARCH Pitfall 4) is visible.
    /// </summary>
    public string ConfiguredProfileVersion { get; set; } =
        "XRechnung 3.0.2 / validator-configuration-xrechnung 2025-07-09 (schematron 2.4.0) / validator JAR 1.5.0";
}
