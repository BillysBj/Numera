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
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real endpoint metadata and authorization middleware, without a database dependency.</summary>
public sealed class FixedAssetAuthorizationTests
{
    [Theory]
    [InlineData(MembershipRole.Owner, true, 200)]
    [InlineData(MembershipRole.Employee, true, 403)]
    [InlineData(MembershipRole.TaxAdvisor, true, 403)]
    [InlineData(MembershipRole.Owner, false, 401)]
    public async Task Every_write_requires_an_authenticated_owner(MembershipRole role, bool authenticated, int expected)
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
        builder.Services.AddScoped<AfaCalculator>();
        builder.Services.AddScoped<PostingEngine>();
        builder.Services.AddScoped<AnlagenspiegelCalculator>();
        await using var app = builder.Build();
        app.MapFixedAssetEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Cast<RouteEndpoint>().ToList();
        Assert.Equal(6, endpoints.Count);
        Assert.All(endpoints, endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        var writes = endpoints.Where(endpoint => !endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET")).ToList();
        Assert.Equal(3, writes.Count);
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
