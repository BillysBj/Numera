using System.Net.Mime;
using System.Text;

using Microsoft.Extensions.Options;

using Numera.Modules.Sales.EInvoice;

namespace Numera.Api.Services;

/// <summary>
/// The v1 <see cref="IEInvoiceValidator"/> over the KoSIT validator sidecar (Phase-5 EINV-03). A
/// typed <see cref="HttpClient"/> POSTs the e-invoice XML as <c>application/xml</c> to the daemon
/// and hands the returned XML report to <see cref="KoSitReport.Parse"/> for a structured verdict.
/// </summary>
/// <remarks>
/// <para>
/// The critical safety property (RESEARCH Pitfall 6): a sidecar OUTAGE — connection refused, a
/// timeout, or an error body that is not a KoSIT report — is mapped to
/// <see cref="EInvoiceValidationStatus.Unavailable"/> with a clear message and logged, and is NEVER
/// mapped to <see cref="EInvoiceValidationStatus.Rejected"/>. An invoice is only "rejected" when the
/// validator actually ran and said so.
/// </para>
/// <para>
/// IMPORTANT — the KoSIT daemon signals the verdict through the HTTP STATUS, not only the body:
/// an ACCEPTED document returns <c>200 OK</c> and a REJECTED document returns <c>406 Not
/// Acceptable</c>, and BOTH carry a full VARL <c>&lt;rep:report&gt;</c> body. So the client must NOT
/// treat a non-2xx status as an outage — it decides on the BODY: any response whose body is a KoSIT
/// report is parsed (accept or reject); only a missing/non-report body (connection failure, timeout,
/// a 5xx error page) is an outage. The client is registered via
/// <c>AddHttpClient&lt;IEInvoiceValidator, KoSitValidatorClient&gt;</c> bound to the
/// <see cref="EInvoiceValidationOptions"/> BaseUrl + Timeout.
/// </para>
/// </remarks>
public sealed class KoSitValidatorClient : IEInvoiceValidator
{
    private readonly HttpClient _http;
    private readonly EInvoiceValidationOptions _options;
    private readonly ILogger<KoSitValidatorClient> _logger;

    /// <summary>Creates the client over the typed <see cref="HttpClient"/> + bound options.</summary>
    public KoSitValidatorClient(
        HttpClient http,
        IOptions<EInvoiceValidationOptions> options,
        ILogger<KoSitValidatorClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var content = new ByteArrayContent(xml);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MediaTypeNames.Application.Xml)
        {
            CharSet = Encoding.UTF8.WebName,
        };

        try
        {
            using var response = await _http.PostAsync(_options.ValidationPath, content, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // The verdict rides on the BODY, not the status: 200 (accept) and 406 (reject) both carry
            // a VARL report. Only a missing/non-report body is an outage — never a false rejection.
            if (KoSitReport.IsReport(body))
            {
                return KoSitReport.Parse(body);
            }

            _logger.LogWarning(
                "KoSIT validator returned status {StatusCode} with a non-report body ({Length} bytes); treating as Unavailable.",
                (int)response.StatusCode, body.Length);
            return Unavailable(
                $"Der Validierungsdienst antwortete unerwartet (Status {(int)response.StatusCode}, kein Prüfbericht).",
                $"The validation service responded unexpectedly (status {(int)response.StatusCode}, no report).");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout (HttpClient.Timeout elapsed) surfaces here as a cancellation NOT requested
            // by the caller — an outage, never a rejection.
            _logger.LogWarning(ex, "KoSIT validator timed out after {Timeout}s; treating as Unavailable.",
                _options.TimeoutSeconds);
            return Unavailable(
                "Der Validierungsdienst hat nicht rechtzeitig geantwortet (Zeitüberschreitung).",
                "The validation service did not respond in time (timeout).");
        }
        catch (HttpRequestException ex)
        {
            // Connection refused / DNS / socket failure — the sidecar is down. Distinct outage.
            _logger.LogWarning(ex, "KoSIT validator was unreachable; treating as Unavailable.");
            return Unavailable(
                "Der Validierungsdienst ist derzeit nicht erreichbar. Bitte später erneut versuchen.",
                "The validation service is currently unreachable. Please try again later.");
        }
    }

    private static EInvoiceValidationResult Unavailable(string messageDe, string messageEn) =>
        new(
            EInvoiceValidationStatus.Unavailable,
            [new EInvoiceFinding("error", null, messageDe, messageDe, messageEn)],
            RawReport: null);
}
