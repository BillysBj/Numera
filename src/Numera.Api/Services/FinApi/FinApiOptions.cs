namespace Numera.Api.Services.FinApi;

/// <summary>Configuration for the opt-in finAPI Access sandbox adapter.</summary>
public sealed class FinApiOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "FinApi";

    /// <summary>OAuth application client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth application client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>finAPI Access API base URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>finAPI Web Form 2.0 browser base URL.</summary>
    public string WebFormBaseUrl { get; set; } = string.Empty;

    /// <summary>Number of days before consent expiry at which re-consent is requested.</summary>
    public int ConsentWarnDays { get; set; } = 14;
}
