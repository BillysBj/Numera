using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>
/// The trivial background job that proves the Hangfire skeleton end to end:
/// it is <b>enqueued after</b> the registration transaction commits, and when it
/// runs it <b>re-establishes tenant context</b> so that RLS applies inside the job
/// exactly as it does inside a request. No SMTP yet — it just resolves the tenant
/// under RLS and logs, standing in for a future welcome email.
/// </summary>
/// <remarks>
/// The job opens its own DI scope and calls <see cref="ICurrentTenant.SetTenant"/>
/// from its argument, so the <c>TenantConnectionInterceptor</c> sets
/// <c>app.current_tenant</c> when the scoped <see cref="NumeraDbContext"/> opens its
/// connection — the same seam a request uses. Reading the tenant's own row back
/// under RLS demonstrates the background tenant context is live.
/// </remarks>
public sealed class WelcomeEmailJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WelcomeEmailJob> _logger;

    /// <summary>Creates the job over the root scope factory (Hangfire activates it).</summary>
    public WelcomeEmailJob(IServiceScopeFactory scopeFactory, ILogger<WelcomeEmailJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Re-establishes tenant context for <paramref name="tenantId"/> in a fresh scope,
    /// reads the tenant's own row under RLS to prove isolation is active in the job,
    /// and logs the (stand-in) welcome email for <paramref name="userId"/>.
    /// </summary>
    public async Task SendAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Re-establish the tenant the interceptor will push into app.current_tenant.
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        var tenantName = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Welcome email (stub) for user {UserId} of tenant {TenantName} ({TenantId}).",
            userId, tenantName, tenantId);
    }
}
