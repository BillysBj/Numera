using System.Reflection;
using System.Text.Json;

using Numera.Api.Contracts;
using Numera.Api.Validators;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Pdf;
using Numera.Modules.Sales.Vat;
using Numera.Platform.Money;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public class LineDiscountTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("99.999999", true)]
    [InlineData("-0.01", false)]
    [InlineData("100", false)]
    public void Validates_discount_range(string value, bool valid)
    {
        var request = Request(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(valid, new SalesLineRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Omitted_discount_defaults_to_zero()
    {
        var request = JsonSerializer.Deserialize<SalesLineRequest>("""
            {"Name":"Service","Quantity":1,"UnitCode":"HUR","NetUnitPrice":100,"VatRatePercent":19}
            """);
        Assert.Equal(0m, request!.DiscountPercent);
    }

    [Fact]
    public void Replace_lines_persists_discount_and_uses_it_for_net_vat_and_snapshot()
    {
        var doc = new SalesDocument { TenantId = Guid.NewGuid() };
        var endpoints = typeof(SalesLineRequestValidator).Assembly.GetType("Numera.Api.Endpoints.SalesDocumentEndpoints")!;
        endpoints.GetMethod("ReplaceLines", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [doc, new[] { Request(12.5m) }, doc.TenantId]);
        var line = Assert.Single(doc.Lines);
        Assert.Equal(12.5m, line.DiscountPercent);
        Assert.Equal(262.5m, line.LineNetAmount);
        var vat = Assert.Single(VatCalculationService.Calculate([new VatLineInput(line.TaxCategory, line.VatRatePercent, line.LineNetAmount)], false));
        Assert.Equal(262.5m, vat.TaxableBase);
        Assert.Equal(49.88m, vat.TaxAmount);
        var snapshot = SnapshotReader.FromDocument(doc);
        Assert.Equal(12.5m, Assert.Single(snapshot.Lines).DiscountPercent);
    }

    [Fact]
    public void Four_decimal_midpoints_round_away_from_zero()
    {
        Assert.Equal(0.0005m, RoundingPolicy.LineNetAmount(1m, 0.001m, 55m));
        Assert.Equal(-0.0005m, RoundingPolicy.LineNetAmount(-1m, 0.001m, 55m));
        Assert.Equal(17.4913m, RoundingPolicy.LineNetAmount(1m, 19.99m, 12.5m));
        Assert.Equal(300m, RoundingPolicy.LineNetAmount(3m, 100m));
    }

    [Fact]
    public void Provisional_invoice_sums_cent_rounded_lines_for_vat_and_preserves_discount()
    {
        var doc = new SalesDocument { TenantId = Guid.NewGuid() };
        doc.Lines = Enumerable.Range(1, 3).Select(i => new SalesDocumentLine
        {
            LineNumber = i, Name = "Service", UnitCode = "HUR", Quantity = 1m,
            NetUnitPrice = 19.99m, DiscountPercent = 12.5m, LineNetAmount = 17.4913m,
            TaxCategory = TaxCategory.S, VatRatePercent = 19m,
        }).ToList();
        var profile = new CompanyProfile { LegalName = "Seller", Address = new() { Street = "Street 1", PostalCode = "10115", City = "Berlin" } };
        var model = (InvoicePdfModel)typeof(EInvoiceService)
            .GetMethod("BuildProvisionalModel", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [doc, profile, null])!;
        Assert.Equal(52.47m, model.TotalNet);
        Assert.Equal(9.97m, model.TotalTax);
        Assert.Equal(62.44m, model.TotalGross);
        Assert.All(model.Lines, line => Assert.Equal(12.5m, line.DiscountPercent));
        Assert.Equal(52.47m, Assert.Single(model.BreakdownRows).TaxableBase);
    }

    private static SalesLineRequest Request(decimal discount) =>
        new(null, "Service", null, 3m, "HUR", 100m, TaxCategory.S, 19m, discount);
}
