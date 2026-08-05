using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Jobs;
using Numera.IntegrationTests.Fixtures;
using Numera.Modules.Banking;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of idempotent, incremental, tenant-isolated bank sync.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BankTransactionSyncTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ReFetched_window_is_deduplicated_and_cursor_advances()
    {
        var tenantId = Guid.CreateVersion7();
        var accountId = await SeedConnectedAccountAsync(tenantId);
        var initialDrafts = new BankTransactionDraft[]
        {
            new(
                "provider-credit-1",
                150.25m,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                "Invoice RE-1001",
                "Example Customer",
                "DE02120300000000202051",
                "E2E-1001",
                BankTransactionSource.FinApi),
            new(
                null,
                49.75m,
                new DateOnly(2026, 8, 2),
                new DateOnly(2026, 8, 2),
                "  invoice   re-1002  ",
                "Second Customer",
                "DE89 3704 0044 0532 0130 00",
                null,
                BankTransactionSource.FinApi),
            new(
                "provider-debit-1",
                -20.50m,
                new DateOnly(2026, 8, 3),
                null,
                "Bank fee",
                "Example Bank",
                null,
                null,
                BankTransactionSource.FinApi),
        };
        var fake = new FakeBankConnectionProvider { Transactions = initialDrafts };
        await using var provider = BuildProvider(fake);
        var job = provider.GetRequiredService<SyncBankTransactionsJob>();

        await job.RunAsync(tenantId, accountId);

        await using (var firstRead = fixture.CreateAppContext(tenantId))
        {
            var transactions = await firstRead.Set<BankTransaction>()
                .AsNoTracking()
                .OrderBy(transaction => transaction.ValueDate)
                .ToListAsync();
            Assert.Equal(3, transactions.Count);
            Assert.All(
                transactions,
                transaction => Assert.Equal(MatchStatus.Unmatched, transaction.MatchStatus));
            Assert.Equal([150.25m, 49.75m, -20.50m], transactions.Select(x => x.Amount));
            Assert.Equal("provider-credit-1", transactions[0].DedupeKey);
            Assert.Equal(64, transactions[1].DedupeKey.Length);
        }

        // Simulate finAPI's overlapping 89-day fetch by returning the identical window.
        await job.RunAsync(tenantId, accountId);
        await using (var repeatedRead = fixture.CreateAppContext(tenantId))
        {
            Assert.Equal(3, await repeatedRead.Set<BankTransaction>().CountAsync());
        }

        fake.Transactions =
        [
            .. initialDrafts,
            new BankTransactionDraft(
                "provider-credit-2",
                75.10m,
                new DateOnly(2026, 8, 4),
                new DateOnly(2026, 8, 4),
                "Invoice RE-1003",
                "Third Customer",
                "DE12500105170648489890",
                "E2E-1003",
                BankTransactionSource.FinApi),
        ];

        await job.RunAsync(tenantId, accountId);

        await using var finalRead = fixture.CreateAppContext(tenantId);
        Assert.Equal(4, await finalRead.Set<BankTransaction>().CountAsync());
        Assert.Equal(
            "2026-08-04",
            await finalRead.Set<BankAccount>()
                .Where(account => account.Id == accountId)
                .Select(account => account.SyncCursor)
                .SingleAsync());
        Assert.Equal([null, "2026-08-03", "2026-08-03"], fake.ReceivedCursors);
    }

    [Fact]
    public async Task Second_tenant_sees_only_its_own_synchronized_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var accountA = await SeedConnectedAccountAsync(tenantA);
        var accountB = await SeedConnectedAccountAsync(tenantB);
        var fake = new FakeBankConnectionProvider();
        await using var provider = BuildProvider(fake);
        var job = provider.GetRequiredService<SyncBankTransactionsJob>();

        fake.Transactions = [Draft("shared-provider-id", 10.25m, new DateOnly(2026, 8, 1))];
        await job.RunAsync(tenantA, accountA);
        fake.Transactions = [Draft("shared-provider-id", 99.95m, new DateOnly(2026, 8, 2))];
        await job.RunAsync(tenantB, accountB);

        await using (var tenantBRead = fixture.CreateAppContext(tenantB))
        {
            var visible = await tenantBRead.Set<BankTransaction>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync();
            Assert.Equal(tenantB, visible.TenantId);
            Assert.Equal(99.95m, visible.Amount);
        }

        await using var tenantARead = fixture.CreateAppContext(tenantA);
        var tenantATransaction = await tenantARead.Set<BankTransaction>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(tenantA, tenantATransaction.TenantId);
        Assert.Equal(10.25m, tenantATransaction.Amount);
    }

    private async Task<Guid> SeedConnectedAccountAsync(Guid tenantId)
    {
        var connection = new BankConnection
        {
            TenantId = tenantId,
            Provider = BankProvider.Stub,
            ConsentStatus = ConsentStatus.Active,
        };
        var account = new BankAccount
        {
            TenantId = tenantId,
            BankConnectionId = connection.Id,
            Iban = $"DE{tenantId:N}"[..22],
            DisplayName = "Integration test account",
        };

        await using var db = fixture.CreateAppContext(tenantId);
        db.Add(connection);
        db.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private ServiceProvider BuildProvider(FakeBankConnectionProvider fake)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(options =>
            options.UseNpgsql(fixture.AppConnectionString));
        services.AddSingleton(fake);
        services.AddScoped<IBankConnectionProvider>(provider =>
            provider.GetRequiredService<FakeBankConnectionProvider>());
        services.AddScoped<BankTransactionIngestService>();
        services.AddTransient<SyncBankTransactionsJob>();
        return services.BuildServiceProvider();
    }

    private static BankTransactionDraft Draft(string providerId, decimal amount, DateOnly valueDate) =>
        new(
            providerId,
            amount,
            valueDate,
            valueDate,
            "Tenant-isolated transaction",
            "Counterparty",
            "DE02120300000000202051",
            null,
            BankTransactionSource.FinApi);
}
