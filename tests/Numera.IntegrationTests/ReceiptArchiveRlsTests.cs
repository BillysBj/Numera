using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Sales.Belege;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof for receipt RLS, archive WORM storage, and dedup verdicts.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReceiptArchiveRlsTests(PostgresFixture fixture)
{
    private static readonly DateOnly InvoiceDate = new(2026, 8, 4);

    [Fact]
    public async Task Rls_hides_another_tenants_receipt_and_archive()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var receipt = NewReceipt(tenantB, "tenant-b-hash");
        var archive = NewArchive(tenantB, receipt.Id, "tenant-b-hash", [1, 2, 3]);
        await SeedAsync(tenantB, receipt, archive);

        await using var readA = fixture.CreateAppContext(tenantA);
        Assert.Equal(0, await readA.Set<Receipt>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await readA.Set<ReceiptArchive>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task With_check_rejects_cross_tenant_insert()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var db = fixture.CreateAppContext(tenantA);
        db.Add(NewReceipt(tenantB, "cross-tenant-hash"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Archive_trigger_blocks_bytes_update_and_delete()
    {
        var tenant = Guid.CreateVersion7();
        var receipt = NewReceipt(tenant, "immutable-hash");
        var archive = NewArchive(tenant, receipt.Id, "immutable-hash", [1, 2, 3]);
        await SeedAsync(tenant, receipt, archive);

        await using (var update = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                update.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE receipt_archive SET original_bytes = {new byte[] { 4, 5, 6 }} WHERE id = {archive.Id}"));
            Assert.Contains("append-only (GoBD)", exception.MessageText);
        }

        await using (var delete = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                delete.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM receipt_archive WHERE id = {archive.Id}"));
            Assert.Contains("append-only (GoBD)", exception.MessageText);
        }
    }

    [Fact]
    public async Task Dedup_returns_exact_then_suspected_business_key()
    {
        var tenant = Guid.CreateVersion7();
        var supplier = Guid.CreateVersion7();
        const string invoiceNumber = "RE-2026-0804";
        const decimal gross = 119m;
        var existing = NewReceipt(tenant, "existing-content-hash");
        existing.MatchedPartnerId = supplier;
        existing.InvoiceNumber = invoiceNumber;
        existing.GrossAmount = gross;
        existing.InvoiceDate = InvoiceDate;
        await SeedAsync(tenant, existing);

        await using var db = fixture.CreateAppContext(tenant);
        var deduplicator = new ReceiptDeduplicator();

        var exact = await deduplicator.CheckAsync(
            db,
            existing.ContentHash,
            null,
            null,
            null,
            null,
            CancellationToken.None);
        Assert.Equal(DuplicateVerdict.Exact, exact);

        var suspected = await deduplicator.CheckAsync(
            db,
            "different-content-hash",
            supplier,
            invoiceNumber,
            gross,
            InvoiceDate,
            CancellationToken.None);
        Assert.Equal(DuplicateVerdict.SuspectedBusinessKey, suspected);
    }

    private async Task SeedAsync(Guid tenant, params object[] rows)
    {
        await using var db = fixture.CreateAppContext(tenant);
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static Receipt NewReceipt(Guid tenant, string contentHash) => new()
    {
        TenantId = tenant,
        Source = ReceiptSource.Upload,
        Status = ReceiptStatus.Captured,
        ContentHash = contentHash,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ReceiptArchive NewArchive(
        Guid tenant,
        Guid receiptId,
        string contentHash,
        byte[] bytes) => new()
        {
            TenantId = tenant,
            ReceiptId = receiptId,
            OriginalBytes = bytes,
            OriginalFileName = "receipt.pdf",
            ContentType = "application/pdf",
            ByteSize = bytes.LongLength,
            ContentHash = contentHash,
            Source = ReceiptSource.Upload,
            ReceivedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = Guid.CreateVersion7(),
        };
}
