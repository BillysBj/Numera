using Numera.Api.Contracts;
using Numera.Api.Validators;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class CompanyProfileValidatorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void Closing_texts_are_optional_and_limited_to_2000_characters(int? length, bool valid)
    {
        var text = length.HasValue ? new string('x', length.Value) : null;
        var request = new UpdateCompanyProfileRequest(
            LegalName: "Muster GmbH",
            Address: new AddressDto("Hauptstraße 1", null, "10115", "Berlin", "DE", null),
            VatId: "DE123456789", TaxNumber: null, IsKleinunternehmer: false,
            DefaultPaymentTermsNetDays: null, DefaultTaxCategory: null,
            Iban: null, Bic: null, BankName: null, RegisterCourt: null, RegisterNumber: null,
            ManagingDirector: null, ContactEmail: null, ContactPhone: null, LogoRef: null,
            InvoiceFooterText: text, DeliveryNoteFooterText: text);

        var result = new UpdateCompanyProfileRequestValidator().Validate(request);

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.InvoiceFooterText));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.DeliveryNoteFooterText));
        }
    }
}
