using System.Globalization;
using System.Text.Json;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using QuestPDF.Infrastructure;

using Xunit;

namespace Numera.Platform.Tests.Sales;

/// <summary>
/// Golden-ish render suite for the §14 invoice PDF unit (SnapshotReader → InvoiceDocument).
/// Pure unit — no DB: a hand-built <see cref="SalesDocument"/> with frozen camelCase jsonb
/// snapshots (matching <c>SerializeIssuer</c>/<c>SerializeRecipient</c>, which use
/// <see cref="JsonSerializerDefaults.Web"/>) is mapped and rendered to real PDF bytes in
/// German and English.
/// </summary>
/// <remarks>
/// Asserts on the render MODEL (the legally load-bearing Pflichttext survives) + the byte
/// stream's validity (non-empty, %PDF magic) + the de-DE culture formatter — NOT on rendered
/// pixels (brittle). QuestPDF's Community license MUST be set or <c>GeneratePdf</c> throws
/// (Pitfall 1); the static ctor sets it once for the whole class.
/// </remarks>
public class InvoiceDocumentTests
{
    private const string ReverseChargePflichttext =
        "Steuerschuldnerschaft des Leistungsempfängers (Reverse Charge, § 13b UStG).";

    static InvoiceDocumentTests()
    {
        // REQUIRED before any GeneratePdf (Pitfall 1). Idempotent; Numera qualifies (< $1M).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // A realistic finalized invoice: one taxed (S 19%) line + one reverse-charge (AE) line
    // whose breakdown row carries a non-empty, legally-frozen German Pflichttext.
    private static SalesDocument BuildFixture()
    {
        var tenantId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();

        var issuer = JsonSerializer.Serialize(new
        {
            LegalName = "Muster GmbH",
            Address = new
            {
                Street = "Hauptstraße 1",
                Line2 = (string?)null,
                PostalCode = "10115",
                City = "Berlin",
                CountryCode = "DE",
                PoBox = (string?)null,
            },
            VatId = "DE123456789",
            TaxNumber = (string?)null,
            IsKleinunternehmer = false,
            Bank = new { Iban = "DE02120300000000202051", Bic = "BYLADEM1001", BankName = "Musterbank" },
            RegisterCourt = "Amtsgericht Berlin",
            RegisterNumber = "HRB 12345",
            ManagingDirector = "Max Muster",
            ContactEmail = "info@muster.de",
            ContactPhone = "+49 30 123456",
        }, WebJson);

        var recipient = JsonSerializer.Serialize(new
        {
            Name = "Kunde AG",
            LegalForm = "AG",
            BillingAddress = new
            {
                Street = "Kundenweg 5",
                Line2 = (string?)null,
                PostalCode = "80331",
                City = "München",
                CountryCode = "DE",
                PoBox = (string?)null,
            },
            VatId = "DE987654321",
            TaxNumber = (string?)null,
            Email = "kunde@example.com",
        }, WebJson);

        return new SalesDocument
        {
            Id = docId,
            TenantId = tenantId,
            DocumentType = DocumentType.Rechnung,
            Status = DocumentStatus.Finalized,
            DocumentNumber = "RE-2026-00042",
            DocumentDate = new DateOnly(2026, 7, 13),
            ServiceDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 27),
            Currency = "EUR",
            BuyerReference = "LW-991-2026",
            Notes = "Vielen Dank für Ihren Auftrag.",
            IssuerSnapshot = issuer,
            RecipientSnapshot = recipient,
            TotalNet = 800.00m,
            TotalTax = 57.00m,
            TotalGross = 857.00m,
            AmountDue = 857.00m,
            IsKleinunternehmer = false,
            ReverseCharge = true,
            Lines =
            [
                new SalesDocumentLine
                {
                    TenantId = tenantId,
                    DocumentId = docId,
                    LineNumber = 1,
                    Name = "Beratungsleistung",
                    Description = "Konzeption & Umsetzung",
                    Quantity = 3m,
                    UnitCode = "HUR",
                    NetUnitPrice = 100m,
                    LineNetAmount = 300m,
                    TaxCategory = TaxCategory.S,
                    VatRatePercent = 19m,
                },
                new SalesDocumentLine
                {
                    TenantId = tenantId,
                    DocumentId = docId,
                    LineNumber = 2,
                    Name = "Bauleistung (§13b)",
                    Description = null,
                    Quantity = 1m,
                    UnitCode = "C62",
                    NetUnitPrice = 500m,
                    LineNetAmount = 500m,
                    TaxCategory = TaxCategory.AE,
                    VatRatePercent = 0m,
                },
            ],
            TaxBreakdown =
            [
                new SalesDocumentTaxBreakdown
                {
                    TenantId = tenantId,
                    DocumentId = docId,
                    TaxCategory = TaxCategory.S,
                    VatRatePercent = 19m,
                    TaxableBase = 300m,
                    TaxAmount = 57m,
                },
                new SalesDocumentTaxBreakdown
                {
                    TenantId = tenantId,
                    DocumentId = docId,
                    TaxCategory = TaxCategory.AE,
                    VatRatePercent = 0m,
                    TaxableBase = 500m,
                    TaxAmount = 0m,
                    ExemptionReasonCode = "VATEX-EU-AE",
                    ExemptionReasonText = ReverseChargePflichttext,
                },
            ],
        };
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Renders_frozen_snapshot_to_valid_pdf_bytes(string language)
    {
        var model = SnapshotReader.FromDocument(BuildFixture(), logoBytes: null, logoContentType: null, language);

        var pdf = InvoiceDocument.Render(model);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, $"Expected a non-trivial PDF, got {pdf.Length} bytes.");
        // %PDF magic-byte prefix.
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Model_carries_the_frozen_Pflichttext_verbatim(string language)
    {
        var model = SnapshotReader.FromDocument(BuildFixture(), logoBytes: null, logoContentType: null, language);

        var exemptRow = Assert.Single(model.BreakdownRows, r => r.TaxCategory == TaxCategory.AE);
        // Verbatim — the legally-frozen note stays German even under the English label set.
        Assert.Equal(ReverseChargePflichttext, exemptRow.ExemptionReasonText);
    }

    [Fact]
    public void SnapshotReader_maps_the_frozen_snapshot_without_live_master_data()
    {
        var model = SnapshotReader.FromDocument(BuildFixture());

        Assert.Equal("de", model.Language);
        Assert.Equal("Muster GmbH", model.Issuer.LegalName);
        Assert.Equal("Hauptstraße 1", model.Issuer.Address.Street);
        Assert.Equal("Berlin", model.Issuer.Address.City);
        Assert.Equal("DE123456789", model.Issuer.VatId);
        Assert.Equal("DE02120300000000202051", model.Issuer.Iban);
        Assert.Equal("Musterbank", model.Issuer.BankName);
        Assert.Equal("Kunde AG", model.Recipient.Name);
        Assert.Equal("München", model.Recipient.BillingAddress.City);
        Assert.Equal("RE-2026-00042", model.DocumentNumber);
        Assert.Equal(2, model.Lines.Count);
        Assert.True(model.ReverseCharge);
        Assert.Equal(857.00m, model.TotalGross);
    }

    [Fact]
    public void En_language_input_selects_english_labels_but_keeps_frozen_totals()
    {
        var model = SnapshotReader.FromDocument(BuildFixture(), language: "EN");

        Assert.Equal("en", model.Language);
        Assert.Equal(857.00m, model.TotalGross);
    }

    [Fact]
    public void DeDe_formatter_produces_german_grouping()
    {
        var deDe = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("1.234,56", InvoiceDocument.FormatNumber(1234.56m, deDe));
    }
}
