using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Audit;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Proves the <c>audit_events</c> table is DB-enforced append-only on a real
/// postgres:18 as the runtime <c>numera_app</c> role (success criterion 3): a row can
/// be INSERTed and SELECTed, but any UPDATE or DELETE is rejected by the database.
/// </summary>
/// <remarks>
/// Two independent layers make the rejection unconditional: <c>UPDATE</c>/<c>DELETE</c>
/// are REVOKEd from <c>numera_app</c> (surfaces as <c>permission denied</c>, SQLSTATE
/// 42501) and a <c>BEFORE UPDATE OR DELETE</c> trigger raises
/// <c>'audit_events is append-only'</c> (SQLSTATE P0001) even for a privileged role.
/// The assertions accept either message so they hold whichever layer fires first.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AuditImmutabilityTests
{
    private readonly PostgresFixture _fixture;

    public AuditImmutabilityTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Insert_and_select_of_an_audit_event_succeed()
    {
        var tenant = Guid.CreateVersion7();

        await using var context = _fixture.CreateAppContext(tenant);
        var audit = new AuditEvent
        {
            TenantId = tenant,
            ActorUserId = Guid.CreateVersion7(),
            Action = "invoice.finalized",
            EntityType = "Invoice",
            EntityId = Guid.CreateVersion7(),
            After = """{"status":"finalized"}""",
        };
        context.Set<AuditEvent>().Add(audit);
        await context.SaveChangesAsync();

        var reloaded = await context.Set<AuditEvent>()
            .IgnoreQueryFilters()
            .SingleAsync(e => e.Id == audit.Id);

        Assert.Equal("invoice.finalized", reloaded.Action);
        Assert.Equal(tenant, reloaded.TenantId);
    }

    [Fact]
    public async Task Update_of_an_audit_event_is_rejected_by_the_database()
    {
        var (tenant, id) = await InsertRowAsync("audit.update.target");

        await using var context = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_events SET action = 'tampered' WHERE id = {id}"));

        AssertAppendOnlyOrDenied(ex);
    }

    [Fact]
    public async Task Delete_of_an_audit_event_is_rejected_by_the_database()
    {
        var (tenant, id) = await InsertRowAsync("audit.delete.target");

        await using var context = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_events WHERE id = {id}"));

        AssertAppendOnlyOrDenied(ex);
    }

    private static void AssertAppendOnlyOrDenied(PostgresException ex)
    {
        // 42501 = insufficient_privilege (REVOKE layer); P0001 = raised trigger.
        var byMessage = ex.MessageText.Contains("append-only", StringComparison.OrdinalIgnoreCase)
                        || ex.MessageText.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
        var bySqlState = ex.SqlState is PostgresErrorCodes.InsufficientPrivilege
                        or PostgresErrorCodes.RaiseException;
        Assert.True(byMessage || bySqlState, $"Unexpected error: {ex.SqlState} {ex.MessageText}");
    }

    private async Task<(Guid Tenant, Guid Id)> InsertRowAsync(string action)
    {
        var tenant = Guid.CreateVersion7();
        await using var context = _fixture.CreateAppContext(tenant);
        var audit = new AuditEvent
        {
            TenantId = tenant,
            ActorUserId = Guid.CreateVersion7(),
            Action = action,
            EntityType = "IntegrationTest",
        };
        context.Set<AuditEvent>().Add(audit);
        await context.SaveChangesAsync();
        return (tenant, audit.Id);
    }
}
