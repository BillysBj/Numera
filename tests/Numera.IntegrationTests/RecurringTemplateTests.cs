using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Endpoints;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Recurring;
using Numera.Platform.Entitlements;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres coverage for recurring-template storage, RLS and idempotency.</summary>
[Collection(PostgresCollection.Name)]
public sealed class RecurringTemplateTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Template_and_lines_are_tenant_scoped_and_status_changes_persist()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var template = Template(tenant);
        var line = Line(tenant, template.Id);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            db.Add(template);
            db.Add(line);
            await db.SaveChangesAsync();

            template.Status = RecurringStatus.Paused;
            await db.SaveChangesAsync();
            template.Status = RecurringStatus.Active;
            await db.SaveChangesAsync();
        }

        await using (var own = fixture.CreateAppContext(tenant))
        {
            var persisted = await own.Set<RecurringInvoiceTemplate>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == template.Id);
            Assert.Equal(RecurringStatus.Active, persisted.Status);
            Assert.True(persisted.AutoFinalize);
            Assert.Single(persisted.Lines);
            Assert.Equal("Consulting", persisted.Lines[0].Name);
        }

        await using var other = fixture.CreateAppContext(otherTenant);
        Assert.Equal(0, await other.Set<RecurringInvoiceTemplate>().CountAsync());
        Assert.Equal(0, await other.Set<RecurringInvoiceTemplateLine>().CountAsync());
    }

    [Fact]
    public async Task Duplicate_document_for_template_period_is_rejected()
    {
        var tenant = Guid.CreateVersion7();
        var template = Template(tenant);
        await using var db = fixture.CreateAppContext(tenant);
        db.Add(template);
        await db.SaveChangesAsync();

        db.Add(Document(tenant, template.Id, "2026-07"));
        await db.SaveChangesAsync();
        db.Add(Document(tenant, template.Id, "2026-07"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task Scheduling_identity_cron_and_capability_gate_use_locked_contract()
    {
        var tenant = Guid.Parse("018f07e0-0000-7000-8000-000000000001");
        var template = Template(tenant);
        template.IntervalUnit = RecurringIntervalUnit.Quarterly;
        template.IntervalCount = 1;
        template.StartOn = new DateOnly(2026, 7, 15);

        Assert.Equal(
            $"recurring-invoice:{tenant}:{template.Id}",
            RecurringInvoiceEndpoints.RecurringJobId(tenant, template.Id));
        Assert.Equal("0 0 15 */3 *", RecurringInvoiceEndpoints.CronExpression(template));

        var denied = new RecordingEntitlementService(false);
        Assert.False(await RecurringInvoiceEndpoints.HasCapabilityAsync(denied, CancellationToken.None));
        Assert.Equal(Capability.RecurringInvoices, denied.RequestedCapability);

        var allowed = new RecordingEntitlementService(true);
        Assert.True(await RecurringInvoiceEndpoints.HasCapabilityAsync(allowed, CancellationToken.None));
        Assert.Equal(Capability.RecurringInvoices, allowed.RequestedCapability);
    }

    private static RecurringInvoiceTemplate Template(Guid tenant) => new()
    {
        TenantId = tenant,
        Name = "Monthly consulting",
        PartnerId = Guid.CreateVersion7(),
        Currency = "EUR",
        IntervalUnit = RecurringIntervalUnit.Monthly,
        IntervalCount = 1,
        StartOn = new DateOnly(2026, 7, 1),
        EndMode = RecurringEndMode.Never,
        NextRunOn = new DateOnly(2026, 7, 1),
        Status = RecurringStatus.Active,
        AutoFinalize = true,
    };

    private static RecurringInvoiceTemplateLine Line(Guid tenant, Guid templateId) => new()
    {
        TenantId = tenant,
        TemplateId = templateId,
        LineNumber = 1,
        Name = "Consulting",
        Quantity = 1m,
        UnitCode = "C62",
        NetUnitPrice = 100m,
        TaxCategory = TaxCategory.S,
        VatRatePercent = 19m,
    };

    private static SalesDocument Document(Guid tenant, Guid templateId, string periodKey) => new()
    {
        TenantId = tenant,
        DocumentType = DocumentType.Rechnung,
        Status = DocumentStatus.Draft,
        DocumentDate = new DateOnly(2026, 7, 1),
        RecurringTemplateId = templateId,
        RecurringPeriodKey = periodKey,
    };

    private sealed class RecordingEntitlementService(bool result) : IEntitlementService
    {
        public Capability? RequestedCapability { get; private set; }

        public Task<bool> HasCapabilityAsync(
            Capability capability,
            CancellationToken cancellationToken = default)
        {
            RequestedCapability = capability;
            return Task.FromResult(result);
        }

        public Task<IReadOnlySet<Capability>> CurrentCapabilitiesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<Capability>>(new HashSet<Capability>());
    }
}
