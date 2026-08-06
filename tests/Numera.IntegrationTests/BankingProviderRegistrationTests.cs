using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Services;
using Numera.Modules.Banking;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>DI contract for the safe, no-network banking provider default.</summary>
public sealed class BankingProviderRegistrationTests
{
    [Fact]
    public void Missing_finapi_configuration_resolves_stub_provider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddBankConnectionProvider(configuration);

        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = serviceProvider.CreateScope();

        Assert.IsType<StubBankConnectionProvider>(
            scope.ServiceProvider.GetRequiredService<IBankConnectionProvider>());
    }
}
