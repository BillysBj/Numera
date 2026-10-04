using System.Globalization;
using System.Text.Json;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using QuestPDF.Infrastructure;

using UglyToad.PdfPig;

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

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Discounted_line_renders_without_changing_the_table_layout(string language)
    {
        var doc = BuildFixture();
        doc.Lines[0].DiscountPercent = 12.5m;
        doc.Lines[0].LineNetAmount = RoundingPolicy.LineNetAmount(doc.Lines[0].Quantity, doc.Lines[0].NetUnitPrice, 12.5m);
        var model = SnapshotReader.FromDocument(doc, language: language);
        Assert.Equal(12.5m, model.Lines[0].DiscountPercent);
        var pdf = InvoiceDocument.Render(model);
        Assert.True(pdf.Length > 1000);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("de", "Kundennummer")]
    [InlineData("en", "Customer no.")]
    public void Frozen_customer_number_is_mapped_and_rendered(string language, string label)
    {
        var document = BuildFixture();
        var recipient = System.Text.Json.Nodes.JsonNode.Parse(document.RecipientSnapshot!)!;
        recipient["customerNumber"] = "K-00042";
        document.RecipientSnapshot = recipient.ToJsonString();

        var model = SnapshotReader.FromDocument(document, language: language);

        Assert.Equal("K-00042", model.Recipient.CustomerNumber);
        Assert.Contains(WithoutWhitespace($"{label}: K-00042"), RenderText(model));
    }

    [Theory]
    [InlineData("de", null)]
    [InlineData("en", null)]
    [InlineData("de", "")]
    [InlineData("en", "")]
    [InlineData("de", "   ")]
    [InlineData("en", "   ")]
    public void Empty_customer_number_omits_the_meta_line(string language, string? number)
    {
        var document = BuildFixture();
        var recipient = System.Text.Json.Nodes.JsonNode.Parse(document.RecipientSnapshot!)!;
        recipient["customerNumber"] = number;
        document.RecipientSnapshot = recipient.ToJsonString();

        var model = SnapshotReader.FromDocument(document, language: language);

        Assert.Equal(number, model.Recipient.CustomerNumber);
        Assert.DoesNotContain(WithoutWhitespace(PdfLabels.For(language).CustomerNumber), RenderText(model));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Legacy_snapshot_without_customer_number_still_renders(string language)
    {
        var model = SnapshotReader.FromDocument(BuildFixture(), language: language);

        Assert.Null(model.Recipient.CustomerNumber);
        Assert.DoesNotContain(WithoutWhitespace(PdfLabels.For(language).CustomerNumber), RenderText(model));
    }

    public static IEnumerable<object[]> SupplyDateDocumentTypes() =>
        from language in new[] { "de", "en" }
        from type in Enum.GetValues<DocumentType>()
        select new object[] { language, type, type is DocumentType.Rechnung or DocumentType.Abschlagsrechnung
            or DocumentType.Schlussrechnung or DocumentType.Gutschrift or DocumentType.Storno };

    [Theory]
    [MemberData(nameof(SupplyDateDocumentTypes))]
    public void Missing_service_date_renders_invoice_date_note_only_for_invoice_types(
        string language, DocumentType type, bool expectsNote)
    {
        var document = BuildFixture();
        document.DocumentType = type;
        document.ServiceDate = null;
        var model = SnapshotReader.FromDocument(document, language: language);

        var text = RenderText(model);
        var note = WithoutWhitespace(language == "en"
            ? "Date of supply is the invoice date"
            : "Leistungsdatum entspricht dem Rechnungsdatum");

        Assert.Equal(expectsNote, text.Contains(note, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("de", false)]
    [InlineData("en", false)]
    [InlineData("de", true)]
    [InlineData("en", true)]
    public void Explicit_service_date_or_period_is_rendered_without_fallback(string language, bool hasPeriod)
    {
        var document = BuildFixture();
        document.ServicePeriodEnd = hasPeriod ? new DateOnly(2026, 7, 10) : null;
        var model = SnapshotReader.FromDocument(document, language: language);
        var labels = PdfLabels.For(language);

        var text = RenderText(model);

        Assert.DoesNotContain(WithoutWhitespace(labels.ServiceDateMatchesInvoiceDate), text);
        Assert.Contains(WithoutWhitespace(hasPeriod ? labels.ServicePeriod : labels.ServiceDate), text);
        Assert.Contains(document.ServiceDate!.Value.ToString("d", labels.Culture), text);
        if (hasPeriod)
        {
            Assert.Contains(document.ServicePeriodEnd!.Value.ToString("d", labels.Culture), text);
        }
    }

    private static string RenderText(InvoicePdfModel model)
    {
        using var pdf = PdfDocument.Open(InvoiceDocument.Render(model));
        return WithoutWhitespace(string.Concat(pdf.GetPages().Select(page => page.Text)));
    }

    private static string WithoutWhitespace(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

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
