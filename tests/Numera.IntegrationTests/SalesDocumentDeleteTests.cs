using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Endpoints;
using Numera.Modules.Sales;
using Numera.Platform.Audit;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for the draft-only sales-document delete endpoint core.
/// Existing endpoint suites invoke internal handlers directly, so these tests exercise
/// the shipped handler while retaining the real RLS, FK and immutability triggers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesDocumentDeleteTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Delete_draft_returns_no_content_then_get_returns_not_found_and_records_audit()
    {
        var tenant = Guid.CreateVersion7();
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Schlussrechnung,
            partnerId: null,
            [new SalesTestData.LineSpec("Schlussposition", 1m, 100m, TaxCategory.S, 19m)],
            new DateOnly(2026, 8, 12));

        await using (var seed = fixture.CreateAppContext(tenant))
        {
            seed.Add(new SalesDocumentTaxBreakdown
            {
                TenantId = tenant,
                DocumentId = documentId,
                TaxCategory = TaxCategory.S,
                VatRatePercent = 19m,
                TaxableBase = 100m,
                TaxAmount = 19m,
            });
            seed.Add(new SalesDocumentPrepayment
            {
                TenantId = tenant,
                DocumentId = documentId,
                AbschlagDocumentId = Guid.CreateVersion7(),
                AbschlagNumber = "AR-2026-00001",
                AbschlagDate = new DateOnly(2026, 8, 1),
                NetAmount = 50m,
                VatAmount = 9.5m,
                GrossAmount = 59.5m,
            });
            await seed.SaveChangesAsync();
        }

        var deleteResult = await DeleteAsync(tenant, documentId);

        AssertStatus(deleteResult, StatusCodes.Status204NoContent);
        await using var read = fixture.CreateAppContext(tenant);
        AssertStatus(
            await SalesDocumentEndpoints.GetAsync(documentId, read, CancellationToken.None),
            StatusCodes.Status404NotFound);
        Assert.False(await read.Set<SalesDocumentLine>().AnyAsync(line => line.DocumentId == documentId));
        Assert.False(await read.Set<SalesDocumentTaxBreakdown>().AnyAsync(row => row.DocumentId == documentId));
        Assert.False(await read.Set<SalesDocumentPrepayment>().AnyAsync(row => row.DocumentId == documentId));

        var audit = await read.Set<AuditEvent>()
            .AsNoTracking()
            .SingleAsync(row =>
                row.Action == "sales_document.deleted"
                && row.EntityId == documentId);
        Assert.NotNull(audit.Before);
        Assert.Null(audit.After);
    }

    [Fact]
    public async Task Delete_finalized_document_returns_conflict_and_keeps_document()
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            new DateOnly(2026, 8, 12));
        await using (var finalize = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(finalize, documentId);
        }

        var deleteResult = await DeleteAsync(tenant, documentId);

        AssertStatus(deleteResult, StatusCodes.Status409Conflict);
        await using var read = fixture.CreateAppContext(tenant);
        Assert.True(await read.Set<SalesDocument>().AnyAsync(doc => doc.Id == documentId));
    }

    [Fact]
    public async Task Delete_another_tenants_draft_returns_not_found_and_keeps_document()
    {
        var ownerTenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            ownerTenant,
            DocumentType.Angebot,
            partnerId: null,
            [new SalesTestData.LineSpec("Angebot", 1m, 100m, TaxCategory.S, 19m)],
            new DateOnly(2026, 8, 12));

        var deleteResult = await DeleteAsync(otherTenant, documentId);

        AssertStatus(deleteResult, StatusCodes.Status404NotFound);
        await using var ownerRead = fixture.CreateAppContext(ownerTenant);
        Assert.True(await ownerRead.Set<SalesDocument>().AnyAsync(doc => doc.Id == documentId));
    }

    private async Task<IResult> DeleteAsync(Guid tenant, Guid documentId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var audit = new AuditWriter(db, currentTenant, new TestCurrentUser(Guid.CreateVersion7()));
        return await SalesDocumentEndpoints.DeleteDraftAsync(
            documentId, db, audit, CancellationToken.None);
    }

    private static void AssertStatus(IResult result, int expectedStatusCode)
    {
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(expectedStatusCode, status.StatusCode);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
