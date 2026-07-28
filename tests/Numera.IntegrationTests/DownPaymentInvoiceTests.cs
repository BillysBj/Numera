using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>INV-07 integration coverage against PostgreSQL 18 with FORCE RLS.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DownPaymentInvoiceTests
{
    private readonly PostgresFixture _fixture;

    public DownPaymentInvoiceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Abschlag_and_Schluss_use_own_series_and_residual_receivable_with_full_vat()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 7, 28);
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);

        var abschlagId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Abschlagsrechnung, partner.Id,
            [new("Abschlag", 1m, 100m, TaxCategory.S, 19m)], date);
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, abschlagId);
        }

        var schlussId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Schlussrechnung, partner.Id,
            [new("Gesamtprojekt", 1m, 300m, TaxCategory.S, 19m)], date);
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            db.Add(new SalesDocumentPrepayment
            {
                TenantId = tenant,
                DocumentId = schlussId,
                AbschlagDocumentId = abschlagId,
                AbschlagNumber = "AR-2026-00001",
                AbschlagDate = date,
                NetAmount = 100m,
                VatAmount = RoundingPolicy.RoundTax(100m, 19m),
                GrossAmount = 119m,
            });
            await db.SaveChangesAsync();
            await SalesTestData.FinalizeAsync(db, schlussId);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var abschlag = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == abschlagId);
        var abschlagOpen = await read.Set<OpenItem>().AsNoTracking().SingleAsync(x => x.DocumentId == abschlagId);
        Assert.Equal("AR-2026-00001", abschlag.DocumentNumber);
        Assert.Equal(119m, abschlag.TotalGross);
        Assert.Equal(abschlag.TotalGross, abschlagOpen.OriginalAmount);

        var schluss = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == schlussId);
        var schlussOpen = await read.Set<OpenItem>().AsNoTracking().SingleAsync(x => x.DocumentId == schlussId);
        var tax = await read.Set<SalesDocumentTaxBreakdown>().AsNoTracking()
            .Where(x => x.DocumentId == schlussId).SumAsync(x => x.TaxAmount);
        Assert.Equal("SR-2026-00001", schluss.DocumentNumber);
        Assert.Equal(357m, schluss.TotalGross);
        Assert.Equal(57m, schluss.TotalTax);
        Assert.Equal(schluss.TotalTax, tax);
        Assert.Equal(238m, schluss.AmountDue);
        Assert.NotEqual(schluss.TotalGross, schluss.AmountDue);
        Assert.Equal(schluss.AmountDue, schlussOpen.OriginalAmount);
        Assert.Equal(schluss.AmountDue, schlussOpen.OpenAmount);
    }

    [Fact]
    public async Task Over_deduction_rolls_back_number_and_open_item()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 7, 28);
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var id = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Schlussrechnung, partner.Id,
            [new("Projekt", 1m, 100m, TaxCategory.S, 19m)], date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            db.Add(new SalesDocumentPrepayment
            {
                TenantId = tenant,
                DocumentId = id,
                AbschlagDocumentId = Guid.CreateVersion7(),
                AbschlagNumber = "AR-2026-99999",
                AbschlagDate = date,
                NetAmount = 120m,
                VatAmount = RoundingPolicy.RoundTax(120m, 19m),
                GrossAmount = 142.80m,
            });
            await db.SaveChangesAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SalesTestData.FinalizeAsync(db, id));
            Assert.Contains("must be between zero and document gross", ex.Message);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var doc = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Null(doc.DocumentNumber);
        Assert.Empty(await read.Set<OpenItem>().AsNoTracking().Where(x => x.DocumentId == id).ToListAsync());
    }

    [Theory]
    [InlineData(DocumentType.Abschlagsrechnung)]
    [InlineData(DocumentType.Schlussrechnung)]
    public async Task Finalized_down_payment_invoice_can_be_stornoed(
        DocumentType type)
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 7, 28);
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var originalId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, type, partner.Id,
            [new("Leistung", 1m, 100m, TaxCategory.S, 19m)], date);
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, originalId);
        }

        var stornoId = await StornoAsync(tenant, originalId);

        await using var read = _fixture.CreateAppContext(tenant);
        var storno = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == stornoId);
        var original = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == originalId);
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(x => x.DocumentId == originalId);
        Assert.Equal(DocumentType.Storno, storno.DocumentType);
        Assert.Equal("ST-2026-00001", storno.DocumentNumber);
        Assert.Equal(-119m, storno.TotalGross);
        Assert.Equal(originalId, storno.CorrectsDocumentId);
        Assert.Equal(DocumentStatus.Cancelled, original.Status);
        Assert.Equal(stornoId, original.CancelledByDocumentId);
        Assert.Equal(OpenItemStatus.Cancelled, openItem.Status);
        Assert.Equal(0m, openItem.OpenAmount);
    }

    [Fact]
    public async Task Prepayments_are_hidden_from_other_tenants_by_rls()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var id = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Schlussrechnung, Guid.CreateVersion7(),
            [new("Projekt", 1m, 100m, TaxCategory.S, 19m)], new DateOnly(2026, 7, 28));
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            db.Add(new SalesDocumentPrepayment
            {
                TenantId = tenant,
                DocumentId = id,
                AbschlagDocumentId = Guid.CreateVersion7(),
                AbschlagNumber = "AR-2026-00001",
                AbschlagDate = new DateOnly(2026, 7, 1),
                NetAmount = 10m,
                VatAmount = RoundingPolicy.RoundTax(10m, 19m),
                GrossAmount = 11.90m,
            });
            await db.SaveChangesAsync();
        }

        await using var other = _fixture.CreateAppContext(otherTenant);
        Assert.Empty(await other.Set<SalesDocumentPrepayment>().AsNoTracking().ToListAsync());
    }

    private async Task<Guid> StornoAsync(Guid tenant, Guid originalId)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var original = await db.Set<SalesDocument>().Include(x => x.Lines).SingleAsync(x => x.Id == originalId);
        var profile = await db.Set<CompanyProfile>().SingleAsync();
        var partner = await db.Set<BusinessPartner>().SingleAsync(x => x.Id == original.PartnerId);
        var storno = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Storno,
            Status = DocumentStatus.Draft,
            CorrectsDocumentId = original.Id,
            PartnerId = original.PartnerId,
            DocumentDate = original.DocumentDate,
            Currency = original.Currency,
        };
        foreach (var line in original.Lines)
        {
            storno.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = storno.Id,
                LineNumber = line.LineNumber,
                Name = line.Name,
                Quantity = -line.Quantity,
                UnitCode = line.UnitCode,
                NetUnitPrice = line.NetUnitPrice,
                LineNetAmount = -line.LineNetAmount,
                TaxCategory = line.TaxCategory,
                VatRatePercent = line.VatRatePercent,
            });
        }

        db.Add(storno);
        await using var tx = await db.Database.BeginTransactionAsync();
        await SalesTestData.ApplyFinalizeAsync(db, storno, profile, partner);
        original.Status = DocumentStatus.Cancelled;
        original.CancelledByDocumentId = storno.Id;
        var openItem = await db.Set<OpenItem>().SingleAsync(x => x.DocumentId == original.Id);
        openItem.Status = OpenItemStatus.Cancelled;
        openItem.OpenAmount = 0m;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return storno.Id;
    }
}
