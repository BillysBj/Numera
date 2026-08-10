using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Auth;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-PostgreSQL coverage for request-time billing degradation.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BillingDegradationGuardTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Active_subscription_allows_writes_and_reads()
    {
        var tenantId = await SeedTenantAsync(
            TenantPlan.M,
            subscriptionStatus: "active",
            trialEndsAt: DateTimeOffset.UtcNow.AddDays(-1));

        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Get, "/api/documents"));
    }

    [Fact]
    public async Task Within_trial_without_subscription_allows_writes()
    {
        var tenantId = await SeedTenantAsync(
            TenantPlan.S,
            subscriptionStatus: null,
            trialEndsAt: DateTimeOffset.UtcNow.AddDays(2));

        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
    }

    [Fact]
    public async Task Lapsed_trial_blocks_writes_but_preserves_reads_and_checkout_recovery()
    {
        var tenantId = await SeedTenantAsync(
            TenantPlan.S,
            subscriptionStatus: null,
            trialEndsAt: DateTimeOffset.UtcNow.AddDays(-1));

        var blocked = await SendAsync(tenantId, HttpMethods.Post, "/api/documents");
        AssertBillingReadOnly(blocked);

        AssertAllowed(await SendAsync(tenantId, HttpMethods.Get, "/api/documents"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/billing/checkout"));
        Assert.Equal(TenantPlan.Free, await EffectivePlanAsync(tenantId));

        await using var read = fixture.CreateAppContext(tenantId);
        var tenant = await read.Tenants.AsNoTracking().SingleAsync();
        Assert.Equal(TenantPlan.S, tenant.Plan);
        Assert.Null(tenant.SubscriptionStatus);
        Assert.True(tenant.TrialEndsAt < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Canceled_subscription_blocks_writes_but_preserves_reads()
    {
        var tenantId = await SeedTenantAsync(
            TenantPlan.M,
            subscriptionStatus: "canceled",
            trialEndsAt: DateTimeOffset.UtcNow.AddDays(-1));

        AssertBillingReadOnly(
            await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Get, "/api/documents"));

        await using var read = fixture.CreateAppContext(tenantId);
        Assert.Equal(tenantId, (await read.Tenants.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task Past_due_subscription_keeps_full_write_access_during_smart_retries()
    {
        var tenantId = await SeedTenantAsync(
            TenantPlan.M,
            subscriptionStatus: "past_due",
            trialEndsAt: DateTimeOffset.UtcNow.AddDays(-1));

        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
        Assert.Equal(TenantPlan.M, await EffectivePlanAsync(tenantId));
    }

    private async Task<Guid> SeedTenantAsync(
        TenantPlan plan,
        string? subscriptionStatus,
        DateTimeOffset? trialEndsAt)
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Billing degradation test {tenantId}",
            Plan = plan,
            StripeSubscriptionId = subscriptionStatus is null ? null : $"sub_{tenantId:N}",
            SubscriptionStatus = subscriptionStatus,
            TrialEndsAt = trialEndsAt,
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<GuardResponse> SendAsync(Guid tenantId, string method, string path)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(options =>
            options.UseNpgsql(fixture.AppConnectionString));
        services.AddScoped<IBillingState, BillingStateService>();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                authenticationType: "BillingGuardTest")),
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var middleware = new BillingDegradationWriteGuardMiddleware(
            next: nextContext =>
            {
                nextContext.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<BillingDegradationWriteGuardMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            scope.ServiceProvider.GetRequiredService<IBillingState>());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return new GuardResponse(
            context.Response.StatusCode,
            await reader.ReadToEndAsync(),
            context.Response.Headers.Location.ToString());
    }

    private async Task<TenantPlan> EffectivePlanAsync(Guid tenantId)
    {
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenantId);
        await using var db = fixture.CreateAppContext(tenantId);
        return await new BillingStateService(currentTenant, db).EffectivePlanAsync();
    }

    private static void AssertAllowed(GuardResponse response)
    {
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.True(string.IsNullOrEmpty(response.Location));
    }

    private static void AssertBillingReadOnly(GuardResponse response)
    {
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.True(string.IsNullOrEmpty(response.Location));

        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal("billing_read_only", body.RootElement.GetProperty("error").GetString());
        Assert.Contains(
            "Nur-Lese-Modus",
            body.RootElement.GetProperty("title").GetString(),
            StringComparison.Ordinal);
    }

    private sealed record GuardResponse(int StatusCode, string Body, string Location);
}

/// <summary>Database-free contract checks for the explicit billing recovery allow-list.</summary>
public sealed class BillingReadOnlyAccessPolicyTests
{
    [Fact]
    public async Task Unauthenticated_request_bypasses_billing_state_and_reaches_next_middleware()
    {
        var nextCalled = false;
        var middleware = new BillingDegradationWriteGuardMiddleware(
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<BillingDegradationWriteGuardMiddleware>.Instance);

        await middleware.InvokeAsync(new DefaultHttpContext(), new ThrowingBillingState());

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("GET", "/api/documents")]
    [InlineData("HEAD", "/api/ledger")]
    [InlineData("OPTIONS", "/api/banking")]
    [InlineData("POST", "/health")]
    [InlineData("POST", "/api/auth/register")]
    [InlineData("POST", "/api/me")]
    [InlineData("POST", "/api/billing/checkout")]
    [InlineData("POST", "/api/billing/portal")]
    [InlineData("POST", "/api/billing/status")]
    [InlineData("POST", "/api/billing/webhook")]
    public void Reads_and_recovery_paths_are_allowed(string method, string path) =>
        Assert.True(BillingReadOnlyAccessPolicy.IsAllowed(method, new PathString(path)));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Other_writes_are_blocked(string method) =>
        Assert.False(BillingReadOnlyAccessPolicy.IsAllowed(
            method,
            new PathString("/api/documents")));

    private sealed class ThrowingBillingState : IBillingState
    {
        public Task<bool> IsDegradedAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unauthenticated requests must not resolve billing state.");

        public Task<TenantPlan> EffectivePlanAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unauthenticated requests must not resolve billing state.");
    }
}
