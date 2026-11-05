using Numera.Api.Services;

namespace Numera.Api.Endpoints;

/// <summary>
/// Owner-only configuration of the shared receipt mailbox (one IMAP inbox + catch-all domain the
/// platform operates for all tenants). Stored globally in <c>platform_belege_mailbox</c>; the
/// password is encrypted at rest and never returned to the client.
/// </summary>
public static class BelegeMailboxSettingsEndpoints
{
    /// <summary>Maps <c>GET/PUT /api/settings/belege-mailbox</c> (Owner-only).</summary>
    public static IEndpointRouteBuilder MapBelegeMailboxSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/belege-mailbox").RequireAuthorization("RequireOwner");

        group.MapGet("/", async (BelegeMailboxConfigStore store, CancellationToken ct) =>
        {
            var config = await store.GetAsync(ct).ConfigureAwait(false);
            return Results.Ok(new BelegeMailboxSettingsResponse(
                Host: config?.Host ?? string.Empty,
                Port: config?.Port ?? 993,
                UseSsl: config?.UseSsl ?? true,
                Username: config?.Username ?? string.Empty,
                Domain: config?.Domain ?? string.Empty,
                IsActive: config?.IsActive ?? false,
                HasPassword: !string.IsNullOrWhiteSpace(config?.PasswordCipher),
                UpdatedAt: config?.UpdatedAt));
        });

        group.MapPut("/", async (
            BelegeMailboxSettingsRequest body,
            BelegeMailboxConfigStore store,
            BelegeMailboxPasswordProtector protector,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Host)
                || string.IsNullOrWhiteSpace(body.Domain)
                || string.IsNullOrWhiteSpace(body.Username)
                || body.Port is < 1 or > 65535)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["belegeMailbox"] = ["Host, Domain, Benutzername und ein gültiger Port sind erforderlich."],
                });
            }

            // A supplied, non-empty password is encrypted and replaces the stored one; an empty
            // password keeps the existing ciphertext (so the Owner can edit other fields without
            // re-typing the password, which is never sent back to the client).
            var passwordCipher = string.IsNullOrEmpty(body.Password)
                ? null
                : protector.Protect(body.Password);

            await store.UpsertAsync(
                body.Host.Trim(),
                body.Port,
                body.UseSsl,
                body.Username.Trim(),
                passwordCipher,
                body.Domain.Trim().TrimStart('@'),
                body.IsActive,
                ct).ConfigureAwait(false);

            var saved = await store.GetAsync(ct).ConfigureAwait(false);
            return Results.Ok(new BelegeMailboxSettingsResponse(
                Host: saved?.Host ?? string.Empty,
                Port: saved?.Port ?? 993,
                UseSsl: saved?.UseSsl ?? true,
                Username: saved?.Username ?? string.Empty,
                Domain: saved?.Domain ?? string.Empty,
                IsActive: saved?.IsActive ?? false,
                HasPassword: !string.IsNullOrWhiteSpace(saved?.PasswordCipher),
                UpdatedAt: saved?.UpdatedAt));
        });

        return app;
    }
}

/// <summary>Response for the shared receipt-mailbox settings (never includes the password).</summary>
public sealed record BelegeMailboxSettingsResponse(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Domain,
    bool IsActive,
    bool HasPassword,
    DateTimeOffset? UpdatedAt);

/// <summary>Request body to save the shared receipt-mailbox settings.</summary>
public sealed record BelegeMailboxSettingsRequest(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string? Password,
    string Domain,
    bool IsActive);
