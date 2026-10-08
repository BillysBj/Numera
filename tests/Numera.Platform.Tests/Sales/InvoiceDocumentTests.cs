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
    [MemberData(nameof(SupplyDateDocumentTypes))]
    public void Frozen_closing_text_renders_only_for_the_matching_document_type(
        string language, DocumentType type, bool isInvoice)
    {
        const string invoiceText = "InvoiceClosingFirst\nInvoiceClosingSecond";
        const string deliveryText = "DeliveryClosingFirst\r\nDeliveryClosingSecond";
        var document = BuildFixture();
        document.DocumentType = type;
        var issuer = System.Text.Json.Nodes.JsonNode.Parse(document.IssuerSnapshot!)!;
        issuer["invoiceFooterText"] = invoiceText;
        issuer["deliveryNoteFooterText"] = deliveryText;
        document.IssuerSnapshot = issuer.ToJsonString();

        var model = SnapshotReader.FromDocument(document, language: language);
        Assert.Equal(invoiceText, model.Issuer.InvoiceFooterText);
        Assert.Equal(deliveryText, model.Issuer.DeliveryNoteFooterText);

        var text = RenderText(model);
        Assert.Equal(isInvoice, text.Contains(WithoutWhitespace(invoiceText), StringComparison.Ordinal));
        Assert.Equal(type == DocumentType.Lieferschein,
            text.Contains(WithoutWhitespace(deliveryText), StringComparison.Ordinal));

        if (isInvoice || type == DocumentType.Lieferschein)
        {
            var closingText = WithoutWhitespace(isInvoice ? invoiceText : deliveryText);
            var precedingText = WithoutWhitespace(isInvoice ? model.Notes! : PdfLabels.For(language).RecipientSignature);
            Assert.True(text.IndexOf(closingText, StringComparison.Ordinal) > text.IndexOf(precedingText, StringComparison.Ordinal));

            using var pdf = PdfDocument.Open(InvoiceDocument.Render(model));
            var words = pdf.GetPages().SelectMany(page => page.GetWords()).ToList();
            var first = Assert.Single(words, word => word.Text == (isInvoice ? "InvoiceClosingFirst" : "DeliveryClosingFirst"));
            var second = Assert.Single(words, word => word.Text == (isInvoice ? "InvoiceClosingSecond" : "DeliveryClosingSecond"));
            Assert.True(first.BoundingBox.Bottom > second.BoundingBox.Top);
        }
    }

    [Theory]
    [InlineData(DocumentType.Rechnung)]
    [InlineData(DocumentType.Lieferschein)]
    public void Closing_text_snapshot_is_unchanged_after_company_profile_changes(DocumentType type)
    {
        var profile = new CompanyProfile
        {
            LegalName = "Muster GmbH",
            Address = new Address { Street = "Hauptstraße 1", PostalCode = "10115", City = "Berlin" },
            InvoiceFooterText = "FrozenInvoiceClosing",
            DeliveryNoteFooterText = "FrozenDeliveryClosing",
        };
        var document = BuildFixture();
        document.DocumentType = type;
        var serializeIssuer = typeof(Numera.Api.Endpoints.SalesDocumentEndpoints)
            .GetMethod("SerializeIssuer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        document.IssuerSnapshot = (string)serializeIssuer.Invoke(null, [profile])!;
        profile.InvoiceFooterText = "ChangedInvoiceClosing";
        profile.DeliveryNoteFooterText = "ChangedDeliveryClosing";

        var model = SnapshotReader.FromDocument(document);
        Assert.Equal("FrozenInvoiceClosing", model.Issuer.InvoiceFooterText);
        Assert.Equal("FrozenDeliveryClosing", model.Issuer.DeliveryNoteFooterText);
        var text = RenderText(model);
        Assert.Contains(type == DocumentType.Rechnung ? "FrozenInvoiceClosing" : "FrozenDeliveryClosing", text);
        Assert.DoesNotContain("ChangedInvoiceClosing", text);
        Assert.DoesNotContain("ChangedDeliveryClosing", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public void Missing_or_empty_closing_text_keeps_legacy_rendering(string? closingText)
    {
        var document = BuildFixture();
        var legacyModel = SnapshotReader.FromDocument(document);
        Assert.Null(legacyModel.Issuer.InvoiceFooterText);
        Assert.Null(legacyModel.Issuer.DeliveryNoteFooterText);

        var issuer = System.Text.Json.Nodes.JsonNode.Parse(document.IssuerSnapshot!)!;
        issuer["InvoiceFooterText"] = closingText;
        issuer["DeliveryNoteFooterText"] = closingText;
        document.IssuerSnapshot = issuer.ToJsonString();
        var model = SnapshotReader.FromDocument(document);

        Assert.Equal(closingText, model.Issuer.InvoiceFooterText);
        Assert.Equal(closingText, model.Issuer.DeliveryNoteFooterText);
        Assert.Equal(RenderText(legacyModel), RenderText(model));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Delivery_note_renders_items_and_notes_without_prices_taxes_or_payment_sections(string language)
    {
        var model = BuildPriceSectionFixture(language, DocumentType.Lieferschein);
        var labels = PdfLabels.For(language);

        var text = RenderText(model);

        Assert.Contains(language == "en" ? "DeliveryNote" : "Lieferschein", text);
        Assert.Contains(WithoutWhitespace(model.Recipient.Name!), text);
        Assert.Contains(WithoutWhitespace(model.DocumentNumber!), text);
        Assert.Contains(WithoutWhitespace(model.Notes!), text);
        Assert.Contains(WithoutWhitespace(labels.Item
            + (language == "en" ? "Description" : "Bezeichnung")
            + labels.Qty + (language == "en" ? "Unit" : "Einheit")), text);
        foreach (var line in model.Lines)
        {
            Assert.Contains(WithoutWhitespace(line.Name), text);
            Assert.Contains(InvoiceDocument.FormatNumber(line.Quantity, labels.Culture), text);
            if (line.Description is not null)
            {
                Assert.Contains(WithoutWhitespace(line.Description), text);
            }
        }
        Assert.Contains(language == "en" ? "hrs" : "Std.", text);
        Assert.Contains(language == "en" ? "pcs" : "Stk.", text);

        // VAT identifiers remain part of recipient metadata and the issuer imprint.
        Assert.Contains(WithoutWhitespace($"{labels.VatId}: {model.Recipient.VatId}"), text);
        var textWithoutVatIdLabel = text.Replace(WithoutWhitespace(labels.VatId), string.Empty);
        Assert.DoesNotContain(labels.Vat, textWithoutVatIdLabel, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            labels.UnitPrice, labels.Amount, labels.Total, labels.SubtotalNet,
            labels.VatBreakdown, labels.PayableBy, labels.BankDetails, labels.Iban,
            language == "en" ? "Sum" : "Summe",
            language == "en" ? "discount" : "Rabatt", "Skonto", "EUR", "€",
            model.Prepayments[0].AbschlagNumber,
        })
        {
            Assert.DoesNotContain(WithoutWhitespace(forbidden), textWithoutVatIdLabel, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var row in model.BreakdownRows.Where(row => row.ExemptionReasonText is not null))
        {
            Assert.DoesNotContain(WithoutWhitespace(row.ExemptionReasonText!), text);
        }
        foreach (var amount in new[] { 100m, 300m, 500m, model.TotalNet, model.TotalTax, model.TotalGross })
        {
            Assert.DoesNotContain(InvoiceDocument.FormatNumber(amount, labels.Culture), text);
        }
    }

    [Theory]
    [InlineData("de", false)]
    [InlineData("de", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public void Delivery_note_renders_receipt_confirmation_after_items_and_optional_notes(string language, bool hasNotes)
    {
        var model = BuildPriceSectionFixture(language, DocumentType.Lieferschein);
        model = model with { Notes = hasNotes ? model.Notes : null };
        var confirmation = WithoutWhitespace(language == "en"
            ? "Goods received complete and in good order:"
            : "Ware vollständig und einwandfrei erhalten:");
        var placeAndDate = WithoutWhitespace(language == "en" ? "Place, date" : "Ort, Datum");
        var signature = WithoutWhitespace(language == "en" ? "Signature (recipient)" : "Unterschrift Empfänger");

        var text = RenderText(model);

        Assert.Contains(confirmation, text);
        Assert.Contains(placeAndDate, text);
        Assert.Contains(signature, text);
        var precedingText = WithoutWhitespace(hasNotes ? model.Notes! : model.Lines[^1].Name);
        Assert.True(text.IndexOf(confirmation, StringComparison.Ordinal) > text.IndexOf(precedingText, StringComparison.Ordinal));
        Assert.True(text.IndexOf(placeAndDate, StringComparison.Ordinal) > text.IndexOf(confirmation, StringComparison.Ordinal));
        Assert.True(text.IndexOf(signature, StringComparison.Ordinal) > text.IndexOf(placeAndDate, StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> PricedDocumentTypes() =>
        from language in new[] { "de", "en" }
        from type in Enum.GetValues<DocumentType>()
        where type != DocumentType.Lieferschein
        select new object[] { language, type };

    [Theory]
    [MemberData(nameof(PricedDocumentTypes))]
    public void Other_document_types_keep_prices_totals_taxes_discounts_and_payment_sections(
        string language, DocumentType type)
    {
        var model = BuildPriceSectionFixture(language, type);
        var labels = PdfLabels.For(language);

        var text = RenderText(model);

        Assert.DoesNotContain(WithoutWhitespace(labels.ReceiptConfirmation), text);
        Assert.DoesNotContain(WithoutWhitespace(labels.PlaceAndDate), text);
        Assert.DoesNotContain(WithoutWhitespace(labels.RecipientSignature), text);
        Assert.Contains(WithoutWhitespace(labels.Item + labels.Description + labels.Qty
            + labels.UnitPrice + labels.Amount), text);
        foreach (var label in new[] { labels.SubtotalNet, labels.Total, labels.VatBreakdown,
            labels.PayableBy, labels.BankDetails, labels.Iban })
        {
            Assert.Contains(WithoutWhitespace(label), text, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var amount in new[] { 100m, 300m, 500m, model.TotalNet, model.TotalTax, model.TotalGross })
        {
            Assert.Contains(WithoutWhitespace($"{InvoiceDocument.FormatNumber(amount, labels.Culture)} €"), text);
        }
        Assert.Contains(WithoutWhitespace(language == "en" ? "less 12.5% discount" : "abzgl. 12,5% Rabatt"), text);
        Assert.Contains(language == "en" ? "Onpaymentby" : "BeiZahlungbis", text);
        Assert.Contains(model.Prepayments[0].AbschlagNumber, text);
        foreach (var row in model.BreakdownRows.Where(row => row.ExemptionReasonText is not null))
        {
            Assert.Contains(WithoutWhitespace(row.ExemptionReasonText!), text);
        }
    }

    private static InvoicePdfModel BuildPriceSectionFixture(string language, DocumentType type)
    {
        var document = BuildFixture();
        document.DocumentType = type;
        document.Lines[0].DiscountPercent = 12.5m;
        var model = SnapshotReader.FromDocument(document, language: language);
        return model with
        {
            Recipient = model.Recipient with { SkontoPercent = 2m, SkontoDays = 7 },
            Prepayments =
            [
                new InvoicePdfModel.PrepaymentRow
                {
                    LineNumber = 1,
                    AbschlagNumber = "AR-2026-00001",
                    AbschlagDate = new DateOnly(2026, 6, 1),
                    NetAmount = 50m,
                    VatAmount = 9.5m,
                    GrossAmount = 59.5m,
                },
            ],
            BreakdownRows =
            [
                .. model.BreakdownRows,
                new InvoicePdfModel.BreakdownRow
                {
                    ExemptionReasonText = "Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.",
                },
            ],
        };
    }

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
