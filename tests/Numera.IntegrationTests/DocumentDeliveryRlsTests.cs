using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Rendering;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating cross-tenant isolation suite for the Phase-4 delivery tables —
/// <c>document_render</c> (the rendered invoice PDF blob) and <c>document_email</c> (the
/// send record). Proves, on a real postgres:18 as the non-BYPASSRLS <c>numera_app</c> role,
/// that the hand-written <c>tenant_isolation</c> RLS policy on each new table actually
/// isolates: a missing policy would leak one tenant's rendered invoice / send status to
/// another and would make these assertions fail (the hard gate).
/// </summary>
/// <remarks>
/// Every isolation read uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// (no-arg) so the app-level EF tenant filter is OFF, leaving RLS as the SOLE control under
/// test — exactly like <see cref="CompanyProfileRlsTests"/>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DocumentDeliveryRlsTests
{
    private readonly PostgresFixture _fixture;

    public DocumentDeliveryRlsTests(PostgresFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------- document_render

    [Fact]
    public async Task DocumentRender_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedRenderAsync(tenantA);
        await SeedRenderAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<DocumentRender>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
        Assert.Equal(0, await contextA.Set<DocumentRender>().IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == tenantB));
    }

    [Fact]
    public async Task DocumentRender_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        // GUC is tenant A, but the row is stamped for tenant B → WITH CHECK rejects it.
        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<DocumentRender>().Add(NewRender(tenantB));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => contextA.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------- document_email

    [Fact]
    public async Task DocumentEmail_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedEmailAsync(tenantA);
        await SeedEmailAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<DocumentEmail>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, e => Assert.Equal(tenantA, e.TenantId));
        Assert.Equal(0, await contextA.Set<DocumentEmail>().IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == tenantB));
    }

    [Fact]
    public async Task DocumentEmail_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<DocumentEmail>().Add(NewEmail(tenantB));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => contextA.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- seed helpers

    private static DocumentRender NewRender(Guid tenantId) => new()
    {
        TenantId = tenantId,
        DocumentId = Guid.CreateVersion7(),
        PdfBytes = [0x25, 0x50, 0x44, 0x46], // "%PDF" — a tiny non-empty blob
        DocumentNumber = "RE-2026-00001",
        Language = "de",
        ByteSize = 4,
        RenderedAt = DateTimeOffset.UtcNow,
    };

    private static DocumentEmail NewEmail(Guid tenantId) => new()
    {
        TenantId = tenantId,
        DocumentId = Guid.CreateVersion7(),
        ToAddress = "kunde@example.com",
        Subject = "Ihre Rechnung RE-2026-00001",
        Status = EmailStatus.Queued,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task SeedRenderAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<DocumentRender>().Add(NewRender(tenantId));
        await context.SaveChangesAsync();
    }

    private async Task SeedEmailAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<DocumentEmail>().Add(NewEmail(tenantId));
        await context.SaveChangesAsync();
    }
}
