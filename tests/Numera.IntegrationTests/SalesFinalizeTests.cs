using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Modules.Sales;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The INV-01/INV-04 + OPDN-01 + DOCS-04 hard gates: proves the finalize side-effects and
/// the DB-enforced immutability of a finalized invoice on real postgres:18 as the
/// non-BYPASSRLS <c>numera_app</c> role. Finalize (a faithful mirror of the production
/// <c>FinalizeCoreAsync</c> via <see cref="SalesTestData"/>) is asserted to: create the
/// correct open item (due date, amounts, status), persist the BG-23 breakdown with
/// per-category rounding, freeze issuer + recipient snapshots, and assign the configured
/// number format. The §14 completeness gate blocks finalization of an incomplete document,
/// finalized business columns cannot be UPDATEd or the row DELETEd (a whitelisted lifecycle
/// update still succeeds), and a Kleinunternehmer issuer produces a zero-VAT §19 breakdown.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesFinalizeTests
{
    private readonly PostgresFixture _fixture;

    public SalesFinalizeTests(PostgresFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------ side-effects

    [Fact]
    public async Task Finalize_creates_open_item_breakdown_snapshots_and_a_formatted_number()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        // Recipient with 30-day net terms — the open item due date must derive from it.
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);

        // Mixed 19/7 lines: net 100 @19% (19.00) + net 200 @7% (14.00) → gross 333.00.
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var doc = await read.Set<SalesDocument>()
            .AsNoTracking()
            .Include(d => d.TaxBreakdown)
            .FirstAsync(d => d.Id == docId);

        // (d) number matches the configured RE-YYYY-##### format, first in the series.
        Assert.Equal("RE-2026-00001", doc.DocumentNumber);
        Assert.Equal(DocumentStatus.Finalized, doc.Status);

        // Totals: per-category rounded tax summed (19.00 + 14.00 = 33.00), gross = 333.00.
        Assert.Equal(300.00m, doc.TotalNet);
        Assert.Equal(33.00m, doc.TotalTax);
        Assert.Equal(333.00m, doc.TotalGross);

        // (b) one breakdown row per (category, rate); document tax == Σ breakdown tax.
        Assert.Equal(2, doc.TaxBreakdown.Count);
        Assert.Contains(doc.TaxBreakdown, b => b.VatRatePercent == 19m && b.TaxableBase == 100m && b.TaxAmount == 19.00m);
        Assert.Contains(doc.TaxBreakdown, b => b.VatRatePercent == 7m && b.TaxableBase == 200m && b.TaxAmount == 14.00m);
        Assert.Equal(doc.TotalTax, doc.TaxBreakdown.Sum(b => b.TaxAmount));

        // (c) issuer + recipient snapshots frozen (contain the legal / recipient names).
        Assert.NotNull(doc.IssuerSnapshot);
        Assert.NotNull(doc.RecipientSnapshot);
        Assert.Contains("Aussteller GmbH", doc.IssuerSnapshot);
        Assert.Contains("Empfänger AG", doc.RecipientSnapshot!);

        // (a) open item: due = document date + partner net days; amounts = gross; Open.
        var openItem = await read.Set<OpenItem>().AsNoTracking().FirstAsync(o => o.DocumentId == docId);
        Assert.Equal(date.AddDays(30), openItem.DueDate);
        Assert.Equal(doc.TotalGross, openItem.OriginalAmount);
        Assert.Equal(doc.TotalGross, openItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, openItem.Status);
        Assert.Equal(doc.DocumentNumber, openItem.DocumentNumber);
    }

    [Fact]
    public async Task Finalize_per_category_rounding_sums_rounded_rows_not_the_grand_total()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);

        // Two 19% lines each net 2.10: per-line/-bucket base 4.20 → round(0.798) = 0.80.
        // (Rounding the grand total of two 0.399 shares would diverge — proves BR-CO-14.)
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [
                new SalesTestData.LineSpec("A", 1m, 2.10m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("B", 1m, 2.10m, TaxCategory.S, 19m),
            ],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == docId);

        Assert.Equal(4.20m, doc.TotalNet);
        Assert.Equal(0.80m, doc.TotalTax);
        Assert.Equal(5.00m, doc.TotalGross);
    }

    // ------------------------------------------------------ §14 gate

    [Fact]
    public async Task Finalize_without_a_company_profile_is_blocked_and_burns_no_number()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        // No CompanyProfile seeded — the §14 gate must block finalize.
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Position", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using var db = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<FinalizeGateException>(() => SalesTestData.FinalizeAsync(db, docId));
        Assert.Contains("Issuer", ex.Errors.Keys);

        // No number was claimed (the gate runs before the counter).
        await using var read = _fixture.CreateAppContext(tenant);
        Assert.Empty(await read.Set<NumberSequence>().AsNoTracking().ToListAsync());
        var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == docId);
        Assert.Null(doc.DocumentNumber);
        Assert.Equal(DocumentStatus.Draft, doc.Status);
    }

    [Fact]
    public async Task Finalize_of_an_AE_line_without_a_recipient_vat_id_is_blocked()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        // Recipient WITHOUT a VAT ID — an AE (§13b reverse-charge) line then fails the gate.
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, vatId: null);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Bauleistung", 1m, 1000m, TaxCategory.AE, 0m)],
            date);

        await using var db = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<FinalizeGateException>(() => SalesTestData.FinalizeAsync(db, docId));
        Assert.Contains("Recipient", ex.Errors.Keys);
    }

    // ------------------------------------------------------ DB immutability

    [Fact]
    public async Task Finalized_invoice_business_column_update_is_rejected_by_the_db()
    {
        var (tenant, docId) = await FinalizeSimpleAsync();

        await using var db = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sales_documents SET total_gross = 999 WHERE id = {docId}"));
        AssertImmutable(ex);
    }

    [Fact]
    public async Task Finalized_invoice_delete_is_rejected_by_the_db()
    {
        var (tenant, docId) = await FinalizeSimpleAsync();

        await using var db = _fixture.CreateAppContext(tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM sales_documents WHERE id = {docId}"));
        AssertImmutable(ex);
    }

    [Fact]
    public async Task Finalized_invoice_whitelisted_lifecycle_update_succeeds()
    {
        var (tenant, docId) = await FinalizeSimpleAsync();

        await using var db = _fixture.CreateAppContext(tenant);
        // status Finalized(1) -> Sent(2) is a whitelisted lifecycle transition.
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales_documents SET status = 2, sent_at = now() WHERE id = {docId}");
        Assert.Equal(1, updated);
    }

    // ------------------------------------------------------ Kleinunternehmer §19

    [Fact]
    public async Task Kleinunternehmer_finalize_produces_a_zero_vat_exempt_breakdown_with_the_para19_note()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        // Issuer flagged §19 Kleinunternehmer → the whole document is category-E, zero tax.
        await SalesTestData.SeedProfileAsync(_fixture, tenant, kleinunternehmer: true);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [
                new SalesTestData.LineSpec("Leistung 1", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Leistung 2", 1m, 50m, TaxCategory.S, 7m),
            ],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var doc = await read.Set<SalesDocument>()
            .AsNoTracking()
            .Include(d => d.TaxBreakdown)
            .FirstAsync(d => d.Id == docId);

        Assert.True(doc.IsKleinunternehmer);
        Assert.Equal(0m, doc.TotalTax);
        Assert.Equal(doc.TotalNet, doc.TotalGross);

        var row = Assert.Single(doc.TaxBreakdown);
        Assert.Equal(TaxCategory.E, row.TaxCategory);
        Assert.Equal(0m, row.TaxAmount);
        Assert.Contains("§19", row.ExemptionReasonText);
    }

    // ------------------------------------------------------------- helpers

    private async Task<(Guid Tenant, Guid DocId)> FinalizeSimpleAsync()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Position", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using var db = _fixture.CreateAppContext(tenant);
        await SalesTestData.FinalizeAsync(db, docId);
        return (tenant, docId);
    }

    private static void AssertImmutable(PostgresException ex)
    {
        var byMessage = ex.MessageText.Contains("immutable", StringComparison.OrdinalIgnoreCase);
        var bySqlState = ex.SqlState == PostgresErrorCodes.RaiseException;
        Assert.True(byMessage || bySqlState, $"Unexpected error: {ex.SqlState} {ex.MessageText}");
    }
}
