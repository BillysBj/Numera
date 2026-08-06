using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Endpoints;
using Numera.IntegrationTests.Fixtures;
using Numera.Modules.Banking;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of provider account materialization under RLS.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BankAccountRefreshTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Refresh_accounts_upserts_idempotently_and_cannot_cross_tenants()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var connectionA = await SeedConnectionAsync(tenantA);
        var connectionB = await SeedConnectionAsync(tenantB);
        var consentExpiry = DateTimeOffset.UtcNow.AddDays(89);
        var fake = new FakeBankConnectionProvider
        {
            Accounts =
            [
                new BankAccountDraft("finapi-account-1", "DE02120300000000202051", "Girokonto", "EUR"),
                new BankAccountDraft("finapi-account-2", "DE89370400440532013000", "Tagesgeld", "EUR"),
            ],
            Consent = new ConsentSnapshot(ConsentStatus.Active, consentExpiry),
        };

        await using (var tenantADb = fixture.CreateAppContext(tenantA))
        {
            var crossTenantResult = await BankAccountEndpoints.RefreshAccountsAsync(
                connectionB,
                tenantADb,
                fake,
                CancellationToken.None);
            Assert.IsType<NotFound>(crossTenantResult);
            Assert.Empty(fake.ListedConnectionIds);

            _ = await BankAccountEndpoints.RefreshAccountsAsync(
                connectionA,
                tenantADb,
                fake,
                CancellationToken.None);

            fake.Accounts =
            [
                new BankAccountDraft("finapi-account-1", "DE02120300000000202051", "Hauptkonto", "EUR"),
                new BankAccountDraft("finapi-account-2", "DE89370400440532013000", "Tagesgeld", "EUR"),
            ];
            _ = await BankAccountEndpoints.RefreshAccountsAsync(
                connectionA,
                tenantADb,
                fake,
                CancellationToken.None);
        }

        Assert.Equal([connectionA, connectionA], fake.ListedConnectionIds);

        await using (var tenantARead = fixture.CreateAppContext(tenantA))
        {
            var accounts = await tenantARead.Set<BankAccount>()
                .AsNoTracking()
                .OrderBy(account => account.FinApiAccountId)
                .ToListAsync();
            Assert.Equal(2, accounts.Count);
            Assert.Equal(["finapi-account-1", "finapi-account-2"], accounts.Select(x => x.FinApiAccountId));
            Assert.Equal("Hauptkonto", accounts[0].DisplayName);
            Assert.All(accounts, account => Assert.Equal(connectionA, account.BankConnectionId));

            var connection = await tenantARead.Set<BankConnection>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == connectionA);
            Assert.Equal(ConsentStatus.Active, connection.ConsentStatus);
            Assert.NotNull(connection.ConsentExpiresAt);
            Assert.InRange(
                (connection.ConsentExpiresAt.Value - consentExpiry).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
        }

        await using var tenantBRead = fixture.CreateAppContext(tenantB);
        Assert.Empty(await tenantBRead.Set<BankAccount>().AsNoTracking().ToListAsync());
    }

    private async Task<Guid> SeedConnectionAsync(Guid tenantId)
    {
        var connection = new BankConnection
        {
            TenantId = tenantId,
            Provider = BankProvider.FinApi,
            ConsentStatus = ConsentStatus.Pending,
        };
        await using var db = fixture.CreateAppContext(tenantId);
        db.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }
}
