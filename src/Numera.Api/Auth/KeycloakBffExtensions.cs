using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Numera.Api.Auth;

/// <summary>
/// Wires the Backend-for-Frontend (BFF) authentication for the React SPA:
/// the ASP.NET Core host is a <b>confidential OIDC client</b> of Keycloak
/// (realm <c>numera</c>), holds the access/refresh tokens <b>server-side</b>
/// (<c>SaveTokens</c>), and issues the SPA nothing but
/// an <b>HttpOnly, SameSite session cookie</b>. The SPA never sees a token.
/// </summary>
/// <remarks>
/// <para>
/// This is RESEARCH.md Pattern 5 (Keycloak single realm + Organizations + BFF).
/// Code flow + PKCE is the current OAuth browser BCP; the cookie session is what
/// keeps the user "angemeldet" across browser restarts (success criterion 1).
/// </para>
/// <para>
/// The <c>organization</c> scope carries the Keycloak organization id into the
/// token; <c>TenantResolutionMiddleware</c> maps it to <c>tenant_id</c>.
/// </para>
/// </remarks>
public static class KeycloakBffExtensions
{
    /// <summary>Cookie + OpenIdConnect(Keycloak) BFF auth. Tokens stay server-side.</summary>
    public static IServiceCollection AddKeycloakBff(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var authority = configuration["Keycloak:Authority"]
            ?? throw new InvalidOperationException("Keycloak:Authority is not configured.");
        var clientId = configuration["Keycloak:ClientId"]
            ?? throw new InvalidOperationException("Keycloak:ClientId is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Keycloak:ClientSecret is not configured. Provide it via user-secrets " +
                "or the Keycloak__ClientSecret environment variable (see plan 01-06 user_setup).");

        // Dev Keycloak runs over plain HTTP (start-dev). Only relax metadata retrieval
        // when the authority is not HTTPS so production stays strict.
        var requireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                // The persistent SPA session. HttpOnly so JS cannot read it (no XSS token theft);
                // SameSite=Lax so top-level OIDC redirects still carry it; sliding so activity
                // keeps the session alive across browser restarts.
                options.Cookie.Name = "numera.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                // The SPA calls the API with credentials: 'include' and expects 401/403,
                // not a 302 to the login page, on an unauthenticated XHR.
                options.Events.OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.RequireHttpsMetadata = requireHttpsMetadata;

                // Code flow + PKCE; tokens are exchanged server-side and never leave the host.
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                // Tokens live in the encrypted server-side cookie ticket, never in the SPA.
                options.SaveTokens = true;

                options.GetClaimsFromUserInfoEndpoint = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                // Carries the Keycloak organization id -> tenant_id (Pattern 5).
                options.Scope.Add("organization");

                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

                // The Keycloak "sub" is the stable user id the app mirrors as membership.user_id.
                options.ClaimActions.MapJsonKey("sub", "sub");
                // Surface the organization claim onto the principal for TenantResolutionMiddleware.
                options.ClaimActions.MapJsonKey("organization", "organization");
                options.ClaimActions.MapJsonKey("email", "email");
                options.ClaimActions.MapJsonKey("name", "name");
            });

        return services;
    }

    private static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}
