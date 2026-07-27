using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of dunning defaults, validation, RLS and schema constraints.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DunningConfigTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Fresh_tenant_read_materializes_four_level_german_defaults()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);

        var levels = await CreateService(db, tenant).GetConfigAsync();

        Assert.Equal(4, levels.Count);
        Assert.Equal([0, 1, 2, 3], levels.Select(x => x.Level));
        Assert.Equal("Zahlungserinnerung", levels[0].Name);
        Assert.Equal([7, 14, 28, 42], levels.Select(x => x.DaysAfterDue));
        Assert.Equal([0m, 5m, 10m, 15m], levels.Select(x => x.Fee));
        Assert.False(levels[0].ChargeInterest);
        Assert.True(levels[2].ChargeInterest);
        Assert.Equal(10.27m, levels[3].InterestRatePercent);
        Assert.Equal(4, await db.Set<DunningLevelConfig>().CountAsync());
    }

    [Fact]
    public async Task Upsert_persists_edited_thresholds_and_fees()
    {
        var tenant = Guid.CreateVersion7();
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var service = CreateService(db, tenant);
            var seeded = await service.GetConfigAsync();
            var edited = seeded.Select(x => x with
            {
                DaysAfterDue = x.DaysAfterDue + 2,
                Fee = x.Fee + 1m,
            }).ToList();

            var result = await service.UpsertConfigAsync(edited);
            Assert.True(result.IsValid);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var persisted = await CreateService(read, tenant).GetConfigAsync();
        Assert.Equal([9, 16, 30, 44], persisted.Select(x => x.DaysAfterDue));
        Assert.Equal([1m, 6m, 11m, 16m], persisted.Select(x => x.Fee));
    }

    [Fact]
    public async Task Non_contiguous_or_negative_ladders_are_rejected()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);
        var service = CreateService(db, tenant);
        var invalid = new[]
        {
            Level(0, 7, 0m),
            Level(2, -1, -1m),
        };

        var result = await service.UpsertConfigAsync(invalid);

        Assert.False(result.IsValid);
        Assert.Contains("levels", result.Errors);
        Assert.Contains("daysAfterDue", result.Errors);
        Assert.Contains("fee", result.Errors);
        Assert.Equal(0, await db.Set<DunningLevelConfig>().CountAsync());
    }

    [Fact]
    public async Task Second_tenant_sees_own_defaults_and_not_first_tenant_edits()
    {
        var firstTenant = Guid.CreateVersion7();
        await using (var first = fixture.CreateAppContext(firstTenant))
        {
            var service = CreateService(first, firstTenant);
            var result = await service.UpsertConfigAsync(
                [Level(0, 99, 123m)]);
            Assert.True(result.IsValid);
        }

        var secondTenant = Guid.CreateVersion7();
        await using var second = fixture.CreateAppContext(secondTenant);
        var secondLevels = await CreateService(second, secondTenant).GetConfigAsync();

        Assert.Equal(4, secondLevels.Count);
        Assert.Equal(7, secondLevels[0].DaysAfterDue);
        Assert.Equal(0m, secondLevels[0].Fee);
        Assert.DoesNotContain(secondLevels, x => x.DaysAfterDue == 99 || x.Fee == 123m);
    }

    [Fact]
    public async Task Finalized_open_item_has_dunning_column_defaults()
    {
        var (tenant, openItemId) = await CreateInvoiceAsync();
        await using var db = fixture.CreateAppContext(tenant);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT current_dunning_level, last_dunned_on FROM open_items WHERE id = @id";
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", openItemId));
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task Duplicate_notice_for_same_open_item_and_level_is_rejected()
    {
        var (tenant, openItemId) = await CreateInvoiceAsync();
        await using var db = fixture.CreateAppContext(tenant);
        var documentId = await db.Set<OpenItem>()
            .Where(x => x.Id == openItemId)
            .Select(x => x.DocumentId)
            .SingleAsync();
        db.Add(Notice(tenant, openItemId, documentId, 1));
        await db.SaveChangesAsync();
        db.Add(Notice(tenant, openItemId, documentId, 1));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ix_dunning_notice_tenant_id_open_item_id_level", postgres.ConstraintName);
    }

    private async Task<(Guid Tenant, Guid OpenItemId)> CreateInvoiceAsync()
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            new DateOnly(2026, 7, 1));
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItemId = await read.Set<OpenItem>()
            .Where(x => x.DocumentId == documentId)
            .Select(x => x.Id)
            .SingleAsync();
        return (tenant, openItemId);
    }

    private static DunningLevelConfigDto Level(int level, int days, decimal fee) =>
        new(level, $"Level {level}", days, fee, level >= 2, 10.27m, "Deutsch", "English");

    private static DunningNotice Notice(Guid tenant, Guid openItemId, Guid documentId, int level) =>
        new()
        {
            TenantId = tenant,
            OpenItemId = openItemId,
            DocumentId = documentId,
            Level = level,
            IssuedOn = new DateOnly(2026, 7, 27),
            NewDueDate = new DateOnly(2026, 8, 3),
            OverdueAmount = 119m,
            Fee = 5m,
            Interest = 0m,
            InterestRatePercent = 10.27m,
            TotalToPay = 124m,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static DunningConfigService CreateService(
        Numera.Platform.Db.NumeraDbContext db,
        Guid tenant)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        return new DunningConfigService(db, tenantContext, new NoOpAuditWriter());
    }
}
