using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Numera.Api.Auth;
using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Auth;

/// <summary>Real authorization/middleware and endpoint binders with an in-memory store; no Docker.</summary>
public sealed class AreaPermissionTests
{
    public static TheoryData<string, string> MappedPaths => new()
    {
        { "/api/documents", "Documents" }, { "/api/open-items", "OpenItems" },
        { "/api/payments", "OpenItems" }, { "/api/dunning", "Dunning" },
        { "/api/recurring-templates", "Recurring" }, { "/api/receipts", "Inbound" },
        { "/api/inbound-documents", "Inbound" }, { "/api/bank-accounts", "Banking" },
        { "/api/bank-transactions", "Banking" }, { "/api/partners", "Partners" },
        { "/api/catalog-items", "Catalog" }, { "/api/reports", "Reports" },
        { "/api/vat-payments", "Reports" },
    };

    [Theory]
    [MemberData(nameof(MappedPaths))]
    public async Task Every_area_is_segment_aware_and_null_remains_unrestricted(string path, string area)
    {
        Assert.Equal(area, AreaPermissions.Resolve(path));
        Assert.Equal(area, AreaPermissions.Resolve(path.ToUpperInvariant() + "/nested"));
        Assert.Null(AreaPermissions.Resolve(path + "-other"));
        await using var app = await CreateApp(MembershipRole.Employee, null);
        foreach (var method in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" })
        {
            Assert.Equal(200, (await Invoke(app, method, path)).Status);
        }
    }

    [Theory]
    [MemberData(nameof(MappedPaths))]
    public async Task Empty_areas_deny_every_mapped_prefix_and_nested_write(string path, string area)
    {
        await using var app = await CreateApp(MembershipRole.Employee, "");
        var response = await Invoke(app, "GET", path);
        Assert.Equal(403, response.Status);
        Assert.Equal("area_forbidden", response.Body.GetProperty("error").GetString());
        Assert.Equal(403, (await Invoke(app, "DELETE", path + "/nested")).Status);
        Assert.Contains(area, AreaPermissions.All);
    }

    [Fact]
    public async Task Documents_only_employee_and_me_contract()
    {
        await using var app = await CreateApp(MembershipRole.Employee, "Documents");
        Assert.Equal(200, (await Invoke(app, "GET", "/api/documents")).Status);
        var denied = await Invoke(app, "GET", "/api/partners");
        Assert.Equal(403, denied.Status);
        Assert.Equal("area_forbidden", denied.Body.GetProperty("error").GetString());
        var me = await Invoke(app, "GET", "/api/me");
        Assert.Equal(200, me.Status);
        Assert.Equal("Employee", me.Body.GetProperty("role").GetString());
        Assert.Equal("Documents", Assert.Single(me.Body.GetProperty("allowedAreas").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/dashboard")]
    [InlineData("/api/auth")]
    [InlineData("/api/features")]
    [InlineData("/api/billing")]
    [InlineData("/health")]
    public async Task Empty_employee_areas_leave_common_routes_available(string path)
    {
        await using var app = await CreateApp(MembershipRole.Employee, "");
        Assert.Equal(200, (await Invoke(app, "GET", path)).Status);
    }

    [Fact]
    public async Task Owner_bypasses_empty_areas_and_me_exposes_null()
    {
        await using var app = await CreateApp(MembershipRole.Owner, "");
        foreach (var row in MappedPaths)
        {
            Assert.Equal(200, (await Invoke(app, "DELETE", (string)row[0])).Status);
        }
        Assert.Equal(JsonValueKind.Null, (await Invoke(app, "GET", "/api/me")).Body.GetProperty("allowedAreas").ValueKind);
    }

    [Fact]
    public async Task Tax_advisor_keeps_existing_read_policy_regardless_of_areas()
    {
        await using var app = await CreateApp(MembershipRole.TaxAdvisor, "");
        Assert.Equal(200, (await Invoke(app, "GET", "/api/documents")).Status);
        foreach (var (method, path) in new[] { ("GET", "/api/partners"), ("PUT", "/api/documents") })
        {
            var response = await Invoke(app, method, path);
            Assert.Equal(403, response.Status);
            Assert.Equal("read_only_role", response.Body.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData(MembershipRole.Employee, 403)]
    [InlineData(MembershipRole.Owner, 200)]
    public async Task Company_profile_writes_require_owner_but_get_is_available(MembershipRole role, int expected)
    {
        await using var app = await CreateApp(role, null);
        Assert.Equal(200, (await Invoke(app, "GET", "/api/company-profile/")).Status);
        var result = await Invoke(app, "PUT", "/api/company-profile/", """
            {"legalName":"Test GmbH","address":{"street":"Test 1","postalCode":"10115","city":"Berlin","countryCode":"DE"},"taxNumber":"123/456/789"}
            """);
        Assert.Equal(expected, result.Status);
        var logo = Endpoints(app).Single(e => e.RoutePattern.RawText == "/api/company-profile/logo"
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("PUT"));
        Assert.Contains(logo.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "RequireOwner");
        if (role == MembershipRole.Employee)
        {
            Assert.Equal(403, (await Invoke(app, "PUT", "/api/company-profile/logo")).Status);
        }
    }

    [Fact]
    public async Task Owner_sets_deduplicated_areas_and_empty_set_but_invalid_keys_do_not_change_them()
    {
        await using var app = await CreateApp(MembershipRole.Owner, null);
        var memberId = await AddMember(app, MembershipRole.Employee);
        var path = $"/api/team/{memberId}/areas";
        Assert.Equal(204, (await Invoke(app, "PUT", path, "[\"Partners\",\"Documents\",\"Documents\"]")).Status);
        Assert.Equal("Documents,Partners", await StoredAreas(app, memberId));
        foreach (var invalid in new[] { "[\"Unknown\"]", "[\"documents\"]", "[null]", "null" })
        {
            Assert.Equal(400, (await Invoke(app, "PUT", path, invalid)).Status);
            Assert.Equal("Documents,Partners", await StoredAreas(app, memberId));
        }
        Assert.Equal(204, (await Invoke(app, "PUT", path, "[]")).Status);
        Assert.Equal("", await StoredAreas(app, memberId));
        Assert.Equal(404, (await Invoke(app, "PUT", $"/api/team/{Guid.NewGuid()}/areas", "[]")).Status);
    }

    [Theory]
    [InlineData(MembershipRole.Owner)]
    [InlineData(MembershipRole.TaxAdvisor)]
    public async Task Non_employee_targets_cannot_have_areas_changed(MembershipRole targetRole)
    {
        await using var app = await CreateApp(MembershipRole.Owner, null);
        var targetId = await AddMember(app, targetRole);
        Assert.Equal(403, (await Invoke(app, "PUT", $"/api/team/{targetId}/areas", "[]")).Status);
        Assert.Null(await StoredAreas(app, targetId));
    }

    [Fact]
    public async Task Employee_cannot_set_permissions_even_with_unrestricted_areas()
    {
        await using var app = await CreateApp(MembershipRole.Employee, null);
        var targetId = await AddMember(app, MembershipRole.Employee);
        Assert.Equal(403, (await Invoke(app, "PUT", $"/api/team/{targetId}/areas", "[]")).Status);
        Assert.Null(await StoredAreas(app, targetId));
    }

    [Fact]
    public async Task Permissions_resolve_current_tenant_and_user_once_per_request()
    {
        await using var app = await CreateApp(MembershipRole.Employee, "Documents");
        using var scope = app.Services.CreateScope();
        var role = scope.ServiceProvider.GetRequiredService<ICurrentUserRole>();
        var permissions = scope.ServiceProvider.GetRequiredService<ICurrentUserPermissions>();
        Assert.Same(role, permissions);
        Assert.Equal(MembershipRole.Employee, await role.GetAsync());
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        var member = await db.Memberships.SingleAsync();
        member.AllowedAreas = "Partners";
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "Documents" }, await permissions.GetAllowedAreasAsync());
        Assert.Equal(new[] { "Partners" }, (await Invoke(app, "GET", "/api/me")).Body
            .GetProperty("allowedAreas").EnumerateArray().Select(x => x.GetString()));
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetTenant(Guid.NewGuid());
        var other = new CurrentUserRole(tenant, scope.ServiceProvider.GetRequiredService<ICurrentUser>(), db);
        Assert.Null(await other.GetAsync());
    }

    private static async Task<WebApplication> CreateApp(MembershipRole role, string? allowedAreas)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        var tenant = new TenantContext();
        builder.Logging.ClearProviders();
        tenant.SetTenant(Guid.NewGuid());
        var user = new TestUser(Guid.NewGuid());
        var database = Guid.NewGuid().ToString();
        builder.Services.AddSingleton<ICurrentTenant>(tenant);
        builder.Services.AddSingleton<ICurrentUser>(user);
        builder.Services.AddDbContext<NumeraDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<CurrentUserRole>();
        builder.Services.AddScoped<ICurrentUserRole>(sp => sp.GetRequiredService<CurrentUserRole>());
        builder.Services.AddScoped<ICurrentUserPermissions>(sp => sp.GetRequiredService<CurrentUserRole>());
        builder.Services.AddScoped<IAuthorizationHandler, RequireOwnerHandler>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("RequireOwner", policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new RequireOwnerRequirement())));
        builder.Services.AddScoped<InvitationService>();
        builder.Services.AddScoped<IEntitlementService, EntitlementService>();
        builder.Services.AddFeatureManagement();
        builder.Services.AddSingleton<IAuditWriter, NoOpAudit>();
        builder.Services.AddScoped<IValidator<UpdateCompanyProfileRequest>, UpdateCompanyProfileRequestValidator>();
        var app = builder.Build();
        app.MapTeamEndpoints();
        app.MapCompanyProfileEndpoints();
        app.MapMeEndpoints();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        db.Tenants.Add(new Tenant { Id = tenant.TenantId!.Value, Name = "Area test" });
        db.Memberships.Add(new Membership
        {
            TenantId = tenant.TenantId.Value, UserId = user.UserId!.Value, Role = role, AllowedAreas = allowedAreas,
        });
        await db.SaveChangesAsync();
        return app;
    }

    private static RouteEndpoint[] Endpoints(WebApplication app) => ((IEndpointRouteBuilder)app).DataSources
        .SelectMany(source => source.Endpoints).Cast<RouteEndpoint>().ToArray();

    private static async Task<(int Status, JsonElement Body)> Invoke(WebApplication app, string method, string path, string? body = null)
    {
        // Use real endpoint delegates where available. Representative area routes terminate in 200,
        // isolating global authorization from unrelated feature dependencies.
        var endpointPath = path.StartsWith("/api/team/", StringComparison.Ordinal) ? "/api/team/{userId:guid}/areas" : path;
        var endpoint = Endpoints(app).SingleOrDefault(e => e.RoutePattern.RawText == endpointPath
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.ToString()!)], "test"));
        http.Request.Method = method;
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(endpoint);
        if (endpointPath.Contains("{userId", StringComparison.Ordinal)) http.Request.RouteValues["userId"] = path.Split('/')[3];
        if (body is not null)
        {
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = http.Request.Body.Length;
            http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
        }
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseAuthorization();
        pipeline.UseMiddleware<ReadOnlyWriteGuardMiddleware>();
        pipeline.UseMiddleware<AreaPermissionGuardMiddleware>();
        pipeline.Run(endpoint?.RequestDelegate ?? (_ => Task.CompletedTask));
        await pipeline.Build()(http);
        http.Response.Body.Position = 0;
        if (http.Response.Body.Length == 0) return (http.Response.StatusCode, default);
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, json.RootElement.Clone());
    }

    private static async Task<Guid> AddMember(WebApplication app, MembershipRole role)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        var member = new Membership
        {
            TenantId = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().TenantId!.Value,
            UserId = Guid.NewGuid(), Role = role,
        };
        db.Memberships.Add(member);
        await db.SaveChangesAsync();
        return member.UserId;
    }

    private static async Task<string?> StoredAreas(WebApplication app, Guid userId)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<NumeraDbContext>().Memberships.SingleAsync(m => m.UserId == userId)).AllowedAreas;
    }

    private sealed record TestUser(Guid? UserId) : ICurrentUser;
    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
    private sealed class NoOpAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent evt, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
