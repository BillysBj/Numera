using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Numera.Api.Lifecycle;

/// <summary>
/// Deletes a tenant's entire Keycloak footprint (its organization and every member's
/// user account) as part of the irreversible tenant purge (Phase B1 Scheibe 2).
/// </summary>
/// <remarks>
/// Self-contained (only <see cref="IHttpClientFactory"/> + <see cref="IConfiguration"/>) so the
/// background worker can resolve it without dragging the full <c>InvitationService</c> dependency
/// graph; it mirrors that service's admin-client + admin-token pattern. The organization id equals
/// the tenant id. All deletes are idempotent: a <c>404 Not Found</c> means the object is already
/// gone and is treated as success, so a re-run after a partial failure is safe.
/// </remarks>
public sealed class KeycloakTenantPurger
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    /// <summary>Creates the purger over the shared HTTP client factory and configuration.</summary>
    public KeycloakTenantPurger(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// True when a Keycloak admin endpoint is configured. A single-tenant self-hosted install
    /// without the admin API configured skips the Keycloak step entirely.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Keycloak:AdminBaseUrl"])
        && !string.IsNullOrWhiteSpace(_configuration["Keycloak:AdminUsername"]);

    /// <summary>
    /// Removes the tenant's organization and all its member user accounts. Returns the number of
    /// user accounts deleted. Throws only on an unexpected (non-404) admin API error.
    /// </summary>
    public async Task<int> PurgeAsync(Guid tenantId, CancellationToken ct)
    {
        var realm = Require("Keycloak:Realm");
        using var http = CreateAdminClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetAdminTokenAsync(http, ct).ConfigureAwait(false));

        // 1) Enumerate the organization's members BEFORE deleting the org (the members
        //    endpoint 404s once the organization is gone).
        var memberIds = await ListMemberUserIdsAsync(http, realm, tenantId, ct).ConfigureAwait(false);

        // 2) Delete each member's Keycloak user account (404 = already removed).
        var deleted = 0;
        foreach (var userId in memberIds)
        {
            using var userDelete = await http
                .DeleteAsync($"admin/realms/{realm}/users/{userId}", ct)
                .ConfigureAwait(false);
            if (userDelete.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            await EnsureSuccessAsync(userDelete, "delete user", ct).ConfigureAwait(false);
            deleted++;
        }

        // 3) Delete the organization itself (404 = already removed).
        using var orgDelete = await http
            .DeleteAsync($"admin/realms/{realm}/organizations/{tenantId}", ct)
            .ConfigureAwait(false);
        if (orgDelete.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(orgDelete, "delete organization", ct).ConfigureAwait(false);
        }

        return deleted;
    }

    private async Task<List<Guid>> ListMemberUserIdsAsync(
        HttpClient http,
        string realm,
        Guid tenantId,
        CancellationToken ct)
    {
        using var response = await http
            .GetAsync($"admin/realms/{realm}/organizations/{tenantId}/members?max=1000", ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, "list organization members", ct).ConfigureAwait(false);
        var members = await response.Content
            .ReadFromJsonAsync<List<KeycloakMember>>(cancellationToken: ct)
            .ConfigureAwait(false);
        if (members is null)
        {
            return [];
        }

        var ids = new List<Guid>(members.Count);
        foreach (var member in members)
        {
            if (Guid.TryParse(member.Id, out var userId))
            {
                ids.Add(userId);
            }
        }

        return ids;
    }

    private HttpClient CreateAdminClient()
    {
        var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(Require("Keycloak:AdminBaseUrl").TrimEnd('/') + "/");
        return http;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient http, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = Require("Keycloak:AdminUsername"),
            ["password"] = Require("Keycloak:AdminPassword"),
        });
        using var response = await http.PostAsync(
            "realms/master/protocol/openid-connect/token",
            form,
            ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "obtain admin token", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak admin token response had no access_token.");
    }

    private string Require(string key) =>
        _configuration[key]
        ?? throw new InvalidOperationException($"Configuration '{key}' is required for the Keycloak purge.");

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Keycloak failed to {action} ({(int)response.StatusCode} {response.ReasonPhrase}): {body}");
    }

    private sealed record KeycloakMember(string Id);
}
