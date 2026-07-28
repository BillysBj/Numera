using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Platform.Db;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Persistence, due-date query, and database RLS coverage for partner tasks.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PartnerTaskTests
{
    private readonly PostgresFixture _fixture;

    public PartnerTaskTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_edit_complete_and_delete_persist()
    {
        var tenantId = Guid.CreateVersion7();
        var task = NewTask(tenantId, "Anrufen", DateOnly.FromDateTime(DateTime.UtcNow));

        await using (var create = _fixture.CreateAppContext(tenantId))
        {
            create.Add(task);
            await create.SaveChangesAsync();
        }

        await using (var edit = _fixture.CreateAppContext(tenantId))
        {
            var persisted = await edit.Set<PartnerTask>().SingleAsync(t => t.Id == task.Id);
            persisted.Title = "Angebot nachfassen";
            persisted.Description = "Telefonisch";
            persisted.Status = PartnerTaskStatus.Done;
            persisted.CompletedAt = DateTimeOffset.UtcNow;
            await edit.SaveChangesAsync();
        }

        await using (var verify = _fixture.CreateAppContext(tenantId))
        {
            var persisted = await verify.Set<PartnerTask>().SingleAsync(t => t.Id == task.Id);
            Assert.Equal("Angebot nachfassen", persisted.Title);
            Assert.Equal("Telefonisch", persisted.Description);
            Assert.Equal(PartnerTaskStatus.Done, persisted.Status);
            Assert.NotNull(persisted.CompletedAt);
            verify.Remove(persisted);
            await verify.SaveChangesAsync();
        }

        await using var deleted = _fixture.CreateAppContext(tenantId);
        Assert.False(await deleted.Set<PartnerTask>().AnyAsync(t => t.Id == task.Id));
    }

    [Fact]
    public async Task Overdue_filter_returns_only_past_due_open_tasks()
    {
        var tenantId = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = NewTask(tenantId, "Überfällig", today.AddDays(-1));
        var future = NewTask(tenantId, "Später", today.AddDays(1));
        var undated = NewTask(tenantId, "Ohne Datum", null);
        var done = NewTask(tenantId, "Schon erledigt", today.AddDays(-2));
        done.Status = PartnerTaskStatus.Done;
        done.CompletedAt = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateAppContext(tenantId);
        context.AddRange(overdue, future, undated, done);
        await context.SaveChangesAsync();

        var rows = await context.Set<PartnerTask>()
            .AsNoTracking()
            .Where(t =>
                t.DueDate != null &&
                t.DueDate < today &&
                t.Status == PartnerTaskStatus.Open)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var only = Assert.Single(rows);
        Assert.Equal(overdue.Id, only.Id);
        Assert.True(
            only.DueDate < today &&
            only.Status == PartnerTaskStatus.Open);
    }

    [Fact]
    public async Task Rls_isolates_other_tenant_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedAsync(NewTask(tenantA, "Tenant A", null));
        await SeedAsync(NewTask(tenantB, "Tenant B", null));

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<PartnerTask>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, task => Assert.Equal(tenantA, task.TenantId));
        Assert.Equal(
            0,
            await contextA.Set<PartnerTask>()
                .IgnoreQueryFilters()
                .CountAsync(task => task.TenantId == tenantB));
    }

    [Fact]
    public async Task Cross_tenant_insert_is_rejected_by_WITH_CHECK()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Add(NewTask(tenantB, "Nicht erlaubt", null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => contextA.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedAsync(PartnerTask task)
    {
        await using var context = _fixture.CreateAppContext(task.TenantId);
        context.Add(task);
        await context.SaveChangesAsync();
    }

    private static PartnerTask NewTask(Guid tenantId, string title, DateOnly? dueDate) => new()
    {
        TenantId = tenantId,
        PartnerId = Guid.CreateVersion7(),
        Title = title,
        DueDate = dueDate,
    };
}
