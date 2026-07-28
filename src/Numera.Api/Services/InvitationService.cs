using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Outcomes for membership mutations that have expected business failures.</summary>
public enum MemberMutationOutcome
{
    Success,
    NotFound,
    LastOwner,
}

/// <summary>
/// Directly adds users to the current Keycloak organization and mirrors their role
/// in the tenant-scoped membership table.
/// </summary>
public sealed class InvitationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvitationService> _logger;
    private readonly IAuditWriter _audit;

    public InvitationService(
        IHttpClientFactory httpClientFactory,
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IConfiguration configuration,
        ILogger<InvitationService> logger,
        IAuditWriter audit)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _currentTenant = currentTenant;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
    }

    /// <summary>Finds or creates a Keycloak user, adds it to the tenant, and mirrors the role.</summary>
    public async Task<Guid> InviteAsync(string email, MembershipRole role, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var realm = Require("Keycloak:Realm");
        using var http = CreateAdminClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetAdminTokenAsync(http, ct).ConfigureAwait(false));

        var userId = await FindOrCreateUserAsync(http, realm, email, ct).ConfigureAwait(false);
        await AddMemberAsync(http, realm, tenantId, userId, ct).ConfigureAwait(false);

        var membership = await _db.Set<Membership>()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        if (membership is null)
        {
            membership = new Membership { TenantId = tenantId, UserId = userId, Role = role };
            _db.Add(membership);
        }
        else
        {
            membership.Role = role;
        }

        await _audit.RecordAsync(
            new TeamMemberAuditEvent(
                "team.member.invited",
                membership.Id,
                null,
                JsonSerializer.Serialize(new { userId, role = role.ToString() })),
            ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Invited user {UserId} to tenant {TenantId} as {Role}.", userId, tenantId, role);
        return userId;
    }

    /// <summary>Changes a member role unless that would demote the tenant's last owner.</summary>
    public async Task<MemberMutationOutcome> ChangeRoleAsync(
        Guid userId,
        MembershipRole role,
        CancellationToken ct)
    {
        var membership = await _db.Set<Membership>()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return MemberMutationOutcome.NotFound;
        }

        if (membership.Role == MembershipRole.Owner
            && role != MembershipRole.Owner
            && await IsLastOwnerAsync(ct).ConfigureAwait(false))
        {
            return MemberMutationOutcome.LastOwner;
        }

        var before = JsonSerializer.Serialize(new { userId, role = membership.Role.ToString() });
        membership.Role = role;
        await _audit.RecordAsync(
            new TeamMemberAuditEvent(
                "team.member.role_changed",
                membership.Id,
                before,
                JsonSerializer.Serialize(new { userId, role = role.ToString() })),
            ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MemberMutationOutcome.Success;
    }

    /// <summary>Removes a member unless it is the tenant's last owner.</summary>
    public async Task<MemberMutationOutcome> RemoveAsync(Guid userId, CancellationToken ct)
    {
        var membership = await _db.Set<Membership>()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return MemberMutationOutcome.NotFound;
        }

        if (membership.Role == MembershipRole.Owner
            && await IsLastOwnerAsync(ct).ConfigureAwait(false))
        {
            return MemberMutationOutcome.LastOwner;
        }

        var tenantId = RequireTenant();
        var realm = Require("Keycloak:Realm");
        using var http = CreateAdminClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetAdminTokenAsync(http, ct).ConfigureAwait(false));
        using var response = await http.DeleteAsync(
            $"admin/realms/{realm}/organizations/{tenantId}/members/{userId}",
            ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "remove organization member", ct).ConfigureAwait(false);

        _db.Remove(membership);
        await _audit.RecordAsync(
            new TeamMemberAuditEvent(
                "team.member.removed",
                membership.Id,
                JsonSerializer.Serialize(new { userId, role = membership.Role.ToString() }),
                null),
            ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MemberMutationOutcome.Success;
    }

    private async Task<bool> IsLastOwnerAsync(CancellationToken ct) =>
        await _db.Set<Membership>()
            .CountAsync(x => x.Role == MembershipRole.Owner, ct)
            .ConfigureAwait(false) == 1;

    private HttpClient CreateAdminClient()
    {
        var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(Require("Keycloak:AdminBaseUrl").TrimEnd('/') + "/");
        return http;
    }

    private async Task<Guid> FindOrCreateUserAsync(
        HttpClient http,
        string realm,
        string email,
        CancellationToken ct)
    {
        var payload = new
        {
            username = email,
            email,
            enabled = true,
            emailVerified = false,
        };
        using var create = await http.PostAsJsonAsync($"admin/realms/{realm}/users", payload, ct)
            .ConfigureAwait(false);
        if (create.IsSuccessStatusCode)
        {
            return ExtractIdFromLocation(create, "user");
        }

        if (create.StatusCode != HttpStatusCode.Conflict)
        {
            await EnsureSuccessAsync(create, "create user", ct).ConfigureAwait(false);
        }

        var path = $"admin/realms/{realm}/users?email={Uri.EscapeDataString(email)}&exact=true";
        using var lookup = await http.GetAsync(path, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(lookup, "find user", ct).ConfigureAwait(false);
        var users = await lookup.Content.ReadFromJsonAsync<List<KeycloakUser>>(cancellationToken: ct)
            .ConfigureAwait(false);
        if (users is null || users.Count == 0 || !Guid.TryParse(users[0].Id, out var userId))
        {
            throw new InvalidOperationException($"Keycloak could not resolve the existing user '{email}'.");
        }

        return userId;
    }

    private async Task AddMemberAsync(
        HttpClient http,
        string realm,
        Guid organizationId,
        Guid userId,
        CancellationToken ct)
    {
        using var content = JsonContent.Create(userId.ToString());
        using var response = await http.PostAsync(
            $"admin/realms/{realm}/organizations/{organizationId}/members",
            content,
            ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            await EnsureSuccessAsync(response, "add organization member", ct).ConfigureAwait(false);
        }
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

    private static Guid ExtractIdFromLocation(HttpResponseMessage response, string what)
    {
        var location = response.Headers.Location?.ToString()
            ?? throw new InvalidOperationException($"Keycloak did not return a Location header when creating the {what}.");
        var lastSegment = location.TrimEnd('/').Split('/')[^1];
        return Guid.TryParse(lastSegment, out var id)
            ? id
            : throw new InvalidOperationException($"Keycloak {what} Location did not end in a GUID: {location}");
    }

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

    private Guid RequireTenant() =>
        _currentTenant.TenantId ?? throw new InvalidOperationException("A tenant is required.");

    private string Require(string key) =>
        _configuration[key] ?? throw new InvalidOperationException($"Configuration '{key}' is not set.");

    private sealed record KeycloakUser(string Id);
}

internal sealed record TeamMemberAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(Membership);
}
