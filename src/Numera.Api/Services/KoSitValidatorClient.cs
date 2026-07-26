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
/// The critical safety property (RESEARCH Pitfall 6): a sidecar OUTAGE — connection refused, a
/// timeout, a non-success HTTP status — is mapped to <see cref="EInvoiceValidationStatus.Unavailable"/>
/// with a clear message and logged, and is NEVER mapped to <see cref="EInvoiceValidationStatus.Rejected"/>.
/// An invoice is only "rejected" when the validator actually ran and said so. The client is
/// registered via <c>AddHttpClient&lt;IEInvoiceValidator, KoSitValidatorClient&gt;</c> bound to the
/// <see cref="EInvoiceValidationOptions"/> BaseUrl + Timeout.
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

            if (!response.IsSuccessStatusCode)
            {
                // The daemon answered but not with a report (e.g. 5xx): treat as an outage, not a
                // rejection — we have no authoritative verdict.
                _logger.LogWarning(
                    "KoSIT validator returned non-success status {StatusCode}; treating as Unavailable.",
                    (int)response.StatusCode);
                return Unavailable(
                    $"Der Validierungsdienst antwortete mit Status {(int)response.StatusCode}.",
                    $"The validation service responded with status {(int)response.StatusCode}.");
            }

            var report = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return KoSitReport.Parse(report);
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
