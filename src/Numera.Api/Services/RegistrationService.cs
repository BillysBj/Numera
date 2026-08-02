using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Hangfire;

using Numera.Api.Jobs;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Input for <see cref="RegistrationService.RegisterAsync"/>.</summary>
/// <param name="Email">The owner's login email.</param>
/// <param name="Password">The owner's initial password.</param>
/// <param name="CompanyName">The company/Mandant display name.</param>
public sealed record RegistrationRequest(string Email, string Password, string CompanyName);

/// <summary>Result of a successful registration.</summary>
/// <param name="TenantId">The created tenant id (== Keycloak organization id).</param>
/// <param name="UserId">The created user id (== Keycloak <c>sub</c>).</param>
public sealed record RegistrationResult(Guid TenantId, Guid UserId);

/// <summary>
/// Registers a new company: creates a Keycloak user + Organization (Keycloak is the
/// IAM source of truth), then mirrors that into the app DB as a <c>tenants</c> row
/// (Keycloak org id as the PK) and an Owner <c>membership</c> row, records a
/// <c>tenant.created</c> audit event in the same transaction, and enqueues the
/// welcome-email job after the commit.
/// </summary>
/// <remarks>
/// RESEARCH Open Question 3: Keycloak owns identity/org membership; the app DB mirrors
/// membership so RLS and joins work per request without a live IAM round-trip. The DB
/// writes run with the new tenant set on <see cref="ICurrentTenant"/> so the RLS
/// <c>WITH CHECK</c> policy (which requires the row's tenant to equal
/// <c>app.current_tenant</c>) admits the bootstrap inserts.
/// </remarks>
public sealed class RegistrationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistrationService> _logger;

    /// <summary>Creates the service.</summary>
    public RegistrationService(
        IHttpClientFactory httpClientFactory,
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IBackgroundJobClient jobs,
        IConfiguration configuration,
        ILogger<RegistrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _currentTenant = currentTenant;
        _jobs = jobs;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Performs the full registration. Throws <see cref="InvalidOperationException"/> if
    /// any Keycloak Admin call fails.
    /// </summary>
    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adminBaseUrl = Require("Keycloak:AdminBaseUrl");
        var realm = Require("Keycloak:Realm");

        using var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(adminBaseUrl.TrimEnd('/') + "/");

        var adminToken = await GetAdminTokenAsync(http, ct).ConfigureAwait(false);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var userId = await CreateUserAsync(http, realm, request, ct).ConfigureAwait(false);
        var organizationId = await CreateOrganizationAsync(http, realm, request, ct).ConfigureAwait(false);
        await AddMemberAsync(http, realm, organizationId, userId, ct).ConfigureAwait(false);

        // Mirror into the app DB. Set the new tenant BEFORE any DB work so the tenant
        // GUC (and thus RLS WITH CHECK) admits the tenants + membership inserts.
        _currentTenant.SetTenant(organizationId);

        _db.Add(new Tenant
        {
            Id = organizationId,
            Name = request.CompanyName,
            Plan = TenantPlan.S,
        });

        _db.Add(new Membership
        {
            TenantId = organizationId,
            UserId = userId,
            Role = MembershipRole.Owner,
        });

        // Audit the bootstrap in the same transaction. This is the one flow where the
        // actor is the just-created user (no prior authenticated principal exists), so
        // the AuditEvent is constructed directly with the known tenant + actor rather
        // than through the ambient IAuditWriter (which stamps from HttpContext).
        _db.Add(new AuditEvent
        {
            TenantId = organizationId,
            ActorUserId = userId,
            Action = "tenant.created",
            EntityType = nameof(Tenant),
            EntityId = organizationId,
            Before = null,
            After = JsonSerializer.Serialize(new { name = request.CompanyName, plan = TenantPlan.S.ToString() }),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Enqueue-after-commit: only once the transaction has persisted do we schedule
        // the welcome email, so a rolled-back registration never leaves an orphan job.
        _jobs.Enqueue<WelcomeEmailJob>(job => job.SendAsync(organizationId, userId, CancellationToken.None));

        _logger.LogInformation(
            "Registered tenant {TenantId} (owner {UserId}) for company {Company}.",
            organizationId, userId, request.CompanyName);

        return new RegistrationResult(organizationId, userId);
    }

    private async Task<string> GetAdminTokenAsync(HttpClient http, CancellationToken ct)
    {
        var adminUser = Require("Keycloak:AdminUsername");
        var adminPassword = Require("Keycloak:AdminPassword");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = adminUser,
            ["password"] = adminPassword,
        });

        using var response = await http.PostAsync("realms/master/protocol/openid-connect/token", form, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "obtain admin token", ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak admin token response had no access_token.");
    }

    private async Task<Guid> CreateUserAsync(HttpClient http, string realm, RegistrationRequest request, CancellationToken ct)
    {
        var payload = new
        {
            username = request.Email,
            email = request.Email,
            enabled = true,
            emailVerified = true,
            credentials = new[]
            {
                new { type = "password", value = request.Password, temporary = false },
            },
        };

        using var response = await http.PostAsJsonAsync($"admin/realms/{realm}/users", payload, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create user", ct).ConfigureAwait(false);

        return ExtractIdFromLocation(response, "user");
    }

    private async Task<Guid> CreateOrganizationAsync(HttpClient http, string realm, RegistrationRequest request, CancellationToken ct)
    {
        // Every registration is its OWN tenant, so the Keycloak organization identity must be
        // unique per realm. It must NOT be derived from the email domain: Keycloak requires a
        // unique org domain, and many small businesses share a provider domain (gmail.com,
        // gmx.de, web.de) — deriving from it makes the second sign-up collide. Use a synthetic,
        // guaranteed-unique alias + domain; the human-readable company name is carried in `name`.
        var token = Guid.NewGuid().ToString("N")[..8];
        var alias = $"{Slugify(request.CompanyName)}-{token}";
        var domain = $"{alias}.numera.local";

        var payload = new
        {
            name = request.CompanyName,
            alias,
            domains = new[]
            {
                new { name = domain, verified = false },
            },
        };

        using var response = await http.PostAsJsonAsync($"admin/realms/{realm}/organizations", payload, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create organization", ct).ConfigureAwait(false);

        return ExtractIdFromLocation(response, "organization");
    }

    private async Task AddMemberAsync(HttpClient http, string realm, Guid organizationId, Guid userId, CancellationToken ct)
    {
        // Keycloak expects the member id as a raw JSON string body.
        using var content = JsonContent.Create(userId.ToString());
        using var response = await http.PostAsync($"admin/realms/{realm}/organizations/{organizationId}/members", content, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "add organization member", ct).ConfigureAwait(false);
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var reason = response.StatusCode == HttpStatusCode.Conflict
            ? "already exists"
            : response.ReasonPhrase;
        throw new InvalidOperationException($"Keycloak failed to {action} ({(int)response.StatusCode} {reason}): {body}");
    }

    private string Require(string key) =>
        _configuration[key] ?? throw new InvalidOperationException($"Configuration '{key}' is not set.");

    private static string Slugify(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrEmpty(slug) ? "org-" + Guid.CreateVersion7().ToString("N")[..8] : slug;
    }
}
