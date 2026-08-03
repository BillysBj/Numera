using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of tenant ledger setup, idempotency and RLS isolation.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerSetupTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Skr03_setup_creates_settings_and_active_chart()
    {
        var tenant = Guid.CreateVersion7();

        var result = await SetupAsync(tenant, ChartVariant.Skr03);

        AssertStatus(result, StatusCodes.Status201Created);
        await using var read = fixture.CreateAppContext(tenant);
        var settings = await read.Set<LedgerSettings>().AsNoTracking().SingleAsync();
        var accounts = await read.Set<Account>().AsNoTracking().ToListAsync();
        Assert.Equal(ChartVariant.Skr03, settings.ChartVariant);
        Assert.Equal(13, accounts.Count);
        Assert.All(accounts, account => Assert.True(account.IsActive));
        Assert.Contains(accounts, account => account.Number == "8400");
        Assert.Equal(
            ("8400", "1776", Steuerschluessel.Ust19),
            SkrMapping.RevenueMapping(ChartVariant.Skr03, TaxCategory.S, 19m));
    }

    [Fact]
    public async Task Second_setup_returns_conflict_without_duplicate_chart()
    {
        var tenant = Guid.CreateVersion7();
        AssertStatus(await SetupAsync(tenant, ChartVariant.Skr03), StatusCodes.Status201Created);

        var second = await SetupAsync(tenant, ChartVariant.Skr03);

        AssertStatus(second, StatusCodes.Status409Conflict);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(1, await read.Set<LedgerSettings>().CountAsync());
        Assert.Equal(13, await read.Set<Account>().CountAsync());
    }

    [Fact]
    public async Task Other_tenant_cannot_see_chart_or_settings_through_rls()
    {
        var firstTenant = Guid.CreateVersion7();
        AssertStatus(await SetupAsync(firstTenant, ChartVariant.Skr03), StatusCodes.Status201Created);

        await using var other = fixture.CreateAppContext(Guid.CreateVersion7());
        Assert.Equal(0, await other.Set<Account>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await other.Set<LedgerSettings>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Skr04_setup_seeds_the_4400_chart()
    {
        var tenant = Guid.CreateVersion7();

        AssertStatus(await SetupAsync(tenant, ChartVariant.Skr04), StatusCodes.Status201Created);

        await using var read = fixture.CreateAppContext(tenant);
        var settings = await read.Set<LedgerSettings>().AsNoTracking().SingleAsync();
        var accounts = await read.Set<Account>().AsNoTracking().ToListAsync();
        Assert.Equal(ChartVariant.Skr04, settings.ChartVariant);
        Assert.Equal(13, accounts.Count);
        Assert.All(accounts, account => Assert.Equal(ChartVariant.Skr04, account.ChartVariant));
        Assert.Contains(accounts, account => account.Number == "4400");
        Assert.DoesNotContain(accounts, account => account.Number == "8400");
    }

    private async Task<IResult> SetupAsync(Guid tenant, ChartVariant variant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);

        return await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(variant, Besteuerungsart.Soll, Gewinnermittlungsart.Euer, null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            tenantContext,
            CancellationToken.None);
    }

    private static void AssertStatus(IResult result, int expectedStatusCode)
    {
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(expectedStatusCode, statusResult.StatusCode);
    }
}
