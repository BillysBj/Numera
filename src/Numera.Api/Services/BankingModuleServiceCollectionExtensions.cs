using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Numera.Api.Services;

/// <summary>Central registration seam for Banking module application services.</summary>
public static class BankingModuleServiceCollectionExtensions
{
    /// <summary>Adds Banking module services shared by the API and worker hosts.</summary>
    public static IServiceCollection AddBankingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Later Banking plans add sync, ingest, import, and reconciliation services here.
        _ = configuration;
        return services;
    }
}
