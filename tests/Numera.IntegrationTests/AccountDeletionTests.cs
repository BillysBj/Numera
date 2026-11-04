using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Numera.Api.Auth;
using Numera.Api.Lifecycle;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-PostgreSQL coverage for the Owner-initiated account-deletion request/undo flow (Phase B1).</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountDeletionServiceTests(PostgresFixture fixture)
{
    private const string TenantName = "Löschung Test GmbH";

    [Fact]
    public async Task Request_without_any_export_is_rejected()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: null);

        var result = await RequestAsync(tenantId, TenantName);

        Assert.Equal(AccountDeletionOutcome.ExportRequired, result.Outcome);
        await AssertNotPendingInDbAsync(tenantId);
    }

    [Fact]
    public async Task Request_with_stale_export_is_rejected()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: DateTimeOffset.UtcNow.AddHours(-48));

        var result = await RequestAsync(tenantId, TenantName);

        Assert.Equal(AccountDeletionOutcome.ExportRequired, result.Outcome);
        await AssertNotPendingInDbAsync(tenantId);
    }

    [Fact]
    public async Task Request_with_wrong_confirmation_name_is_rejected()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await RequestAsync(tenantId, "Falscher Name");

        Assert.Equal(AccountDeletionOutcome.NameMismatch, result.Outcome);
        await AssertNotPendingInDbAsync(tenantId);
    }

    [Fact]
    public async Task Request_with_fresh_export_and_matching_name_schedules_deletion()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var actor = Guid.CreateVersion7();

        var result = await RequestAsync(tenantId, "  löschung test gmbh  ", actor);

        Assert.Equal(AccountDeletionOutcome.Ok, result.Outcome);
        Assert.True(result.Status.PendingDeletion);

        await using var read = fixture.CreateAppContext(tenantId);
        var tenant = await read.Tenants.AsNoTracking().SingleAsync();
        Assert.NotNull(tenant.DeletionRequestedAt);
        Assert.Equal(actor, tenant.DeletionRequestedBy);
        Assert.NotNull(tenant.DeletionScheduledFor);

        // Grace window is ~30 days from the request (default), within a tolerant band.
        var expected = tenant.DeletionRequestedAt!.Value.AddDays(30);
        Assert.True((tenant.DeletionScheduledFor!.Value - expected).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Cancel_clears_a_pending_deletion()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(AccountDeletionOutcome.Ok, (await RequestAsync(tenantId, TenantName)).Outcome);

        var result = await CancelAsync(tenantId);

        Assert.Equal(AccountDeletionOutcome.Ok, result.Outcome);
        Assert.False(result.Status.PendingDeletion);
        await AssertNotPendingInDbAsync(tenantId);
    }

    [Fact]
    public async Task Request_when_already_pending_is_idempotent()
    {
        var tenantId = await SeedTenantAsync(lastExportAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var first = await RequestAsync(tenantId, TenantName);
        Assert.Equal(AccountDeletionOutcome.Ok, first.Outcome);

        var second = await RequestAsync(tenantId, TenantName);

        Assert.Equal(AccountDeletionOutcome.AlreadyPending, second.Outcome);
        // The clock must NOT be reset on a repeat request. Compare with a tolerant band:
        // the persisted value round-trips at Postgres microsecond precision, so exact
        // equality against the first (in-memory, 100 ns) value would spuriously differ.
        Assert.True(
            (first.Status.ScheduledFor!.Value - second.Status.ScheduledFor!.Value).Duration()
                < TimeSpan.FromSeconds(1));
    }

    private async Task<Guid> SeedTenantAsync(DateTimeOffset? lastExportAt)
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = TenantName,
            Plan = TenantPlan.M,
            LastExportAt = lastExportAt,
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<AccountDeletionResult> RequestAsync(Guid tenantId, string confirmationName, Guid? actor = null)
    {
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenantId);
        await using var db = fixture.CreateAppContext(tenantId);
        var service = BuildService(db, currentTenant, actor ?? Guid.CreateVersion7());
        return await service.RequestDeletionAsync(confirmationName);
    }

    private async Task<AccountDeletionResult> CancelAsync(Guid tenantId)
    {
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenantId);
        await using var db = fixture.CreateAppContext(tenantId);
        var service = BuildService(db, currentTenant, Guid.CreateVersion7());
        return await service.CancelDeletionAsync();
    }

    private static AccountDeletionService BuildService(NumeraDbContext db, ICurrentTenant currentTenant, Guid actor)
    {
        var currentUser = new TestCurrentUser(actor);
        var audit = new AuditWriter(db, currentTenant, currentUser);
        return new AccountDeletionService(db, currentUser, audit, Options.Create(new AccountDeletionOptions()));
    }

    private async Task AssertNotPendingInDbAsync(Guid tenantId)
    {
        await using var read = fixture.CreateAppContext(tenantId);
        var tenant = await read.Tenants.AsNoTracking().SingleAsync();
        Assert.Null(tenant.DeletionScheduledFor);
        Assert.Null(tenant.DeletionRequestedAt);
        Assert.Null(tenant.DeletionRequestedBy);
    }

    private sealed class TestCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Real-PostgreSQL coverage for the deletion-grace read-only write guard.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountDeletionWriteGuardTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Pending_deletion_blocks_writes_but_allows_reads_export_and_cancel()
    {
        var tenantId = await SeedTenantAsync(pending: true);

        AssertDeletionReadOnly(await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Get, "/api/documents"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Get, "/api/export"));
        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/account/deletion/cancel"));
    }

    [Fact]
    public async Task Not_pending_allows_writes()
    {
        var tenantId = await SeedTenantAsync(pending: false);

        AssertAllowed(await SendAsync(tenantId, HttpMethods.Post, "/api/documents"));
    }

    private async Task<Guid> SeedTenantAsync(bool pending)
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Deletion guard test {tenantId}",
            Plan = TenantPlan.M,
            DeletionRequestedAt = pending ? DateTimeOffset.UtcNow : null,
            DeletionScheduledFor = pending ? DateTimeOffset.UtcNow.AddDays(30) : null,
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
        services.AddScoped<IAccountDeletionState, AccountDeletionState>();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                authenticationType: "AccountDeletionGuardTest")),
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var middleware = new AccountDeletionWriteGuardMiddleware(
            next: nextContext =>
            {
                nextContext.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<AccountDeletionWriteGuardMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            scope.ServiceProvider.GetRequiredService<IAccountDeletionState>());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return new GuardResponse(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static void AssertAllowed(GuardResponse response) =>
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);

    private static void AssertDeletionReadOnly(GuardResponse response)
    {
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal("account_deletion_read_only", body.RootElement.GetProperty("error").GetString());
    }

    private sealed record GuardResponse(int StatusCode, string Body);
}

