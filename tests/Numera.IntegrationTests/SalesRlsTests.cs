using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The CI-gating hard gate for the polymorphic sales schema (DOCS-04). Proves, on a real
/// postgres:18 as the non-BYPASSRLS <c>numera_app</c> role, two irreversible seams:
/// <list type="bullet">
///   <item>every one of the 6 new sales tables (<c>sales_documents</c>,
///   <c>sales_document_lines</c>, <c>sales_document_tax_breakdown</c>,
///   <c>number_sequences</c>, <c>document_number_formats</c>, <c>open_items</c>) is
///   tenant-isolated by a hand-written RLS policy — reflective discovery never creates
///   one; and</item>
///   <item>GoBD immutability is DB-enforced: a finalized document's business columns
///   cannot be UPDATEd, it cannot be DELETEd, and lines cannot be appended to it, while
///   a Draft stays fully mutable.</item>
/// </list>
/// </summary>
/// <remarks>
/// Isolation queries use <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// so the app-level EF filters are OFF and RLS is the SOLE control under test (mirrors
/// <see cref="StammdatenRlsTests"/>). Immutability is exercised with raw SQL / EF writes
/// as <c>numera_app</c> so the trigger fires regardless of the client (mirrors
/// <see cref="AuditImmutabilityTests"/>).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SalesRlsTests
{
    private readonly PostgresFixture _fixture;

    public SalesRlsTests(PostgresFixture fixture) => _fixture = fixture;

    // ==================================================================== RLS
    // ---------------------------------------------------------- sales_documents

    [Fact]
    public async Task SalesDocuments_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedDocumentAsync(tenantA);
        await SeedDocumentAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<SalesDocument>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, d => Assert.Equal(tenantA, d.TenantId));
        Assert.Equal(0, await contextA.Set<SalesDocument>().IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == tenantB));
    }

    [Fact]
    public async Task SalesDocuments_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<SalesDocument>().Add(NewDocument(tenantB));

        await AssertRlsRejectsAsync(contextA);
    }

    // ------------------------------------------------------ sales_document_lines

    [Fact]
    public async Task SalesDocumentLines_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedLineAsync(tenantA);
        await SeedLineAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<SalesDocumentLine>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, l => Assert.Equal(tenantA, l.TenantId));
        Assert.Equal(0, await contextA.Set<SalesDocumentLine>().IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == tenantB));
    }

    [Fact]
    public async Task SalesDocumentLines_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        // A Draft parent owned by tenant A so the child immutability trigger passes
        // (parent status 0) and RLS WITH CHECK is the control actually under test.
        var parentA = await SeedDocumentAsync(tenantA);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<SalesDocumentLine>().Add(NewLine(tenantB, parentA));

        await AssertRlsRejectsAsync(contextA);
    }

    // ---------------------------------------------- sales_document_tax_breakdown

    [Fact]
    public async Task SalesDocumentTaxBreakdown_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedBreakdownAsync(tenantA);
        await SeedBreakdownAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<SalesDocumentTaxBreakdown>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, b => Assert.Equal(tenantA, b.TenantId));
        Assert.Equal(0, await contextA.Set<SalesDocumentTaxBreakdown>().IgnoreQueryFilters()
            .CountAsync(b => b.TenantId == tenantB));
    }

    [Fact]
    public async Task SalesDocumentTaxBreakdown_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var parentA = await SeedDocumentAsync(tenantA);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<SalesDocumentTaxBreakdown>().Add(NewBreakdown(tenantB, parentA));

        await AssertRlsRejectsAsync(contextA);
    }

    // --------------------------------------------------------- number_sequences

    [Fact]
    public async Task NumberSequences_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedNumberSequenceAsync(tenantA);
        await SeedNumberSequenceAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<NumberSequence>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, n => Assert.Equal(tenantA, n.TenantId));
        Assert.Equal(0, await contextA.Set<NumberSequence>().IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == tenantB));
    }

    [Fact]
    public async Task NumberSequences_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<NumberSequence>().Add(new NumberSequence
        {
            TenantId = tenantB,
            DocType = (int)DocumentType.Rechnung,
            Year = 2026,
            NextValue = 1,
        });

        await AssertRlsRejectsAsync(contextA);
    }

    // -------------------------------------------------- document_number_formats

    [Fact]
    public async Task DocumentNumberFormats_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedNumberFormatAsync(tenantA);
        await SeedNumberFormatAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<DocumentNumberFormat>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, f => Assert.Equal(tenantA, f.TenantId));
        Assert.Equal(0, await contextA.Set<DocumentNumberFormat>().IgnoreQueryFilters()
            .CountAsync(f => f.TenantId == tenantB));
    }

    [Fact]
    public async Task DocumentNumberFormats_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<DocumentNumberFormat>().Add(new DocumentNumberFormat
        {
            TenantId = tenantB,
            DocType = (int)DocumentType.Rechnung,
            Prefix = "RE-",
            IncludeYear = true,
            Padding = 5,
        });

        await AssertRlsRejectsAsync(contextA);
    }

    // ---------------------------------------------------------------- open_items

    [Fact]
    public async Task OpenItems_tenant_A_sees_only_its_own_rows_and_zero_tenant_B_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedOpenItemAsync(tenantA);
        await SeedOpenItemAsync(tenantB);

        await using var contextA = _fixture.CreateAppContext(tenantA);
        var rows = await contextA.Set<OpenItem>().IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, o => Assert.Equal(tenantA, o.TenantId));
        Assert.Equal(0, await contextA.Set<OpenItem>().IgnoreQueryFilters()
            .CountAsync(o => o.TenantId == tenantB));
    }

    [Fact]
    public async Task OpenItems_cross_tenant_insert_is_rejected_by_the_WITH_CHECK_policy()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using var contextA = _fixture.CreateAppContext(tenantA);
        contextA.Set<OpenItem>().Add(NewOpenItem(tenantB, Guid.CreateVersion7()));

        await AssertRlsRejectsAsync(contextA);
    }

    // ------------------------------------------------------------- fail-closed
    [Fact]
    public async Task Reading_without_a_tenant_context_never_leaks_any_sales_table()
    {
        var tenant = Guid.CreateVersion7();
        await SeedDocumentAsync(tenant);
        await SeedLineAsync(tenant);
        await SeedBreakdownAsync(tenant);
        await SeedNumberSequenceAsync(tenant);
        await SeedNumberFormatAsync(tenant);
        await SeedOpenItemAsync(tenant);

        try
        {
            await using var context = _fixture.CreateAppContext(tenantId: null);
            Assert.Empty(await context.Set<SalesDocument>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<SalesDocumentLine>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<SalesDocumentTaxBreakdown>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<NumberSequence>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<DocumentNumberFormat>().IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await context.Set<OpenItem>().IgnoreQueryFilters().ToListAsync());
        }
        catch (PostgresException)
        {
            // Fail-closed at the database (unset GUC errors the predicate) — equally safe.
        }
    }

    // =========================================================== DB IMMUTABILITY

    [Fact]
    public async Task Draft_document_and_its_lines_are_fully_mutable()
    {
        var tenant = Guid.CreateVersion7();
        var docId = await SeedDocumentAsync(tenant);

        // A line may be inserted while the parent is Draft (child trigger passes).
        await using (var ctx = _fixture.CreateAppContext(tenant))
        {
            ctx.Set<SalesDocumentLine>().Add(NewLine(tenant, docId));
            await ctx.SaveChangesAsync(); // must not throw
        }

        await using (var ctx = _fixture.CreateAppContext(tenant))
        {
            // A business column of a Draft may be UPDATEd.
            var updated = await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sales_documents SET total_gross = 42 WHERE id = {docId}");
            Assert.Equal(1, updated);
        }

        // A Draft with no lines may be hard-deleted.
        var deletableId = await SeedDocumentAsync(tenant);
        await using (var ctx = _fixture.CreateAppContext(tenant))
        {
            var deleted = await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM sales_documents WHERE id = {deletableId}");
            Assert.Equal(1, deleted);
        }
    }

    [Fact]
    public async Task Finalized_document_update_of_a_frozen_column_is_rejected()
    {
        var (tenant, docId) = await SeedFinalizedDocumentAsync();

        await using var ctx = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sales_documents SET total_gross = 999 WHERE id = {docId}"));

        AssertImmutable(ex);
    }

    [Fact]
    public async Task Finalized_document_update_of_document_date_is_rejected()
    {
        var (tenant, docId) = await SeedFinalizedDocumentAsync();

        await using var ctx = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sales_documents SET document_date = DATE '2000-01-01' WHERE id = {docId}"));

        AssertImmutable(ex);
    }

    [Fact]
    public async Task Finalized_document_delete_is_rejected()
    {
        var (tenant, docId) = await SeedFinalizedDocumentAsync();

        await using var ctx = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM sales_documents WHERE id = {docId}"));

        AssertImmutable(ex);
    }

    [Fact]
    public async Task Finalized_document_update_of_a_whitelisted_lifecycle_column_succeeds()
    {
        var (tenant, docId) = await SeedFinalizedDocumentAsync();

        await using var ctx = _fixture.CreateAppContext(tenant);
        // status Finalized(1) -> Sent(2) is a whitelisted lifecycle transition.
        var updated = await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales_documents SET status = 2, sent_at = now() WHERE id = {docId}");

        Assert.Equal(1, updated);
    }

    [Fact]
    public async Task Insert_of_a_line_into_a_finalized_parent_is_rejected()
    {
        var (tenant, docId) = await SeedFinalizedDocumentAsync();

        await using var ctx = _fixture.CreateAppContext(tenant);
        ctx.Set<SalesDocumentLine>().Add(NewLine(tenant, docId));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains(
            "immutable",
            ex.InnerException?.Message ?? ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- seed helpers

    private static SalesDocument NewDocument(Guid tenantId, DocumentStatus status = DocumentStatus.Draft) => new()
    {
        TenantId = tenantId,
        DocumentType = DocumentType.Rechnung,
        Status = status,
        DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Currency = "EUR",
    };

    private async Task<Guid> SeedDocumentAsync(Guid tenantId, DocumentStatus status = DocumentStatus.Draft)
    {
        var doc = NewDocument(tenantId, status);
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<SalesDocument>().Add(doc);
        await context.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>
    /// Seeds a Draft document, inserts a line while it is still Draft, then flips it to
    /// Finalized with a document number via raw SQL (allowed because OLD.status = 0).
    /// Returns the now-immutable document id.
    /// </summary>
    private async Task<(Guid Tenant, Guid DocId)> SeedFinalizedDocumentAsync()
    {
        var tenant = Guid.CreateVersion7();
        var docId = await SeedDocumentAsync(tenant);

        await using var context = _fixture.CreateAppContext(tenant);
        context.Set<SalesDocumentLine>().Add(NewLine(tenant, docId));
        await context.SaveChangesAsync();

        var number = $"RE-{Guid.NewGuid():N}"[..12];
        var flipped = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales_documents SET status = 1, document_number = {number}, finalized_at = now() WHERE id = {docId}");
        Assert.Equal(1, flipped);

        return (tenant, docId);
    }

    private static SalesDocumentLine NewLine(Guid tenantId, Guid documentId) => new()
    {
        TenantId = tenantId,
        DocumentId = documentId,
        LineNumber = 1,
        Name = "Beratungsleistung",
        Quantity = 1m,
        UnitCode = "C62",
        NetUnitPrice = 100m,
        LineNetAmount = 100m,
        TaxCategory = TaxCategory.S,
        VatRatePercent = 19m,
    };

    private async Task SeedLineAsync(Guid tenantId)
    {
        var docId = await SeedDocumentAsync(tenantId);
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<SalesDocumentLine>().Add(NewLine(tenantId, docId));
        await context.SaveChangesAsync();
    }

    private static SalesDocumentTaxBreakdown NewBreakdown(Guid tenantId, Guid documentId) => new()
    {
        TenantId = tenantId,
        DocumentId = documentId,
        TaxCategory = TaxCategory.S,
        VatRatePercent = 19m,
        TaxableBase = 100m,
        TaxAmount = 19m,
    };

    private async Task SeedBreakdownAsync(Guid tenantId)
    {
        var docId = await SeedDocumentAsync(tenantId);
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<SalesDocumentTaxBreakdown>().Add(NewBreakdown(tenantId, docId));
        await context.SaveChangesAsync();
    }

    private async Task SeedNumberSequenceAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<NumberSequence>().Add(new NumberSequence
        {
            TenantId = tenantId,
            DocType = (int)DocumentType.Rechnung,
            Year = 2026,
            NextValue = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedNumberFormatAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<DocumentNumberFormat>().Add(new DocumentNumberFormat
        {
            TenantId = tenantId,
            DocType = (int)DocumentType.Rechnung,
            Prefix = "RE-",
            IncludeYear = true,
            Padding = 5,
        });
        await context.SaveChangesAsync();
    }

    private static OpenItem NewOpenItem(Guid tenantId, Guid documentId) => new()
    {
        TenantId = tenantId,
        DocumentId = documentId,
        DocumentNumber = "RE-2026-00001",
        Currency = "EUR",
        OriginalAmount = 119m,
        OpenAmount = 119m,
        Status = OpenItemStatus.Open,
        IssuedOn = DateOnly.FromDateTime(DateTime.UtcNow),
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
    };

    private async Task SeedOpenItemAsync(Guid tenantId)
    {
        await using var context = _fixture.CreateAppContext(tenantId);
        context.Set<OpenItem>().Add(NewOpenItem(tenantId, Guid.CreateVersion7()));
        await context.SaveChangesAsync();
    }

    private static void AssertImmutable(PostgresException ex)
    {
        // P0001 = the raised trigger exception; the message carries "immutable".
        var byMessage = ex.MessageText.Contains("immutable", StringComparison.OrdinalIgnoreCase);
        var bySqlState = ex.SqlState == PostgresErrorCodes.RaiseException;
        Assert.True(byMessage || bySqlState, $"Unexpected error: {ex.SqlState} {ex.MessageText}");
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
