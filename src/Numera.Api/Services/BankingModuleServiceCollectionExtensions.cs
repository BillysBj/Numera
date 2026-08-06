using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Numera.Api.Jobs;
using Numera.Api.Services.FinApi;
using Numera.Modules.Banking;

namespace Numera.Api.Services;

/// <summary>Central registration seam for Banking module application services.</summary>
public static class BankingModuleServiceCollectionExtensions
{
    /// <summary>
    /// Adds the provider-neutral banking port, selecting finAPI only when all
    /// required application credentials are configured.
    /// </summary>
    public static IServiceCollection AddBankConnectionProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FinApiOptions>(configuration.GetSection(FinApiOptions.SectionName));
        var finApi = configuration.GetSection(FinApiOptions.SectionName);
        if (!string.IsNullOrWhiteSpace(finApi[nameof(FinApiOptions.ClientId)])
            && !string.IsNullOrWhiteSpace(finApi[nameof(FinApiOptions.ClientSecret)])
            && !string.IsNullOrWhiteSpace(finApi[nameof(FinApiOptions.BaseUrl)]))
        {
            services.AddDataProtection();
            services.AddSingleton<IBankCredentialProtector, DataProtectionBankCredentialProtector>();
            services.AddHttpClient<FinApiClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<FinApiOptions>>().Value;
                http.BaseAddress = new Uri(
                    options.BaseUrl.TrimEnd('/') + "/",
                    UriKind.Absolute);
                http.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddScoped<IBankConnectionProvider, FinApiBankConnectionProvider>();
        }
        else
        {
            services.AddScoped<IBankConnectionProvider, StubBankConnectionProvider>();
        }

        return services;
    }

    /// <summary>Adds Banking module services shared by the API and worker hosts.</summary>
    public static IServiceCollection AddBankingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<BankTransactionIngestService>();
        services.AddTransient<SyncBankTransactionsJob>();
        services.AddTransient<SyncBankTransactionsFanOutJob>();
        services.AddTransient<CheckBankConsentJob>();
        return services;
    }
}
