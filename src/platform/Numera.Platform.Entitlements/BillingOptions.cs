namespace Numera.Platform.Entitlements;

/// <summary>Deployment-wide licensing settings shared by billing and entitlements.</summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Grants permanent full access without subscription or plan checks.</summary>
    public bool SelfHosted { get; set; } = false;
}
