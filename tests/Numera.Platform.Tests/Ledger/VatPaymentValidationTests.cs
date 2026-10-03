using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Ledger.Tax;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class VatPaymentValidationTests
{
    [Theory]
    [InlineData(null, 0, "amount")]
    [InlineData(0, 0, "amount")]
    [InlineData(-1, 1, "amount")]
    [InlineData(10, 2, "kind")]
    [InlineData(10, -1, "kind")]
    public async Task Invalid_request_is_rejected_before_database_or_audit(int? amount, int kind, string key)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var service = new VatPaymentService(db, tenant, new UnexpectedAudit());
        var result = await service.RecordAsync(amount, (VatPaymentKind)kind, new(2026, 2, 10), null);
        Assert.Equal(PaymentOperationStatus.Invalid, result.Status);
        Assert.Equal(key, result.ErrorKey);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Missing_value_date_is_rejected()
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var result = await new VatPaymentService(db, tenant, new UnexpectedAudit())
            .RecordAsync(19m, VatPaymentKind.Payment, default, null);
        Assert.Equal(PaymentOperationStatus.Invalid, result.Status);
        Assert.Equal("valueDate", result.ErrorKey);
    }

    private sealed class UnexpectedAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent auditEvent, CancellationToken ct = default) =>
            throw new InvalidOperationException("Invalid input must not produce an audit event.");
    }
}
