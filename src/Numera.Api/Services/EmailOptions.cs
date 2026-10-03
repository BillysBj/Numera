namespace Numera.Api.Services;

/// <summary>
/// SMTP configuration for <see cref="MailKitEmailSender"/>, bound from the <c>Email</c>
/// configuration section. Locally + in tests this points at Mailpit (host <c>localhost</c>,
/// port <c>1025</c>, no TLS, no auth); production overrides Host/Port/credentials.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The configuration section name (<c>Email</c>).</summary>
    public const string SectionName = "Email";

    /// <summary>The SMTP host (Mailpit: <c>localhost</c>).</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The SMTP port (Mailpit: <c>1025</c>).</summary>
    public int Port { get; set; } = 1025;

    /// <summary>Require TLS: implicit TLS on port 465, STARTTLS otherwise. False = plain (Mailpit).</summary>
    public bool UseSsl { get; set; }

    /// <summary>The envelope + header From address.</summary>
    public string FromAddress { get; set; } = "noreply@numera.local";

    /// <summary>The display name on the From address.</summary>
    public string FromName { get; set; } = "Numera";

    /// <summary>Optional SMTP username; authentication is skipped when blank (Mailpit).</summary>
    public string? Username { get; set; }

    /// <summary>Optional SMTP password (used only when <see cref="Username"/> is set).</summary>
    public string? Password { get; set; }
}
