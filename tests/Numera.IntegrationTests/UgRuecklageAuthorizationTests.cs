using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Numera.Api.Auth;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Exercises the real authorization middleware without requiring Docker.</summary>
public sealed class UgRuecklageAuthorizationTests
{
    [Theory]
    [InlineData(MembershipRole.Owner, true, 200)]
    [InlineData(MembershipRole.Employee, true, 403)]
    [InlineData(MembershipRole.TaxAdvisor, true, 403)]
    [InlineData(MembershipRole.Owner, false, 401)]
    public async Task Booking_and_settings_update_require_an_authenticated_owner(MembershipRole role, bool authenticated, int expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ICurrentUserRole>(new Role(role));
        builder.Services.AddScoped<IAuthorizationHandler, RequireOwnerHandler>();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("RequireOwner", policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new RequireOwnerRequirement())));
        builder.Services.AddScoped<ICurrentTenant, TenantContext>();
        builder.Services.AddDbContext<NumeraDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused"));
        builder.Services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        builder.Services.AddScoped<RecognitionReader>();
        builder.Services.AddScoped<UstVaCalculator>();
        builder.Services.AddScoped<EuerCalculator>();
        builder.Services.AddScoped<AbschlussCalculator>();
        builder.Services.AddScoped<UgRuecklageCalculator>();
        builder.Services.AddScoped<PostingEngine>();
        builder.Services.AddScoped<ChartSeeder>();
        await using var app = builder.Build();
        app.MapReportEndpoints();
        app.MapLedgerSetupEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Cast<RouteEndpoint>().ToList();
        var preview = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/reports/ug-ruecklage");
        Assert.NotEmpty(preview.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var writes = endpoints.Where(endpoint =>
            endpoint.RoutePattern.RawText == "/api/reports/ug-ruecklage/{jahr:int}/buchen"
            || (endpoint.RoutePattern.RawText == "/api/ledger/settings" && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("PUT"))).ToList();
        Assert.Equal(2, writes.Count);
        foreach (var endpoint in writes)
        {
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == "RequireOwner");
            using var scope = app.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            if (authenticated)
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-user")], "test"));
            }

            http.SetEndpoint(endpoint);
            var reachedHandler = false;
            var pipeline = new ApplicationBuilder(app.Services);
            pipeline.UseAuthorization();
            pipeline.Run(_ => { reachedHandler = true; return Task.CompletedTask; });
            await pipeline.Build()(http);
            Assert.Equal(expected, http.Response.StatusCode);
            Assert.Equal(expected == 200, reachedHandler);
        }
    }

    private sealed class Role(MembershipRole role) : ICurrentUserRole
    {
        public Task<MembershipRole?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<MembershipRole?>(role);
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
