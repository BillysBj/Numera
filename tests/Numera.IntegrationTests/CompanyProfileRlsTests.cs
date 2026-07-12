using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Sales;
using Numera.Platform.Db;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating cross-tenant isolation suite for the Sales <c>company_profile</c> table —
/// the §14 UStG issuer master data plan 03-05 snapshots onto finalized invoices. Proves, on
/// a real postgres:18 as the non-BYPASSRLS <c>numera_app</c> role, that the hand-written
/// <c>tenant_isolation</c> RLS policy actually isolates: a leak here would cross-contaminate
/// one tenant's legal identity onto another's documents.
/// </summary>
/// <remarks>
/// Every isolation query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// (no-arg) so the app-level EF tenant filter is OFF, leaving RLS as the SOLE control under
/// test — exactly like <see cref="StammdatenRlsTests"/>. If the policy were missing, the
/// cross-tenant rows would leak and these assertions would fail (the hard gate).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CompanyProfileRlsTests
{
    private readonly PostgresFixture _fixture;

    public CompanyProfileRlsTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CompanyProfile_tenant_A_sees_only_its_own_row_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedProfileAsync(tenantA);
        await SeedProfileAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<CompanyProfile>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, p => Assert.Equal(tenantA, p.TenantId));
        Assert.Equal(0, await contextA.Set<CompanyProfile>().IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantB));
    }

    [Fact]
    public async Task CompanyProfile_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        // GUC is tenant A, but the row is stamped for tenant B → WITH CHECK rejects it.
        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<CompanyProfile>().Add(NewProfile(tenantB));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => contextA.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reading_without_a_tenant_context_never_leaks_the_company_profile()
    {
        var tenant = Guid.CreateVersion7();
        await SeedProfileAsync(tenant);

        // No app.current_tenant set: the RLS predicate must fail closed — either error on
        // the unset GUC or match nothing. The only unacceptable outcome is a row leaking.
        try
        {
            await using var context = _fixture.CreateAppContext(tenantId: null);
            Assert.Empty(await context.Set<CompanyProfile>().IgnoreQueryFilters().ToListAsync());
        }
        catch (PostgresException)
        {
            // Fail-closed at the database (unset GUC errors the predicate) — equally safe.
        }
    }

    // ------------------------------------------------------------- seed helpers

    private static CompanyProfile NewProfile(Guid tenantId) => new()
    {
        TenantId = tenantId,
        LegalName = "Muster GmbH",
        Address = new Address
        {
            Street = "Musterstr. 1",
            PostalCode = "10115",
            City = "Berlin",
            CountryCode = "DE",
        },
        VatId = "DE811907980",
    };

    private async Task SeedProfileAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<CompanyProfile>().Add(NewProfile(tenantId));
        await context.SaveChangesAsync();
    }
}
