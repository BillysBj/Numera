using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Stable Hangfire entry point; plan 07-07 supplies the generation body.</summary>
public sealed class GenerateRecurringInvoiceJob(IServiceScopeFactory scopeFactory)
{
    public Task RunAsync(Guid tenantId, Guid templateId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        // TODO(07-07): create the tenant scope and perform idempotent catch-up generation.
        return Task.CompletedTask;
    }
}
