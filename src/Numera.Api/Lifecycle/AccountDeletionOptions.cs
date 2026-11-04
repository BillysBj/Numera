namespace Numera.Api.Lifecycle;

/// <summary>
/// Deployment-wide settings for the GoBD-safe account-deletion flow (Phase B1).
/// Bound from the <c>AccountDeletion</c> configuration section.
/// </summary>
public sealed class AccountDeletionOptions
{
    public const string SectionName = "AccountDeletion";

    /// <summary>
    /// Days between a deletion request and the earliest hard purge (the read-only
    /// grace/undo window). Default 30.
    /// </summary>
    public int GraceDays { get; set; } = 30;

    /// <summary>
    /// How recent the tenant's last data export must be (in hours) for a deletion
    /// request to be accepted — "export before deletion". Default 24.
    /// </summary>
    public int ExportFreshnessHours { get; set; } = 24;
}
