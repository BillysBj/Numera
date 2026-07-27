using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Real-Postgres proof of payment settlement, validation, append-only enforcement,
/// reversal and tenant isolation while running as the non-BYPASSRLS application role.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PaymentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Partial_payment_reduces_open_amount_and_keeps_document_finalized()
    {
        var (tenant, documentId, openItemId, originalAmount) = await CreateInvoiceAsync();
        var paid = 40m;

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await CreateService(db, tenant).RecordAsync(Request(openItemId, paid));
            Assert.Equal(PaymentOperationStatus.Success, result.Status);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(o => o.Id == openItemId);
        var document = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(d => d.Id == documentId);
        var payment = await read.Set<Payment>().AsNoTracking().SingleAsync();
        var allocation = await read.Set<PaymentAllocation>().AsNoTracking().SingleAsync();

        Assert.Equal(originalAmount - paid, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.PartiallyPaid, openItem.Status);
        Assert.Equal(originalAmount - paid, document.AmountDue);
        Assert.Equal(DocumentStatus.Finalized, document.Status);
        Assert.Equal(paid, payment.Amount);
        Assert.Equal(openItemId, allocation.OpenItemId);
        Assert.Equal(paid, allocation.AllocatedAmount);
    }

    [Fact]
    public async Task Full_payment_marks_open_item_and_document_paid()
    {
        var (tenant, documentId, openItemId, originalAmount) = await CreateInvoiceAsync();

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await CreateService(db, tenant).RecordAsync(Request(openItemId, originalAmount));
            Assert.Equal(PaymentOperationStatus.Success, result.Status);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(o => o.Id == openItemId);
        var document = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(d => d.Id == documentId);
        Assert.Equal(0m, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Paid, openItem.Status);
        Assert.Equal(0m, document.AmountDue);
        Assert.Equal(DocumentStatus.Paid, document.Status);
    }

    [Fact]
    public async Task One_payment_can_partially_allocate_across_multiple_open_items()
    {
        var first = await CreateInvoiceAsync();
        var second = await CreateInvoiceAsync(first.Tenant);
        const decimal firstPart = 20m;
        const decimal secondPart = 30m;

        await using (var db = fixture.CreateAppContext(first.Tenant))
        {
            var result = await CreateService(db, first.Tenant).RecordAsync(new RecordPaymentRequest(
                firstPart + secondPart,
                new DateOnly(2026, 7, 27),
                PaymentMethod.BankTransfer,
                "MULTI",
                [
                    new PaymentAllocationInput(first.OpenItemId, firstPart),
                    new PaymentAllocationInput(second.OpenItemId, secondPart),
                ]));
            Assert.Equal(PaymentOperationStatus.Success, result.Status);
        }

        await using var read = fixture.CreateAppContext(first.Tenant);
        var items = await read.Set<OpenItem>()
            .AsNoTracking()
            .Where(o => o.Id == first.OpenItemId || o.Id == second.OpenItemId)
            .ToDictionaryAsync(o => o.Id);
        Assert.Equal(first.OriginalAmount - firstPart, items[first.OpenItemId].OpenAmount);
        Assert.Equal(second.OriginalAmount - secondPart, items[second.OpenItemId].OpenAmount);
        Assert.Equal(2, await read.Set<PaymentAllocation>().CountAsync());
    }

    [Fact]
    public async Task Over_allocation_is_rejected_without_writing_payment()
    {
        var (tenant, _, openItemId, originalAmount) = await CreateInvoiceAsync();

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await CreateService(db, tenant).RecordAsync(Request(openItemId, originalAmount + 0.01m));
            Assert.Equal(PaymentOperationStatus.Invalid, result.Status);
        }

        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(0, await read.Set<Payment>().CountAsync());
        Assert.Equal(0, await read.Set<PaymentAllocation>().CountAsync());
        Assert.Equal(originalAmount, (await read.Set<OpenItem>().SingleAsync(o => o.Id == openItemId)).OpenAmount);
    }

    [Fact]
    public async Task Database_trigger_blocks_payment_update_and_delete()
    {
        var (tenant, _, openItemId, _) = await CreateInvoiceAsync();
        Guid paymentId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await CreateService(db, tenant).RecordAsync(Request(openItemId, 10m));
            paymentId = Assert.IsType<Guid>(result.PaymentId);
        }

        await using (var update = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                update.Database.ExecuteSqlInterpolatedAsync($"UPDATE payment SET amount = 11 WHERE id = {paymentId}"));
            Assert.Contains("append-only (GoBD)", exception.MessageText);
        }

        await using (var delete = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                delete.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM payment WHERE id = {paymentId}"));
            Assert.Contains("append-only (GoBD)", exception.MessageText);
        }
    }

    [Fact]
    public async Task Reversal_appends_negative_rows_and_restores_receivable()
    {
        var (tenant, documentId, openItemId, originalAmount) = await CreateInvoiceAsync();
        Guid originalPaymentId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var recorded = await CreateService(db, tenant).RecordAsync(Request(openItemId, originalAmount));
            originalPaymentId = Assert.IsType<Guid>(recorded.PaymentId);
        }

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var reversed = await CreateService(db, tenant).ReverseAsync(originalPaymentId);
            Assert.Equal(PaymentOperationStatus.Success, reversed.Status);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var payments = await read.Set<Payment>().AsNoTracking().OrderBy(p => p.RecordedAt).ToListAsync();
        var allocations = await read.Set<PaymentAllocation>().AsNoTracking().ToListAsync();
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(o => o.Id == openItemId);
        var document = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(d => d.Id == documentId);

        Assert.Equal(2, payments.Count);
        Assert.Equal(originalAmount, payments.Single(p => p.Id == originalPaymentId).Amount);
        var reversal = Assert.Single(payments, p => p.ReversesPaymentId == originalPaymentId);
        Assert.Equal(-originalAmount, reversal.Amount);
        Assert.Contains(allocations, a => a.PaymentId == reversal.Id && a.AllocatedAmount == -originalAmount);
        Assert.Equal(originalAmount, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, openItem.Status);
        Assert.Equal(originalAmount, document.AmountDue);
        Assert.Equal(DocumentStatus.Finalized, document.Status);
    }

    [Fact]
    public async Task Payments_are_hidden_from_another_tenant_by_rls()
    {
        var (tenant, _, openItemId, _) = await CreateInvoiceAsync();
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await CreateService(db, tenant).RecordAsync(Request(openItemId, 10m));
        }

        await using var other = fixture.CreateAppContext(Guid.CreateVersion7());
        Assert.Equal(0, await other.Set<Payment>().CountAsync());
        Assert.Equal(0, await other.Set<PaymentAllocation>().CountAsync());
    }

    private async Task<(Guid Tenant, Guid DocumentId, Guid OpenItemId, decimal OriginalAmount)>
        CreateInvoiceAsync(Guid? existingTenant = null)
    {
        var tenant = existingTenant ?? Guid.CreateVersion7();
        if (existingTenant is null)
        {
            await SalesTestData.SeedProfileAsync(fixture, tenant);
        }

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
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(o => o.DocumentId == documentId);
        return (tenant, documentId, openItem.Id, openItem.OriginalAmount);
    }

    private static RecordPaymentRequest Request(Guid openItemId, decimal amount) =>
        new(
            amount,
            new DateOnly(2026, 7, 27),
            PaymentMethod.BankTransfer,
            "TEST",
            [new PaymentAllocationInput(openItemId, amount)]);

    private static PaymentService CreateService(Numera.Platform.Db.NumeraDbContext db, Guid tenant)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        return new PaymentService(db, tenantContext, new NoOpAuditWriter());
    }
}
