using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Validators;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Money;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Integration coverage for the frozen foreign-currency invoice core (INV-05).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ForeignCurrencyInvoiceTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task Finalize_usd_invoice_freezes_eur_vat_and_keeps_document_and_open_item_in_usd()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 7, 28);
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");

        var doc = SalesTestData.BuildDraft(
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("USD consulting", 1m, 100m, TaxCategory.S, 19m)],
            date);
        doc.Currency = "USD";
        doc.ExchangeRate = 1.10m;
        doc.ExchangeRateDate = date;

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            db.Add(doc);
            await db.SaveChangesAsync();
            await SalesTestData.FinalizeAsync(db, doc.Id);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var finalized = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == doc.Id);
        var openItem = await read.Set<OpenItem>().AsNoTracking().SingleAsync(x => x.DocumentId == doc.Id);

        Assert.Equal("USD", finalized.Currency);
        Assert.Equal(1.10m, finalized.ExchangeRate);
        Assert.Equal(date, finalized.ExchangeRateDate);
        Assert.Equal(RoundingPolicy.RoundAmount(finalized.TotalTax / 1.10m), finalized.TotalTaxEur);
        Assert.Equal(119m, finalized.TotalGross);
        Assert.Equal("USD", openItem.Currency);
        Assert.Equal(finalized.TotalGross, openItem.OriginalAmount);
        Assert.Equal(finalized.TotalGross, openItem.OpenAmount);
    }

    [Fact]
    public async Task Finalize_eur_invoice_leaves_eur_vat_conversion_null()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 7, 28);
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var id = await SalesTestData.SeedDraftAsync(
            _fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("EUR consulting", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, id);
        }

        await using var read = _fixture.CreateAppContext(tenant);
        var finalized = await read.Set<SalesDocument>().AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal("EUR", finalized.Currency);
        Assert.Null(finalized.TotalTaxEur);
        Assert.Null(finalized.ExchangeRate);
        Assert.Null(finalized.ExchangeRateDate);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("BHD")]
    public async Task Create_validator_rejects_non_two_minor_unit_currency(string currency)
    {
        Assert.False(CurrencyScope.IsSupported(currency));
        Assert.True(CurrencyScope.IsSupported("USD"));

        var request = new CreateSalesDocumentRequest(
            DocumentType.Rechnung,
            Guid.CreateVersion7(),
            new DateOnly(2026, 7, 28),
            null,
            null,
            null,
            [new SalesLineRequest(null, "Line", null, 1m, "C62", 100m, TaxCategory.S, 19m)],
            currency,
            1.10m,
            new DateOnly(2026, 7, 28));

        var result = await new CreateSalesDocumentRequestValidator().ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "Currency"
                && failure.ErrorMessage == CurrencyScope.UnsupportedReason);
    }
}
