using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Crm;
using Numera.Platform.Db;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating cross-tenant isolation suite for the CRM (Stammdaten) tables. Proves,
/// on a real postgres:18 as the non-BYPASSRLS <c>numera_app</c> role, that EVERY new
/// CRM table (<c>partners</c>, <c>partner_contacts</c>, <c>partner_notes</c>,
/// <c>partner_activities</c>) is tenant-isolated by Row-Level Security — a hand-written
/// policy on each was mandatory because reflective entity discovery never creates them.
/// </summary>
/// <remarks>
/// Every isolation query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// (no-arg) so the app-level EF query filters are OFF, leaving RLS as the SOLE control
/// under test — exactly like <see cref="RlsIsolationTests"/>. If any table's policy were
/// missing, its cross-tenant rows would leak and these assertions would fail.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class StammdatenRlsTests
{
    private readonly PostgresFixture _fixture;

    public StammdatenRlsTests(PostgresFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- partners

    [Fact]
    public async Task Partners_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedPartnerAsync(tenantA);
        await SeedPartnerAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<BusinessPartner>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, p => Assert.Equal(tenantA, p.TenantId));
        Assert.Equal(0, await contextA.Set<BusinessPartner>().IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenantB));
    }

    [Fact]
    public async Task Partners_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<BusinessPartner>().Add(NewPartner(tenantB));

        await AssertRlsRejectsAsync(contextA);
    }

    // ------------------------------------------------------- partner_contacts

    [Fact]
    public async Task PartnerContacts_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedContactAsync(tenantA);
        await SeedContactAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<PartnerContact>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, c => Assert.Equal(tenantA, c.TenantId));
        Assert.Equal(0, await contextA.Set<PartnerContact>().IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == tenantB));
    }

    [Fact]
    public async Task PartnerContacts_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<PartnerContact>().Add(new PartnerContact
        {
            TenantId = tenantB,
            PartnerId = Guid.CreateVersion7(),
            LastName = "Cross",
        });

        await AssertRlsRejectsAsync(contextA);
    }

    // ---------------------------------------------------------- partner_notes

    [Fact]
    public async Task PartnerNotes_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedNoteAsync(tenantA);
        await SeedNoteAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<PartnerNote>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, n => Assert.Equal(tenantA, n.TenantId));
        Assert.Equal(0, await contextA.Set<PartnerNote>().IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == tenantB));
    }

    [Fact]
    public async Task PartnerNotes_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<PartnerNote>().Add(new PartnerNote
        {
            TenantId = tenantB,
            PartnerId = Guid.CreateVersion7(),
            AuthorUserId = Guid.CreateVersion7(),
            Body = "cross-tenant note",
        });

        await AssertRlsRejectsAsync(contextA);
    }

    // ----------------------------------------------------- partner_activities

    [Fact]
    public async Task PartnerActivities_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedActivityAsync(tenantA);
        await SeedActivityAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<PartnerActivity>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, a => Assert.Equal(tenantA, a.TenantId));
        Assert.Equal(0, await contextA.Set<PartnerActivity>().IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tenantB));
    }

    [Fact]
    public async Task PartnerActivities_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<PartnerActivity>().Add(new PartnerActivity
        {
            TenantId = tenantB,
            PartnerId = Guid.CreateVersion7(),
            Type = PartnerActivityType.PartnerCreated,
            Summary = "cross-tenant activity",
        });

        await AssertRlsRejectsAsync(contextA);
    }

    // --------------------------------------------------------- fail-closed all

    [Fact]
    public async Task Reading_without_a_tenant_context_never_leaks_any_CRM_table()
    {
        // Seed one row per table for a real tenant ...
        var tenant = Guid.CreateVersion7();
        await SeedPartnerAsync(tenant);
        await SeedContactAsync(tenant);
        await SeedNoteAsync(tenant);
        await SeedActivityAsync(tenant);

        // ... then read every CRM table with NO app.current_tenant set. The RLS
        // predicate (tenant_id = current_setting('app.current_tenant')::uuid) must
        // fail closed: either error on the unset GUC or match nothing. The only
        // unacceptable outcome is another tenant's row leaking through.
        try
        {
            await using var context = _fixture.CreateAppContext(tenantId: null);
            Assert.Empty(await context.Set<BusinessPartner>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<PartnerContact>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<PartnerNote>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<PartnerActivity>().IgnoreQueryFilters().ToListAsync());
        }
        catch (PostgresException)
        {
            // Fail-closed at the database (unset GUC errors the predicate) — equally safe.
        }
    }

    // --------------------------------------------------- archive filter compose

    [Fact]
    public async Task Archived_partner_is_hidden_by_default_but_visible_when_only_the_NotArchived_filter_is_disabled()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        var active = await SeedPartnerAsync(tenantA);
        var archived = await SeedPartnerAsync(tenantA, archived: true);
        // A different tenant's archived partner must never appear for tenant A even
        // with the archive filter off — the Tenant filter (and RLS) still apply.
        await SeedPartnerAsync(tenantB, archived: true);

        await using var contextA = _fixture.CreateAppContext(tenantA);

        // Default query: both named filters ON → archived row hidden, active shown.
        var defaultRows = await contextA.Set<BusinessPartner>().ToListAsync();
        Assert.Contains(defaultRows, p => p.Id == active);
        Assert.DoesNotContain(defaultRows, p => p.Id == archived);

        // Disable ONLY the NotArchived filter → archived row appears, Tenant filter
        // still composes so tenant B's archived partner stays invisible.
        var withArchived = await contextA.Set<BusinessPartner>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .ToListAsync();
        Assert.Contains(withArchived, p => p.Id == archived);
        Assert.Contains(withArchived, p => p.Id == active);
        Assert.All(withArchived, p => Assert.Equal(tenantA, p.TenantId));
    }

    // ------------------------------------------------------------- seed helpers

    private static BusinessPartner NewPartner(Guid tenantId, bool archived = false) => new()
    {
        TenantId = tenantId,
        Name = "Muster GmbH",
        IsCustomer = true,
        BillingAddress = new Address
        {
            Street = "Musterstr. 1",
            PostalCode = "10115",
            City = "Berlin",
            CountryCode = "DE",
        },
        ArchivedAt = archived ? DateTimeOffset.UtcNow : null,
    };

    private async Task<Guid> SeedPartnerAsync(Guid tenantId, bool archived = false)
    {
        var partner = NewPartner(tenantId, archived);
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<BusinessPartner>().Add(partner);
        await context.SaveChangesAsync();
        return partner.Id;
    }

    private async Task SeedContactAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<PartnerContact>().Add(new PartnerContact
        {
            TenantId = tenantId,
            PartnerId = Guid.CreateVersion7(),
            LastName = "Ansprechpartner",
            IsPrimary = true,
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedNoteAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<PartnerNote>().Add(new PartnerNote
        {
            TenantId = tenantId,
            PartnerId = Guid.CreateVersion7(),
            AuthorUserId = Guid.CreateVersion7(),
            Body = "Notiz",
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedActivityAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<PartnerActivity>().Add(new PartnerActivity
        {
            TenantId = tenantId,
            PartnerId = Guid.CreateVersion7(),
            Type = PartnerActivityType.PartnerCreated,
            Summary = "angelegt",
        });
        await context.SaveChangesAsync();
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
