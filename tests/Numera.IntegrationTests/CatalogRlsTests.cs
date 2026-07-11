using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Catalog;
using Numera.Platform.Db;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating cross-tenant isolation suite for the catalog table. Proves, on a real
/// postgres:18 as the non-BYPASSRLS <c>numera_app</c> role, that <c>catalog_items</c>
/// is tenant-isolated by Row-Level Security (a hand-written policy was mandatory —
/// reflective entity discovery never creates one) AND that the article number is
/// unique per tenant among non-archived rows while freely reusable across tenants.
/// </summary>
/// <remarks>
/// Every isolation query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// (no-arg) so the app-level EF query filters are OFF, leaving RLS as the SOLE control
/// under test — exactly like <see cref="StammdatenRlsTests"/>. If the policy were
/// missing, cross-tenant rows would leak and these assertions would fail.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CatalogRlsTests
{
    private readonly PostgresFixture _fixture;

    public CatalogRlsTests(PostgresFixture fixture) => _fixture = fixture;

    // ----------------------------------------------------- cross-tenant reads

    [Fact]
    public async Task CatalogItems_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedItemAsync(tenantA);
        await SeedItemAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<CatalogItem>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, i => Assert.Equal(tenantA, i.TenantId));
        Assert.Equal(0, await contextA.Set<CatalogItem>().IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == tenantB));
    }

    [Fact]
    public async Task CatalogItems_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<CatalogItem>().Add(NewItem(tenantB));

        await AssertRlsRejectsAsync(contextA);
    }

    [Fact]
    public async Task Reading_without_a_tenant_context_never_leaks_catalog_items()
    {
        var tenant = Guid.CreateVersion7();
        await SeedItemAsync(tenant);

        // Read with NO app.current_tenant set. The RLS predicate must fail closed:
        // either error on the unset GUC or match nothing. The only unacceptable
        // outcome is another tenant's row leaking through.
        try
        {
            await using var context = _fixture.CreateAppContext(tenantId: null);
            Assert.Empty(await context.Set<CatalogItem>().IgnoreQueryFilters().ToListAsync());
        }
        catch (PostgresException)
        {
            // Fail-closed at the database (unset GUC errors the predicate) — equally safe.
        }
    }

    // ------------------------------------------------- per-tenant unique number

    [Fact]
    public async Task Duplicate_item_number_for_the_same_tenant_is_rejected()
    {
        var tenant = Guid.CreateVersion7();
        await SeedItemAsync(tenant, itemNumber: "ART-100");

        await using var context = _fixture.CreateAppContext(tenant);
        context.Set<CatalogItem>().Add(NewItem(tenant, itemNumber: "ART-100"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(
            "ux_catalog_items_tenant_item_number",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_item_number_under_a_different_tenant_is_allowed()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await SeedItemAsync(tenantA, itemNumber: "ART-200");

        // The same number for a DIFFERENT tenant must succeed — uniqueness is per tenant.
        await using var contextB = _fixture.CreateAppContext(tenantB);
        contextB.Set<CatalogItem>().Add(NewItem(tenantB, itemNumber: "ART-200"));
        await contextB.SaveChangesAsync(); // must not throw

        Assert.Equal(1, await contextB.Set<CatalogItem>().IgnoreQueryFilters()
            .CountAsync(i => i.ItemNumber == "ART-200"));
    }

    [Fact]
    public async Task Archived_item_number_can_be_reused_within_the_same_tenant()
    {
        var tenant = Guid.CreateVersion7();
        // An ARCHIVED item does not block reuse of its number (partial index WHERE archived_at IS NULL).
        await SeedItemAsync(tenant, itemNumber: "ART-300", archived: true);

        await using var context = _fixture.CreateAppContext(tenant);
        context.Set<CatalogItem>().Add(NewItem(tenant, itemNumber: "ART-300"));
        await context.SaveChangesAsync(); // must not throw

        Assert.Equal(1, await context.Set<CatalogItem>()
            .CountAsync(i => i.ItemNumber == "ART-300")); // one ACTIVE row visible by default
    }

    // --------------------------------------------------- archive filter compose

    [Fact]
    public async Task Archived_item_is_hidden_by_default_but_visible_when_only_the_NotArchived_filter_is_disabled()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        var active = await SeedItemAsync(tenantA, itemNumber: "ACT-1");
        var archived = await SeedItemAsync(tenantA, itemNumber: "ARC-1", archived: true);
        // A different tenant's archived item must never appear for tenant A even with
        // the archive filter off — the Tenant filter (and RLS) still apply.
        await SeedItemAsync(tenantB, itemNumber: "ARC-1", archived: true);

        await using var contextA = _fixture.CreateAppContext(tenantA);

        // Default query: both named filters ON → archived row hidden, active shown.
        var defaultRows = await contextA.Set<CatalogItem>().ToListAsync();
        Assert.Contains(defaultRows, i => i.Id == active);
        Assert.DoesNotContain(defaultRows, i => i.Id == archived);

        // Disable ONLY the NotArchived filter → archived row appears, Tenant filter
        // still composes so tenant B's archived item stays invisible.
        var withArchived = await contextA.Set<CatalogItem>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .ToListAsync();
        Assert.Contains(withArchived, i => i.Id == archived);
        Assert.Contains(withArchived, i => i.Id == active);
        Assert.All(withArchived, i => Assert.Equal(tenantA, i.TenantId));
    }

    // ------------------------------------------------------------- seed helpers

    private static CatalogItem NewItem(Guid tenantId, string itemNumber = "ART-1", bool archived = false) => new()
    {
        TenantId = tenantId,
        ItemNumber = itemNumber,
        Name = "Musterartikel",
        Kind = CatalogItemKind.Product,
        UnitCode = UnitOfMeasure.Piece,
        NetPrice = 19.9900m,
        Currency = "EUR",
        TaxCategory = Numera.Platform.Money.TaxCategory.S,
        VatRatePercent = 19.00m,
        ArchivedAt = archived ? DateTimeOffset.UtcNow : null,
    };

    private async Task<Guid> SeedItemAsync(Guid tenantId, string itemNumber = "ART-1", bool archived = false)
    {
        var item = NewItem(tenantId, itemNumber, archived);
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<CatalogItem>().Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }

    private static async Task AssertRlsRejectsAsync(NumeraDbContext context)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
