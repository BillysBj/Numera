using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Reporting;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Tax;
using Numera.Modules.Sales;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real PostgreSQL cash recognition, append-only settlement and RLS regression coverage.</summary>
[Collection(PostgresCollection.Name)]
public sealed class VatPaymentTests(PostgresFixture fixture)
{
    private static readonly DateOnly ValueDate = new(2026, 2, 10);

    [Theory]
    [InlineData(VatPaymentKind.Payment, 19)]
    [InlineData(VatPaymentKind.Refund, -19)]
    public async Task Settlement_recognizes_signed_line_58_only_on_value_date(VatPaymentKind kind, decimal expected)
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant);
        var result = await Service(db, tenant).RecordAsync(19m, kind, ValueDate, "  USt 02/2026  ");
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        Assert.Equal("USt 02/2026", (await db.Set<VatPayment>().SingleAsync()).Reference);
        var reader = new RecognitionReader(db);
        Assert.Equal(expected, await reader.ReadVatFinanzamtRecognitionAsync(ValueDate, ValueDate, default));
        Assert.Equal(0m, await reader.ReadVatFinanzamtRecognitionAsync(ValueDate.AddDays(-1), ValueDate.AddDays(-1), default));
        Assert.Equal(0m, await reader.ReadVatFinanzamtRecognitionAsync(ValueDate.AddDays(1), ValueDate.AddDays(1), default));
        var calculator = new EuerCalculator(db, reader);
        var report = await calculator.ComputeAsync(2026, ValueDate, ValueDate, default);
        Assert.Equal(expected, Assert.Single(report.Betriebsausgaben, line => line.Zeile == "58").Betrag);
        Assert.Equal(expected, report.SummeAusgaben);
        Assert.Equal(-expected, report.Gewinn);
        Assert.Equal(0m, (await calculator.ComputeAsync(2026, ValueDate.AddDays(1), ValueDate.AddDays(1), default)).SummeAusgaben);
        Assert.Empty(await db.Set<JournalEntry>().ToListAsync());
        Assert.Empty(await db.Set<Posting>().ToListAsync());
        var audit = Assert.Single(await db.Set<AuditEvent>().ToListAsync());
        Assert.Equal("vat_payment.recorded", audit.Action);
        Assert.Equal(result.PaymentId, audit.EntityId);
    }

    [Fact]
    public async Task Kleinunternehmer_excludes_line_58_even_if_settlements_exist()
    {
        var tenant = await SetupAsync(smallBusiness: true);
        await using var db = fixture.CreateAppContext(tenant);
        await Service(db, tenant).RecordAsync(19m, VatPaymentKind.Payment, ValueDate, null);
        var report = await new EuerCalculator(db, new RecognitionReader(db)).ComputeAsync(2026, ValueDate, ValueDate, default);
        Assert.DoesNotContain(report.Betriebsausgaben, line => line.Zeile is "57" or "58");
        Assert.Equal(0m, report.SummeAusgaben);
        Assert.Equal(0m, report.Gewinn);
    }

    [Fact]
    public async Task Line_rounding_happens_after_signed_aggregation()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant);
        var service = Service(db, tenant);
        await service.RecordAsync(1.004m, VatPaymentKind.Payment, ValueDate, null);
        await service.RecordAsync(1.004m, VatPaymentKind.Payment, ValueDate, null);
        await service.RecordAsync(0.001m, VatPaymentKind.Refund, ValueDate, null);
        var report = await new EuerCalculator(db, new RecognitionReader(db)).ComputeAsync(2026, ValueDate, ValueDate, default);
        Assert.Equal(2.01m, Assert.Single(report.Betriebsausgaben, line => line.Zeile == "58").Betrag);
        Assert.Equal(2.01m, report.SummeAusgaben);
        Assert.Equal(-2.01m, report.Gewinn);
    }

    [Theory]
    [InlineData(VatPaymentKind.Payment, VatPaymentKind.Refund, 19)]
    [InlineData(VatPaymentKind.Refund, VatPaymentKind.Payment, -19)]
    public async Task Reversal_nets_out_on_its_own_value_date_and_cannot_be_repeated(
        VatPaymentKind kind, VatPaymentKind reverseKind, decimal signed)
    {
        var tenant = await SetupAsync();
        var originalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        await using var db = fixture.CreateAppContext(tenant);
        var service = Service(db, tenant);
        var recorded = await service.RecordAsync(19m, kind, originalDate, null);
        var id = recorded.PaymentId!.Value;
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await service.ReverseAsync(id);
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        var reversal = await db.Set<VatPayment>().SingleAsync(payment => payment.Id == result.PaymentId);
        Assert.Equal(19m, reversal.Amount);
        Assert.Equal(reverseKind, reversal.Kind);
        Assert.Equal(id, reversal.ReversesPaymentId);
        Assert.InRange(reversal.ValueDate, before, DateOnly.FromDateTime(DateTime.UtcNow));
        var original = await db.Set<VatPayment>().SingleAsync(payment => payment.Id == id);
        Assert.Equal(kind, original.Kind);
        Assert.Equal(19m, original.Amount);
        var reader = new RecognitionReader(db);
        Assert.Equal(signed, await reader.ReadVatFinanzamtRecognitionAsync(originalDate, originalDate, default));
        Assert.Equal(-signed, await reader.ReadVatFinanzamtRecognitionAsync(reversal.ValueDate, reversal.ValueDate, default));
        var netted = await new EuerCalculator(db, reader).ComputeAsync(reversal.ValueDate.Year, originalDate, reversal.ValueDate, default);
        Assert.Equal(0m, Assert.Single(netted.Betriebsausgaben, line => line.Zeile == "58").Betrag);
        Assert.Equal(0m, netted.SummeAusgaben);
        Assert.Equal(0m, netted.Gewinn);
        Assert.Equal(PaymentOperationStatus.Conflict, (await service.ReverseAsync(id)).Status);
        Assert.Equal(PaymentOperationStatus.Conflict, (await service.ReverseAsync(reversal.Id)).Status);
        Assert.Equal(2, await db.Set<VatPayment>().CountAsync());
        var audit = await db.Set<AuditEvent>().SingleAsync(evt => evt.Action == "vat_payment.reversed");
        Assert.NotNull(audit.Before);
        Assert.NotNull(audit.After);
        Assert.Empty(await db.Set<JournalEntry>().ToListAsync());
    }

    [Fact]
    public async Task Concurrent_reversals_create_exactly_one_correction()
    {
        var tenant = await SetupAsync();
        Guid id;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            id = (await Service(db, tenant).RecordAsync(19m, VatPaymentKind.Payment, ValueDate, null)).PaymentId!.Value;
        }

        async Task<PaymentOperationResult> ReverseAsync()
        {
            await using var db = fixture.CreateAppContext(tenant);
            return await Service(db, tenant).ReverseAsync(id);
        }

        var results = await Task.WhenAll(ReverseAsync(), ReverseAsync());
        Assert.Single(results, result => result.Status == PaymentOperationStatus.Success);
        Assert.Single(results, result => result.Status == PaymentOperationStatus.Conflict);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(2, await read.Set<VatPayment>().CountAsync());
    }

    [Fact]
    public async Task Rls_hides_foreign_settlements_and_rejects_forged_inserts()
    {
        var tenant = await SetupAsync();
        await using var owner = fixture.CreateAppContext(tenant);
        var id = (await Service(owner, tenant).RecordAsync(19m, VatPaymentKind.Payment, ValueDate, null)).PaymentId!.Value;
        var other = Guid.CreateVersion7();
        await using var foreign = fixture.CreateAppContext(other);
        Assert.Empty(await foreign.Set<VatPayment>().IgnoreQueryFilters().ToListAsync());
        Assert.Equal(0m, await new RecognitionReader(foreign).ReadVatFinanzamtRecognitionAsync(ValueDate, ValueDate, default));
        Assert.Equal(PaymentOperationStatus.NotFound, (await Service(foreign, other).ReverseAsync(id)).Status);
        await Assert.ThrowsAsync<PostgresException>(() => foreign.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vat_payment SET amount = 99 WHERE id = {id}"));
        foreign.Add(new VatPayment
        {
            TenantId = tenant, Amount = 19m, Kind = VatPaymentKind.Payment,
            ValueDate = ValueDate, RecordedAt = DateTimeOffset.UtcNow,
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Equal(19m, (await owner.Set<VatPayment>().SingleAsync()).Amount);
    }

    [Fact]
    public async Task Owner_cannot_update_or_delete_append_only_settlements()
    {
        var tenant = await SetupAsync();
        await using var db = fixture.CreateAppContext(tenant);
        var id = (await Service(db, tenant).RecordAsync(19m, VatPaymentKind.Payment, ValueDate, null)).PaymentId!.Value;
        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vat_payment SET amount = 99 WHERE id = {id}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM vat_payment WHERE id = {id}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        Assert.Single(await db.Set<VatPayment>().ToListAsync());
    }

    private async Task<Guid> SetupAsync(bool smallBusiness = false)
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);
        db.Add(new LedgerSettings { TenantId = tenant, ChartVariant = ChartVariant.Skr03 });
        db.Add(new CompanyProfile
        {
            TenantId = tenant, LegalName = "VAT payment test", IsKleinunternehmer = smallBusiness,
            Address = new() { Street = "Test 1", PostalCode = "10115", City = "Berlin", CountryCode = "DE" },
        });
        await db.SaveChangesAsync();
        return tenant;
    }

    private static VatPaymentService Service(NumeraDbContext db, Guid id)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(id);
        return new(db, tenant, new AuditWriter(db, tenant, new Actor()));
    }

    private sealed class Actor : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
    }
}
