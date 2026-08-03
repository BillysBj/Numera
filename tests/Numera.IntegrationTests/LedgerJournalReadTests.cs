using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of the RLS-scoped journal and Kontoauszug reads.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerJournalReadTests(PostgresFixture fixture)
{
    private static readonly DateOnly InvoiceDate = new(2026, 8, 1);
    private static readonly DateOnly PaymentDate = new(2026, 8, 4);

    [Fact]
    public async Task Journal_returns_balanced_invoice_and_payment_with_stable_keyset_pages()
    {
        var arranged = await ArrangeInvoiceAndPaymentAsync();
        await using var db = fixture.CreateAppContext(arranged.Tenant);

        var journal = await LedgerEndpoints.GetJournalAsync(
            db, null, null, null, null, 10, CancellationToken.None);

        Assert.Equal(2, journal.Items.Count);
        Assert.Contains(journal.Items, item => item.SourceType == LedgerSourceType.Invoice);
        Assert.Contains(journal.Items, item => item.SourceType == LedgerSourceType.Payment);
        Assert.All(journal.Items, item =>
        {
            Assert.Equal(item.TotalDebit, item.TotalCredit);
            Assert.Equal(arranged.Amount, item.TotalDebit);
            Assert.False(item.IsFestgeschrieben);
        });

        var firstPage = await LedgerEndpoints.GetJournalAsync(
            db, null, null, null, null, 1, CancellationToken.None);
        var cursor = Assert.IsType<Guid>(firstPage.NextCursor);
        var secondPage = await LedgerEndpoints.GetJournalAsync(
            db, null, null, null, cursor, 1, CancellationToken.None);

        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task Forderungen_statement_runs_from_invoice_debit_to_payment_credit_and_zero()
    {
        var arranged = await ArrangeInvoiceAndPaymentAsync();
        await using var db = fixture.CreateAppContext(arranged.Tenant);
        var receivableId = await db.Set<Account>()
            .Where(account => account.Number == "1400")
            .Select(account => account.Id)
            .SingleAsync();

        var statement = await LedgerEndpoints.GetAccountStatementAsync(
            db, receivableId, null, null, null, 10, CancellationToken.None);
        var rows = Assert.IsType<PagedEnvelope<AccountStatementRow>>(statement).Items;

        Assert.Equal(2, rows.Count);
        Assert.Equal(InvoiceDate, rows[0].EntryDate);
        Assert.Equal(arranged.Amount, rows[0].Debit);
        Assert.Equal(0m, rows[0].Credit);
        Assert.Equal(arranged.Amount, rows[0].RunningBalance);
        Assert.Equal(PaymentDate, rows[1].EntryDate);
        Assert.Equal(0m, rows[1].Debit);
        Assert.Equal(arranged.Amount, rows[1].Credit);
        Assert.Equal(0m, rows[1].RunningBalance);

        var firstPage = await LedgerEndpoints.GetAccountStatementAsync(
            db, receivableId, null, null, null, 1, CancellationToken.None);
        var cursor = Assert.IsType<Guid>(firstPage!.NextCursor);
        var secondPage = await LedgerEndpoints.GetAccountStatementAsync(
            db, receivableId, null, null, cursor, 1, CancellationToken.None);
        Assert.Single(firstPage.Items);
        Assert.Single(secondPage!.Items);
        Assert.Equal(0m, secondPage.Items[0].RunningBalance);
    }

    [Fact]
    public async Task Other_tenant_cannot_read_journal_or_account_statement()
    {
        var arranged = await ArrangeInvoiceAndPaymentAsync();
        Guid receivableId;
        await using (var owner = fixture.CreateAppContext(arranged.Tenant))
        {
            receivableId = await owner.Set<Account>()
                .Where(account => account.Number == "1400")
                .Select(account => account.Id)
                .SingleAsync();
        }

        await using var other = fixture.CreateAppContext(Guid.CreateVersion7());
        var journal = await LedgerEndpoints.GetJournalAsync(
            other, null, null, null, null, 10, CancellationToken.None);
        var statement = await LedgerEndpoints.GetAccountStatementAsync(
            other, receivableId, null, null, null, 10, CancellationToken.None);

        Assert.Empty(journal.Items);
        Assert.Null(statement);
    }

    private async Task<(Guid Tenant, Guid PaymentId, decimal Amount)> ArrangeInvoiceAndPaymentAsync()
    {
        var tenant = Guid.CreateVersion7();
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SetupLedgerAsync(tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            InvoiceDate);
        await using (var finalize = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(finalize, documentId);
        }

        Guid openItemId;
        decimal amount;
        await using (var read = fixture.CreateAppContext(tenant))
        {
            var openItem = await read.Set<OpenItem>().AsNoTracking()
                .SingleAsync(item => item.DocumentId == documentId);
            openItemId = openItem.Id;
            amount = openItem.OriginalAmount;
        }

        Guid paymentId;
        await using (var paymentDb = fixture.CreateAppContext(tenant))
        {
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var result = await new PaymentService(paymentDb, tenantContext, new NoOpAuditWriter())
                .RecordAsync(new RecordPaymentRequest(
                    amount,
                    PaymentDate,
                    PaymentMethod.BankTransfer,
                    "JOURNAL-READ",
                    [new PaymentAllocationInput(openItemId, amount)]));
            paymentId = Assert.IsType<Guid>(result.PaymentId);
        }

        return (tenant, paymentId, amount);
    }

    private async Task SetupLedgerAsync(Guid tenant)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        var result = await LedgerSetupEndpoints.SetupAsync(
            new LedgerSetupRequest(
                ChartVariant.Skr03,
                Besteuerungsart.Soll,
                Gewinnermittlungsart.Euer,
                null),
            db,
            new ChartSeeder(db),
            new NoOpAuditWriter(),
            tenantContext,
            CancellationToken.None);
        Assert.Equal(
            StatusCodes.Status201Created,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