/// <summary>Database-free contract checks for the deletion-grace read-only allow-list.</summary>
public sealed class AccountDeletionReadOnlyAccessPolicyTests
{
    [Theory]
    [InlineData("GET", "/api/documents")]
    [InlineData("HEAD", "/api/ledger")]
    [InlineData("OPTIONS", "/api/banking")]
    [InlineData("GET", "/api/export")]
    [InlineData("POST", "/health")]
    [InlineData("POST", "/api/auth/register")]
    [InlineData("POST", "/api/me")]
    [InlineData("POST", "/api/account/deletion/cancel")]
    [InlineData("POST", "/api/account/deletion/request")]
    public void Reads_cancel_and_export_are_allowed(string method, string path) =>
        Assert.True(AccountDeletionReadOnlyAccessPolicy.IsAllowed(method, new PathString(path)));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Other_writes_are_blocked(string method) =>
        Assert.False(AccountDeletionReadOnlyAccessPolicy.IsAllowed(
            method,
            new PathString("/api/documents")));

    [Fact]
    public async Task Unauthenticated_request_bypasses_state_and_reaches_next_middleware()
    {
        var nextCalled = false;
        var middleware = new AccountDeletionWriteGuardMiddleware(
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<AccountDeletionWriteGuardMiddleware>.Instance);

        await middleware.InvokeAsync(new DefaultHttpContext(), new ThrowingAccountDeletionState());

        Assert.True(nextCalled);
    }

    private sealed class ThrowingAccountDeletionState : IAccountDeletionState
    {
        public Task<bool> IsPendingDeletionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unauthenticated requests must not resolve deletion state.");
    }
}
