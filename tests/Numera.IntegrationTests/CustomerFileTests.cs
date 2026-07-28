using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Crm;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres coverage for Kundenakte bytea storage, RLS, and immutability.</summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomerFileTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Bytes_round_trip_and_list_projection_omits_bytes()
    {
        var tenant = Guid.CreateVersion7();
        var partnerId = Guid.CreateVersion7();
        var expected = new byte[] { 0, 1, 2, 127, 128, 254, 255 };
        var file = NewFile(tenant, partnerId, expected, "beleg.pdf", "application/pdf");
        await SeedAsync(file);

        await using var db = fixture.CreateAppContext(tenant);
        var metadataQuery = db.Set<CustomerFile>()
            .AsNoTracking()
            .Where(f => f.PartnerId == partnerId)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.ContentType,
                f.ByteSize,
                f.UploadedByUserId,
                f.UploadedAt,
            });

        Assert.DoesNotContain("\"bytes\"", metadataQuery.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        var metadata = Assert.Single(await metadataQuery.ToListAsync());
        Assert.Equal(file.Id, metadata.Id);
        Assert.Equal("beleg.pdf", metadata.FileName);
        Assert.Equal("application/pdf", metadata.ContentType);
        Assert.Equal(expected.LongLength, metadata.ByteSize);

        var download = await db.Set<CustomerFile>()
            .AsNoTracking()
            .SingleAsync(f => f.Id == file.Id && f.PartnerId == partnerId);
        Assert.Equal(expected, download.Bytes);
        Assert.Equal("application/pdf", download.ContentType);
        Assert.Equal("beleg.pdf", download.FileName);
    }

    [Fact]
    public async Task Database_trigger_blocks_update_and_delete()
    {
        var tenant = Guid.CreateVersion7();
        var file = NewFile(
            tenant,
            Guid.CreateVersion7(),
            [1, 2, 3],
            "notiz.txt",
            "text/plain");
        await SeedAsync(file);

        await using (var update = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                update.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE customer_files SET file_name = 'changed.txt' WHERE id = {file.Id}"));
            Assert.Contains("append-only (Kundenakte)", exception.MessageText);
        }

        await using (var delete = fixture.CreateAppContext(tenant))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                delete.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM customer_files WHERE id = {file.Id}"));
            Assert.Contains("append-only (Kundenakte)", exception.MessageText);
        }
    }

    [Fact]
    public async Task Rls_isolates_other_tenant_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedAsync(NewFile(tenantA, Guid.CreateVersion7(), [1], "a.txt", "text/plain"));

        await using var other = fixture.CreateAppContext(tenantB);
        Assert.Equal(0, await other.Set<CustomerFile>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Cross_tenant_insert_is_rejected_by_with_check()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenantA);
        db.Add(NewFile(tenantB, Guid.CreateVersion7(), [1], "b.txt", "text/plain"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(
            "row-level security",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedAsync(CustomerFile file)
    {
        await using var db = fixture.CreateAppContext(file.TenantId);
        db.Add(file);
        await db.SaveChangesAsync();
    }

    private static CustomerFile NewFile(
        Guid tenant,
        Guid partnerId,
        byte[] bytes,
        string fileName,
        string contentType) => new()
        {
            TenantId = tenant,
            PartnerId = partnerId,
            Bytes = bytes,
            FileName = fileName,
            ContentType = contentType,
            ByteSize = bytes.LongLength,
            UploadedByUserId = Guid.CreateVersion7(),
        };
}
