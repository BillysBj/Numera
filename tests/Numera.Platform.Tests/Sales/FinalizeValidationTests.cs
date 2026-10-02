using Numera.Api.Validators;
using Numera.Modules.Crm;
using Numera.Modules.Sales;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class FinalizeValidationTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData(" ", "\t", false)]
    [InlineData("DE123456789", null, true)]
    [InlineData(null, "12/345/67890", true)]
    [InlineData("DE123456789", "12/345/67890", true)]
    public void Issuer_requires_at_least_one_tax_identifier(string? vatId, string? taxNumber, bool valid)
    {
        var document = new SalesDocument
        {
            DocumentType = DocumentType.Rechnung,
            Lines = [new SalesDocumentLine { Name = "Beratung", Quantity = 1m, UnitCode = "HUR", NetUnitPrice = 100m }],
        };
        var issuer = new CompanyProfile
        {
            LegalName = "Muster GmbH",
            Address = new Numera.Modules.Sales.Address { Street = "Hauptstraße 1", PostalCode = "10115", City = "Berlin" },
            VatId = vatId,
            TaxNumber = taxNumber,
        };
        var recipient = new BusinessPartner
        {
            Name = "Kunde AG",
            BillingAddress = new Numera.Modules.Crm.Address { Street = "Kundenweg 5", PostalCode = "80331", City = "München" },
        };

        var errors = FinalizeValidation.Check(document, issuer, recipient);

        if (valid)
        {
            Assert.Empty(errors);
        }
        else
        {
            var error = Assert.Single(errors);
            Assert.Equal("Issuer", error.Key);
            Assert.Equal("At least one of the issuer VAT ID or tax number is required (§14 UStG, BT-31/BT-32).", Assert.Single(error.Value));
        }
    }
}
