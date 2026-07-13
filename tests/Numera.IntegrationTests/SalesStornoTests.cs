using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The INV-03 + DOCS-01/04 hard gates: proves Storno + Gutschrift + copy-forward convert on
/// real postgres:18 as <c>numera_app</c>. A Storno (a faithful mirror of the production
/// endpoint flow) is asserted to take its OWN number from the Storno series, be a negative
/// mirror referencing the original, flip the original to Cancelled with a back-link while
/// leaving its OTHER business columns untouched (DB immutability), and close the original's
/// open item with no positive receivable created for the Storno. Convert copies a quote
/// forward into a fresh Draft invoice (the DOCS-01 chain), and a commercial credit note
/// finalizes to a Gutschrift-series number with no positive open item.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesStornoTests
{
    private readonly PostgresFixture _fixture;

    public SalesStornoTests(PostgresFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------ Storno

    [Fact]
    public async Task Storno_numbers_negates_cancels_the_original_and_closes_its_open_item()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Storno, "ST-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);

        var originalId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, originalId);
        }

        // Capture the original's pre-storno business snapshot to prove immutability later.
        string originalNumber;
        decimal originalGross;
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var pre = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == originalId);
            originalNumber = pre.DocumentNumber!;
            originalGross = pre.TotalGross;
            Assert.Equal(119.00m, originalGross);
        }

        var stornoId = await StornoAsync(tenant, originalId);

        await using var final = _fixture.CreateAppContext(tenant);

        // (a) the Storno has its OWN number from the Storno series, references the original,
        //     and is a negative mirror (negative gross).
        var storno = await final.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == stornoId);
        Assert.Equal(DocumentType.Storno, storno.DocumentType);
        Assert.Equal("ST-2026-00001", storno.DocumentNumber);
        Assert.NotEqual(originalNumber, storno.DocumentNumber);
        Assert.Equal(originalId, storno.CorrectsDocumentId);
        Assert.Equal(-119.00m, storno.TotalGross);
        Assert.Equal(-19.00m, storno.TotalTax);

        // (b) the original is Cancelled + back-linked; its OTHER business columns are UNCHANGED.
        var original = await final.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == originalId);
        Assert.Equal(DocumentStatus.Cancelled, original.Status);
        Assert.Equal(stornoId, original.CancelledByDocumentId);
        Assert.Equal(originalNumber, original.DocumentNumber);
        Assert.Equal(originalGross, original.TotalGross);
        Assert.Equal(date, original.DocumentDate);

        // (c) the original's open item is Cancelled with a zero open amount, and NO positive
        //     receivable was created for the Storno.
        var openItem = await final.Set<OpenItem>().AsNoTracking().FirstAsync(o => o.DocumentId == originalId);
        Assert.Equal(OpenItemStatus.Cancelled, openItem.Status);
        Assert.Equal(0m, openItem.OpenAmount);
        Assert.Empty(await final.Set<OpenItem>().AsNoTracking().Where(o => o.DocumentId == stornoId).ToListAsync());
    }

    // ------------------------------------------------------ copy-forward convert

    [Fact]
    public async Task Convert_copies_an_angebot_forward_into_a_fresh_draft_rechnung()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        var angebotId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Angebot, partnerId: Guid.CreateVersion7(),
            [
                new SalesTestData.LineSpec("Pos 1", 2m, 50m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Pos 2", 1m, 80m, TaxCategory.S, 7m),
            ],
            date);

        var rechnungId = await ConvertAsync(tenant, angebotId, DocumentType.Rechnung);

        await using var read = _fixture.CreateAppContext(tenant);
        var angebot = await read.Set<SalesDocument>().AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == angebotId);
        var rechnung = await read.Set<SalesDocument>().AsNoTracking().Include(d => d.Lines).FirstAsync(d => d.Id == rechnungId);

        Assert.Equal(DocumentType.Rechnung, rechnung.DocumentType);
        Assert.Equal(DocumentStatus.Draft, rechnung.Status);
        Assert.Null(rechnung.DocumentNumber);
        Assert.Equal(angebotId, rechnung.SourceDocumentId);

        // 2 lines copied forward with NEW ids but identical snapshot fields + order.
        Assert.Equal(2, rechnung.Lines.Count);
        var srcIds = angebot.Lines.Select(l => l.Id).ToHashSet();
        Assert.All(rechnung.Lines, l => Assert.DoesNotContain(l.Id, srcIds));
        foreach (var src in angebot.Lines.OrderBy(l => l.LineNumber))
        {
            var copy = rechnung.Lines.Single(l => l.LineNumber == src.LineNumber);
            Assert.Equal(src.Name, copy.Name);
            Assert.Equal(src.Quantity, copy.Quantity);
            Assert.Equal(src.NetUnitPrice, copy.NetUnitPrice);
            Assert.Equal(src.LineNetAmount, copy.LineNetAmount);
            Assert.Equal(src.TaxCategory, copy.TaxCategory);
            Assert.Equal(src.VatRatePercent, copy.VatRatePercent);
        }
    }

    // ------------------------------------------------------ credit note (light)

    [Fact]
    public async Task Credit_note_finalizes_to_a_gutschrift_number_with_no_positive_open_item()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Gutschrift, "GS-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);

        var originalId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, originalId);
        }

        // Create the commercial credit note draft (positive amounts, references the original).
        var creditNoteId = await CreditNoteDraftAsync(tenant, originalId);

        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var draft = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == creditNoteId);
            Assert.Equal(DocumentType.Gutschrift, draft.DocumentType);
            Assert.Equal(DocumentStatus.Draft, draft.Status);
            Assert.Equal(originalId, draft.CorrectsDocumentId);
        }

        // Finalize it: a Gutschrift-series number, and (not a Rechnung) NO positive receivable.
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, creditNoteId);
        }

        await using var final = _fixture.CreateAppContext(tenant);
        var creditNote = await final.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == creditNoteId);
        Assert.Equal("GS-2026-00001", creditNote.DocumentNumber);
        Assert.Equal(DocumentStatus.Finalized, creditNote.Status);
        Assert.True(creditNote.TotalGross > 0m);
        Assert.Empty(await final.Set<OpenItem>().AsNoTracking().Where(o => o.DocumentId == creditNoteId).ToListAsync());
    }

    // ------------------------------------------------------------- operations

    /// <summary>
    /// Cancels a finalized invoice via a Storno — a faithful mirror of the production
    /// <c>/storno</c> endpoint: build a negative-mirror Draft Storno, finalize it (own series
    /// number, no open item), then in the SAME transaction mutate the original with ONLY the
    /// whitelisted lifecycle columns and close its open item. Returns the Storno id.
    /// </summary>
    private async Task<Guid> StornoAsync(Guid tenant, Guid originalId)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var original = await db.Set<SalesDocument>().Include(x => x.Lines).FirstAsync(x => x.Id == originalId);
        var profile = await db.Set<CompanyProfile>().FirstAsync();
        var partner = original.PartnerId is null
            ? null
            : await db.Set<BusinessPartner>().FirstOrDefaultAsync(p => p.Id == original.PartnerId);

        var storno = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Storno,
            Status = DocumentStatus.Draft,
            CorrectsDocumentId = original.Id,
            PartnerId = original.PartnerId,
            DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ServiceDate = original.ServiceDate,
            Currency = original.Currency,
        };
        foreach (var l in original.Lines.OrderBy(l => l.LineNumber))
        {
            storno.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = storno.Id,
                LineNumber = l.LineNumber,
                Name = l.Name,
                Quantity = -l.Quantity,
                UnitCode = l.UnitCode,
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = -l.LineNetAmount,
                TaxCategory = l.TaxCategory,
                VatRatePercent = l.VatRatePercent,
            });
        }

        db.Add(storno);

        await using var tx = await db.Database.BeginTransactionAsync();

        // Finalize the Storno inside the caller-owned transaction (own Storno-series number,
        // negative breakdown + totals; Storno is not a Rechnung → no open item).
        await SalesTestData.ApplyFinalizeAsync(db, storno, profile, partner);

        // Mutate the ORIGINAL using ONLY whitelisted lifecycle columns (the DB immutability
        // trigger permits status + cancelled_by_document_id).
        original.Status = DocumentStatus.Cancelled;
        original.CancelledByDocumentId = storno.Id;

        var openItem = await db.Set<OpenItem>().FirstOrDefaultAsync(o => o.DocumentId == original.Id);
        if (openItem is not null)
        {
            openItem.Status = OpenItemStatus.Cancelled;
            openItem.OpenAmount = 0m;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return storno.Id;
    }

    /// <summary>Copy-forward convert (DOCS-01): a NEW Draft of the target type from the source.</summary>
    private async Task<Guid> ConvertAsync(Guid tenant, Guid sourceId, DocumentType target)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var source = await db.Set<SalesDocument>().AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == sourceId);

        var doc = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = target,
            Status = DocumentStatus.Draft,
            SourceDocumentId = source.Id,
            PartnerId = source.PartnerId,
            DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Currency = source.Currency,
        };
        foreach (var l in source.Lines.OrderBy(l => l.LineNumber))
        {
            doc.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = doc.Id,
                LineNumber = l.LineNumber,
                CatalogItemId = l.CatalogItemId,
                Name = l.Name,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitCode = l.UnitCode,
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = l.LineNetAmount,
                TaxCategory = l.TaxCategory,
                VatRatePercent = l.VatRatePercent,
            });
        }

        doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);
        db.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>Creates a commercial Gutschrift draft (positive amounts) referencing an issued invoice.</summary>
    private async Task<Guid> CreditNoteDraftAsync(Guid tenant, Guid originalId)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var original = await db.Set<SalesDocument>().AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == originalId);

        var creditNote = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Gutschrift,
            Status = DocumentStatus.Draft,
            CorrectsDocumentId = original.Id,
            PartnerId = original.PartnerId,
            DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Currency = original.Currency,
        };
        foreach (var l in original.Lines.OrderBy(l => l.LineNumber))
        {
            creditNote.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = creditNote.Id,
                LineNumber = l.LineNumber,
                Name = l.Name,
                Quantity = l.Quantity,
                UnitCode = l.UnitCode,
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = l.LineNetAmount,
                TaxCategory = l.TaxCategory,
                VatRatePercent = l.VatRatePercent,
            });
        }

        creditNote.TotalNet = creditNote.Lines.Sum(l => l.LineNetAmount);
        db.Add(creditNote);
        await db.SaveChangesAsync();
        return creditNote.Id;
    }
}
