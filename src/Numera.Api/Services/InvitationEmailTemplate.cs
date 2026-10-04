using System.Net;

using Numera.Platform.Db.Entities;

namespace Numera.Api.Services;

/// <summary>German invitation with a plain-text alternative and escaped HTML values.</summary>
public static class InvitationEmailTemplate
{
    public static EmailMessage Build(
        string email, MembershipRole role, string? company, string? temporaryPassword, string? loginUrl)
    {
        var roleName = role switch
        {
            MembershipRole.Owner => "Inhaber",
            MembershipRole.Employee => "Mitarbeiter",
            MembershipRole.TaxAdvisor => "Steuerberater",
            _ => role.ToString(),
        };
        var account = string.IsNullOrWhiteSpace(company) ? "Numera" : $"Numera ({company})";
        var introduction = $"Sie wurden dem Konto {account} mit der Rolle {roleName} hinzugefügt.";
        var login = loginUrl is null
            ? "Öffnen Sie Numera in Ihrem Browser, um sich anzumelden."
            : $"Anmelden: {loginUrl}";
        var credentials = temporaryPassword is null
            ? "Sie können sich mit Ihren bestehenden Zugangsdaten anmelden."
            : $"Temporäres Passwort: {temporaryPassword}\nSie müssen dieses Passwort bei der ersten Anmeldung ändern.";

        return new EmailMessage
        {
            To = email,
            Subject = "Einladung zu Numera",
            TextBody = $"Hallo,\n\n{introduction}\n\n{login}\nBenutzername: {email}\n\n{credentials}\n\nIhr Numera-Team",
            HtmlBody = $"<p>Hallo,</p><p>{WebUtility.HtmlEncode(introduction)}</p>"
                + (loginUrl is null
                    ? $"<p>{WebUtility.HtmlEncode(login)}</p>"
                    : $"<p>Anmelden: <a href=\"{WebUtility.HtmlEncode(loginUrl)}\">{WebUtility.HtmlEncode(loginUrl)}</a></p>")
                + $"<p>Benutzername: {WebUtility.HtmlEncode(email)}</p>"
                + $"<p>{WebUtility.HtmlEncode(credentials).Replace("\n", "<br>", StringComparison.Ordinal)}</p>"
                + "<p>Ihr Numera-Team</p>",
        };
    }

    /// <summary>Prefer the public app URL; use the request origin when it is unconfigured.</summary>
    public static string? LoginUrl(string? publicUrl, HttpRequest? request)
    {
        var baseUrl = publicUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl) && request?.Host.HasValue == true)
        {
            baseUrl = $"{request.Scheme}://{request.Host}";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.AbsoluteUri.TrimEnd('/') + "/login";
    }
}
