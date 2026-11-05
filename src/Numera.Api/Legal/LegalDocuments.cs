namespace Numera.Api.Legal;

/// <summary>
/// The platform's legal document set (Impressum, Datenschutzerklärung, AGB). The current
/// version string is persisted with every registration's consent (A2) so that a later change
/// to the binding texts can require renewed acceptance and the acceptance stays provable.
/// </summary>
/// <remarks>
/// Bump <see cref="CurrentVersion"/> (an ISO date) whenever the legal texts change materially.
/// It MUST stay in sync with the frontend constant <c>LEGAL_VERSION</c> (web/src/lib/legal.ts).
/// </remarks>
public static class LegalDocuments
{
    /// <summary>Current legal-document version (ISO date). Mirrors the frontend LEGAL_VERSION.</summary>
    public const string CurrentVersion = "2026-11-05";
}
