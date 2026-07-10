using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

using Numera.Api.Services;

namespace Numera.Api.Endpoints;

/// <summary>Minimal auth surface for the BFF: register, login (OIDC challenge), logout.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps <c>/api/auth/*</c> endpoints.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // POST /api/auth/register — creates the Keycloak user + org and mirrors tenant+membership.
        group.MapPost("/register", async (
            RegisterBody body,
            RegistrationService registration,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email)
                || string.IsNullOrWhiteSpace(body.Password)
                || string.IsNullOrWhiteSpace(body.CompanyName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["registration"] = ["email, password and companyName are all required."],
                });
            }

            var result = await registration.RegisterAsync(
                new RegistrationRequest(body.Email, body.Password, body.CompanyName), ct)
                .ConfigureAwait(false);

            return Results.Ok(new { tenantId = result.TenantId, userId = result.UserId });
        })
        .AllowAnonymous();

        // GET /api/auth/login — starts the OIDC code+PKCE flow; the cookie session is
        // issued on the callback. returnUrl controls where the SPA lands afterwards.
        group.MapGet("/login", ([FromQuery] string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        // POST /api/auth/logout — clears the cookie session and signs out of Keycloak.
        group.MapPost("/logout", () =>
            Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
            .RequireAuthorization();

        return app;
    }

    /// <summary>Request body for <c>POST /api/auth/register</c>.</summary>
    public sealed record RegisterBody(string Email, string Password, string CompanyName);
}
