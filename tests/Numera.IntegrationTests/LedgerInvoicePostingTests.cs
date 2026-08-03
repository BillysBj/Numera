using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Modules.Crm;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof that invoice finalization and ledger posting are atomic.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerInvoicePostingTests(PostgresFixture fixture)
{
    private static readonly DateOnly DocumentDate = new(2026, 8, 3);

    [Fact]
    public async Task Finalizing_19_percent_invoice_creates_one_balanced_Skr03_entry()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var documentId = await SeedInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);

        await FinalizeAsync(tenant, documentId);

        await using var read = fixture.CreateAppContext(tenant);
        var entry = await InvoiceEntryAsync(read, documentId);
        var accountNumbers = await AccountNumbersAsync(read);

        Assert.Equal(PostingType.Normal, entry.PostingType);
        Assert.Null(entry.ReversesEntryId);
        AssertBalanced(entry.Postings);
        Assert.Equal(3, entry.Postings.Count);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Debit, 119m, null);
        AssertLeg(entry.Postings, accountNumbers, "8400", PostingDirection.Credit, 100m, Steuerschluessel.Ust19);
        AssertLeg(entry.Postings, accountNumbers, "1776", PostingDirection.Credit, 19m, null);
    }

    [Fact]
    public async Task Finalizing_split_invoice_books_one_revenue_and_tax_pair_per_breakdown()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var documentId = await SeedInvoiceAsync(
            tenant,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ]);

        await FinalizeAsync(tenant, documentId);

        await using var read = fixture.CreateAppContext(tenant);
        var entry = await InvoiceEntryAsync(read, documentId);
        var accountNumbers = await AccountNumbersAsync(read);

        AssertBalanced(entry.Postings);
        Assert.Equal(5, entry.Postings.Count);
        AssertLeg(entry.Postings, accountNumbers, "1400", PostingDirection.Debit, 333m, null);
        AssertLeg(entry.Postings, accountNumbers, "8400", PostingDirection.Credit, 100m, Steuerschluessel.Ust19);
        AssertLeg(entry.Postings, accountNumbers, "1776", PostingDirection.Credit, 19m, null);
        AssertLeg(entry.Postings, accountNumbers, "8300", PostingDirection.Credit, 200m, Steuerschluessel.Ust7);
        AssertLeg(entry.Postings, accountNumbers, "1771", PostingDirection.Credit, 14m, null);
    }

    [Fact]
    public async Task Failure_after_booking_rolls_back_invoice_and_journal_entry_together()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var documentId = await SeedInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var doc = await db.Set<SalesDocument>()
                .Include(document => document.Lines)
                .SingleAsync(document => document.Id == documentId);
            var profile = await db.Set<CompanyProfile>().SingleAsync();
            var partner = await db.Set<BusinessPartner>().SingleAsync(candidate => candidate.Id == doc.PartnerId);

            await using var tx = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<FinalizeAfterBookingException>(() =>
                SalesDocumentEndpoints.FinalizeCoreAsync(
                    doc,
                    profile,
                    partner,
                    db,
                    new NumberingService(db),
                    new ThrowingAuditWriter(),
                    new PostingEngine(db),
                    new AccountResolver(db),
                    NullLoggerFactory.Instance,
                    tenant,
                    "FinalizeTest",
                    CancellationToken.None));
            await tx.RollbackAsync();
        }

        await using var read = fixture.CreateAppContext(tenant);
        Assert.Equal(0, await read.Set<JournalEntry>().CountAsync());
        Assert.Equal(0, await read.Set<SalesDocumentTaxBreakdown>().CountAsync(row => row.DocumentId == documentId));
        var persisted = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(document => document.Id == documentId);
        Assert.Equal(DocumentStatus.Draft, persisted.Status);
        Assert.Null(persisted.DocumentNumber);
    }

    [Fact]
    public async Task Storno_creates_balanced_general_reversal_that_nets_original_to_zero()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Storno, "ST-");
        var originalDocumentId = await SeedInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);
        await FinalizeAsync(tenant, originalDocumentId);

        var stornoDocumentId = await StornoAsync(tenant, originalDocumentId);

        await using var read = fixture.CreateAppContext(tenant);
        var entries = await read.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .Where(entry => entry.SourceType == LedgerSourceType.Invoice)
            .ToListAsync();
        var original = Assert.Single(entries, entry => entry.SourceRef == originalDocumentId.ToString());
        var storno = Assert.Single(entries, entry => entry.SourceRef == stornoDocumentId.ToString());

        Assert.Equal(PostingType.Storno, storno.PostingType);
        Assert.Equal(original.Id, storno.ReversesEntryId);
        AssertBalanced(storno.Postings);
        Assert.Equal(original.Postings.Count, storno.Postings.Count);
        foreach (var posting in original.Postings)
        {
            Assert.Single(storno.Postings, reversed =>
                reversed.AccountId == posting.AccountId
                && reversed.Amount == posting.Amount
                && reversed.Direction != posting.Direction
                && reversed.Steuerschluessel == posting.Steuerschluessel
                && reversed.TaxRatePercent == posting.TaxRatePercent
                && reversed.TaxCategory == posting.TaxCategory);
        }

        var netByAccount = entries
            .SelectMany(entry => entry.Postings)
            .GroupBy(posting => posting.AccountId)
            .Select(group => group.Sum(posting =>
                posting.Direction == PostingDirection.Debit ? posting.Amount : -posting.Amount));
        Assert.All(netByAccount, net => Assert.Equal(0m, net));
    }

    [Fact]
    public async Task Tenant_without_ledger_settings_still_finalizes_without_booking()
    {
        var tenant = Guid.CreateVersion7();
        var documentId = await SeedInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);

        await FinalizeAsync(tenant, documentId);

        await using var read = fixture.CreateAppContext(tenant);
        var document = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(candidate => candidate.Id == documentId);
        Assert.Equal(DocumentStatus.Finalized, document.Status);
        Assert.Equal(0, await read.Set<JournalEntry>().CountAsync());
    }

    [Fact]
    public async Task Existing_source_entry_prevents_double_booking_on_finalize_retry_path()
    {
        var tenant = Guid.CreateVersion7();
        await SetupLedgerAsync(tenant);
        var documentId = await SeedInvoiceAsync(
            tenant,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)]);

        Guid existingEntryId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var input = new InvoicePostingInput(
                tenant,
                documentId,
                "retry-placeholder",
                DocumentDate,
                ChartVariant.Skr03,
                null,
                [new InvoicePostingBreakdown(TaxCategory.S, 19m, 100m, 19m)],
                119m,
                false,
                false);
            var existing = await new PostingEngine(db).PostAsync(
                new InvoicePostingSource(input, new AccountResolver(db)),
                new JournalEntry
                {
                    TenantId = tenant,
                    EntryDate = DocumentDate,
                    SourceRef = documentId.ToString(),
                    SourceType = LedgerSourceType.Invoice,
                    Description = "Existing retry booking",
                    PostingType = PostingType.Normal,
                },
                CancellationToken.None);
            existingEntryId = existing.Id;
        }

        await FinalizeAsync(tenant, documentId);

        await using var read = fixture.CreateAppContext(tenant);
        var entry = await read.Set<JournalEntry>().AsNoTracking().SingleAsync();
        Assert.Equal(existingEntryId, entry.Id);
        Assert.Equal(documentId.ToString(), entry.SourceRef);
    }

    private async Task SetupLedgerAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var currentTenant = new TenantContext();
        currentTenant.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Soll,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            currentTenant,
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private async Task<Guid> SeedInvoiceAsync(
        Guid tenant,
        IReadOnlyList<SalesTestData.LineSpec> lines)
    {
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        return await SalesTestData.SeedDraftAsync(
            fixture, tenant, DocumentType.Rechnung, partner.Id, lines, DocumentDate);
    }

    private async Task FinalizeAsync(Guid tenant, Guid documentId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var document = await db.Set<SalesDocument>()
            .Include(candidate => candidate.Lines)
            .SingleAsync(candidate => candidate.Id == documentId);
        var profile = await db.Set<CompanyProfile>().SingleAsync();
        var partner = await db.Set<BusinessPartner>().SingleAsync(candidate => candidate.Id == document.PartnerId);

        await using var tx = await db.Database.BeginTransactionAsync();
        await SalesDocumentEndpoints.FinalizeCoreAsync(
            document,
            profile,
            partner,
            db,
            new NumberingService(db),
            new NoOpAuditWriter(),
            new PostingEngine(db),
            new AccountResolver(db),
            NullLoggerFactory.Instance,
            tenant,
            "FinalizeTest",
            CancellationToken.None);
        await tx.CommitAsync();
    }

    private async Task<Guid> StornoAsync(Guid tenant, Guid originalDocumentId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var original = await db.Set<SalesDocument>()
            .Include(document => document.Lines)
            .SingleAsync(document => document.Id == originalDocumentId);
        var profile = await db.Set<CompanyProfile>().SingleAsync();
        var partner = await db.Set<BusinessPartner>().SingleAsync(candidate => candidate.Id == original.PartnerId);
        var storno = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Storno,
            Status = DocumentStatus.Draft,
            CorrectsDocumentId = original.Id,
            PartnerId = original.PartnerId,
            DocumentDate = DocumentDate.AddDays(1),
            ServiceDate = original.ServiceDate,
            Currency = original.Currency,
        };
        foreach (var line in original.Lines.OrderBy(line => line.LineNumber))
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
        var postingEngine = new PostingEngine(db);
        await SalesDocumentEndpoints.FinalizeCoreAsync(
            storno,
            profile,
            partner,
            db,
            new NumberingService(db),
            new NoOpAuditWriter(),
            postingEngine,
            new AccountResolver(db),
            NullLoggerFactory.Instance,
            tenant,
            "StornoTest",
            CancellationToken.None);

        original.Status = DocumentStatus.Cancelled;
        original.CancelledByDocumentId = storno.Id;
        var openItem = await db.Set<OpenItem>().SingleAsync(item => item.DocumentId == original.Id);
        openItem.Status = OpenItemStatus.Cancelled;
        openItem.OpenAmount = 0m;

        await SalesDocumentEndpoints.PostStornoReversalAsync(
            original,
            storno,
            db,
            postingEngine,
            NullLoggerFactory.Instance,
            tenant,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return storno.Id;
    }

    private static async Task<JournalEntry> InvoiceEntryAsync(
        NumeraDbContext db,
        Guid documentId) =>
        await db.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .SingleAsync(entry =>
                entry.SourceType == LedgerSourceType.Invoice
                && entry.SourceRef == documentId.ToString());

    private static async Task<IReadOnlyDictionary<Guid, string>> AccountNumbersAsync(NumeraDbContext db) =>
        await db.Set<Account>()
            .AsNoTracking()
            .ToDictionaryAsync(account => account.Id, account => account.Number);

    private static void AssertBalanced(IEnumerable<Posting> postings)
    {
        var list = postings.ToList();
        var debit = list.Where(posting => posting.Direction == PostingDirection.Debit).Sum(posting => posting.Amount);
        var credit = list.Where(posting => posting.Direction == PostingDirection.Credit).Sum(posting => posting.Amount);
        Assert.Equal(decimal.Round(debit, 2, MidpointRounding.AwayFromZero), debit);
        Assert.Equal(decimal.Round(credit, 2, MidpointRounding.AwayFromZero), credit);
        Assert.Equal(debit, credit);
    }

    private static Posting AssertLeg(
        IEnumerable<Posting> postings,
        IReadOnlyDictionary<Guid, string> accountNumbers,
        string accountNumber,
        PostingDirection direction,
        decimal amount,
        Steuerschluessel? key) =>
        Assert.Single(postings, posting =>
            accountNumbers[posting.AccountId] == accountNumber
            && posting.Direction == direction
            && posting.Amount == amount
            && posting.Steuerschluessel == key);

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent evt, CancellationToken ct) =>
            throw new FinalizeAfterBookingException();
    }

    private sealed class FinalizeAfterBookingException : Exception
    {
    }
}
