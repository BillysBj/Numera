using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Jobs;
using Numera.Modules.Banking;

namespace Numera.Api.Services;

/// <summary>Central registration seam for Banking module application services.</summary>
public static class BankingModuleServiceCollectionExtensions
{
    /// <summary>Adds Banking module services shared by the API and worker hosts.</summary>
    public static IServiceCollection AddBankingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<BankTransactionIngestService>();
        services.AddTransient<SyncBankTransactionsJob>();
        services.AddTransient<SyncBankTransactionsFanOutJob>();
        return services;
    }
}
