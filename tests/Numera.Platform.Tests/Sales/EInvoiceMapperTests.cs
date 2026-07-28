using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using Xunit;

namespace Numera.Platform.Tests.Sales;

/// <summary>
/// Golden-file mapper suite for the outbound e-invoice layer
/// (<see cref="EInvoiceMapper"/> → <see cref="XRechnungGenerator"/>). Pure unit — no DB, no
/// KoSIT (that is the 05-02/05-03 gate): each VAT scenario is built as an
/// <see cref="InvoicePdfModel"/> fixture, serialized to XRechnung UBL + CII, and asserted on
/// the emitted XML by EN 16931 element (local-name matched, so a library minor bump does not
/// thrash the suite).
/// </summary>
/// <remarks>
/// The load-bearing proof is that UBL and CII — built from the SAME descriptor — carry
/// identical totals + per-category tax (the single-source-of-truth behind EINV-02), plus the
/// correct UNCL5305 category / VATEX exemption / frozen Pflichttext and the G1-G4 CIUS fields
/// (BT-10, BT-34/49 scheme EM, BG-6, BT-81).
/// </remarks>
public class EInvoiceMapperTests
{
    private const string ReverseChargePflichttext =
        "Steuerschuldnerschaft des Leistungsempfängers (§13b UStG)";
    private const string KleinunternehmerPflichttext =
        "Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG";

    // ---------------------------------------------------------------- Scenario 1: standard rate

    [Fact]
    public void Standard_rate_maps_category_S_and_transcribes_totals()
    {
        var model = Model(
            lines:
            [
                Line(1, "Beratung", 3m, "HUR", 100m, 300m, TaxCategory.S, 19m),
                Line(2, "Material", 10m, "C62", 10m, 100m, TaxCategory.S, 7m),
            ],
            rows:
            [
                Row(TaxCategory.S, 19m, 300m, 57m),
                Row(TaxCategory.S, 7m, 100m, 7m),
            ],
            net: 400m, tax: 64m, gross: 464m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        // Category S on both syntaxes.
        Assert.Contains("S", UblCategories(ubl));
        Assert.Contains("S", Vals(cii, "CategoryCode"));

        // Totals equal the FROZEN values, and TaxBasis/TaxTotal/GrandTotal == Net/Tax/Gross.
        Assert.Equal(400m, Dec(MonetaryChild(ubl, "TaxExclusiveAmount")));
        Assert.Equal(464m, Dec(MonetaryChild(ubl, "TaxInclusiveAmount")));
        Assert.Equal(64m, Dec(UblDocumentTaxAmount(ubl)));

        Assert.Equal(400m, Dec(CiiSummation(cii, "TaxBasisTotalAmount")));
        Assert.Equal(64m, Dec(CiiSummation(cii, "TaxTotalAmount")));
        Assert.Equal(464m, Dec(CiiSummation(cii, "GrandTotalAmount")));
    }

    // ---------------------------------------------------------------- Scenario 2: reverse charge

    [Fact]
    public void Reverse_charge_AE_maps_category_exemption_and_zero_tax()
    {
        var model = Model(
            lines: [Line(1, "Bauleistung", 1m, "C62", 500m, 500m, TaxCategory.AE, 0m)],
            rows: [Row(TaxCategory.AE, 0m, 500m, 0m, "VATEX-EU-AE", ReverseChargePflichttext)],
            net: 500m, tax: 0m, gross: 500m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        Assert.Contains("AE", UblCategories(ubl));
        Assert.Contains("AE", Vals(cii, "CategoryCode"));

        // VATEX code + the frozen Pflichttext transcribed verbatim, on both syntaxes.
        Assert.Contains("VATEX-EU-AE", Vals(ubl, "TaxExemptionReasonCode"));
        Assert.Contains("VATEX-EU-AE", Vals(cii, "ExemptionReasonCode"));
        Assert.Contains(ReverseChargePflichttext, Vals(ubl, "TaxExemptionReason"));
        Assert.Contains(ReverseChargePflichttext, Vals(cii, "ExemptionReason"));

        Assert.Equal(0m, Dec(UblDocumentTaxAmount(ubl)));
        Assert.Equal(0m, Dec(CiiSummation(cii, "TaxTotalAmount")));
    }

    // ---------------------------------------------------------------- Scenario 3: Kleinunternehmer

    [Fact]
    public void Kleinunternehmer_paragraph19_maps_category_E_zero_tax_and_note()
    {
        var model = Model(
            lines: [Line(1, "Leistung", 1m, "C62", 200m, 200m, TaxCategory.E, 0m)],
            rows: [Row(TaxCategory.E, 0m, 200m, 0m, "VATEX-EU-D", KleinunternehmerPflichttext)],
            net: 200m, tax: 0m, gross: 200m,
            isKleinunternehmer: true);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        Assert.Contains("E", UblCategories(ubl));
        Assert.Contains("E", Vals(cii, "CategoryCode"));
        Assert.Contains("VATEX-EU-D", Vals(cii, "ExemptionReasonCode"));
        Assert.Contains(KleinunternehmerPflichttext, Vals(cii, "ExemptionReason"));
        Assert.Equal(0m, Dec(CiiSummation(cii, "TaxTotalAmount")));
    }

    // ---------------------------------------------------------------- Scenario 4: K / G / Z

    [Theory]
    [InlineData(TaxCategory.K, "VATEX-EU-IC", "Innergemeinschaftliche Lieferung")]
    [InlineData(TaxCategory.G, "VATEX-EU-G", "Ausfuhrlieferung")]
    [InlineData(TaxCategory.Z, null, null)]
    public void Zero_rate_categories_map_code_and_reason(
        TaxCategory category, string? expectedCode, string? reasonText)
    {
        var expectedLetter = category.ToString();
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 250m, 250m, category, 0m)],
            rows: [Row(category, 0m, 250m, 0m, expectedCode, reasonText)],
            net: 250m, tax: 0m, gross: 250m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        Assert.Contains(expectedLetter, UblCategories(ubl));
        Assert.Contains(expectedLetter, Vals(cii, "CategoryCode"));

        if (expectedCode is not null)
        {
            Assert.Contains(expectedCode, Vals(cii, "ExemptionReasonCode"));
            Assert.Contains(expectedCode, Vals(ubl, "TaxExemptionReasonCode"));
        }

        Assert.Equal(0m, Dec(CiiSummation(cii, "TaxTotalAmount")));
    }

    // ---------------------------------------------------------------- Scenario 5: CIUS gaps G1-G4

    [Fact]
    public void G1_buyer_reference_is_defaulted_when_absent()
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m,
            buyerReference: null);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        Assert.Equal(EInvoiceMapper.DefaultBuyerReference, Vals(ubl, "BuyerReference").Single());
        Assert.Equal(EInvoiceMapper.DefaultBuyerReference, Vals(cii, "BuyerReference").Single());
    }

    [Fact]
    public void G1_buyer_reference_is_preserved_when_present()
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m,
            buyerReference: "LW-991-2026");

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));

        Assert.Equal("LW-991-2026", Vals(ubl, "BuyerReference").Single());
    }

    [Fact]
    public void G2_seller_and_buyer_electronic_addresses_carry_scheme_EM()
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        // UBL: two EndpointID (seller + buyer), each with EAS scheme EM.
        var ublSchemes = Local(ubl, "EndpointID")
            .Select(e => e.Attribute("schemeID")?.Value)
            .ToList();
        Assert.Equal(2, ublSchemes.Count);
        Assert.All(ublSchemes, s => Assert.Equal("EM", s));

        // CII: the two electronic addresses (URIID with schemeID) carry EM.
        var ciiSchemes = Local(cii, "URIID")
            .Select(e => e.Attribute("schemeID")?.Value)
            .Where(s => s is not null)
            .ToList();
        Assert.Equal(2, ciiSchemes.Count);
        Assert.All(ciiSchemes, s => Assert.Equal("EM", s));
    }

    [Fact]
    public void G3_seller_contact_group_BG6_is_populated()
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        // UBL seller Contact (BT-41 name / BT-42 phone / BT-43 email).
        var contact = Local(ubl, "Contact").First();
        Assert.Equal("Max Muster", Child(contact, "Name"));
        Assert.Equal("+49 30 123456", Child(contact, "Telephone"));
        Assert.Equal("info@muster.de", Child(contact, "ElectronicMail"));

        // CII BG-6 seller contact.
        var tradeContact = Local(cii, "DefinedTradeContact").First();
        Assert.Equal("Max Muster", Child(tradeContact, "PersonName"));
    }

    [Theory]
    [InlineData(true, "58")]   // IBAN present → SEPA credit transfer (58)
    [InlineData(false, "30")]  // no IBAN → credit transfer non-SEPA (30)
    public void G4_payment_means_code_depends_on_iban(bool withIban, string expectedCode)
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m,
            withIban: withIban);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));

        Assert.Equal(expectedCode, Vals(ubl, "PaymentMeansCode").Single());
    }

    // ---------------------------------------------------------------- Single-source proof

    [Fact]
    public void Ubl_and_cii_carry_identical_totals_and_per_category_tax()
    {
        var model = Model(
            lines:
            [
                Line(1, "Beratung", 3m, "HUR", 100m, 300m, TaxCategory.S, 19m),
                Line(2, "Bau §13b", 1m, "C62", 500m, 500m, TaxCategory.AE, 0m),
            ],
            rows:
            [
                Row(TaxCategory.S, 19m, 300m, 57m),
                Row(TaxCategory.AE, 0m, 500m, 0m, "VATEX-EU-AE", ReverseChargePflichttext),
            ],
            net: 800m, tax: 57m, gross: 857m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        // Document-level totals identical across the two serializations.
        Assert.Equal(Dec(MonetaryChild(ubl, "LineExtensionAmount")), Dec(CiiSummation(cii, "LineTotalAmount")));
        Assert.Equal(Dec(MonetaryChild(ubl, "TaxExclusiveAmount")), Dec(CiiSummation(cii, "TaxBasisTotalAmount")));
        Assert.Equal(Dec(MonetaryChild(ubl, "TaxInclusiveAmount")), Dec(CiiSummation(cii, "GrandTotalAmount")));
        Assert.Equal(Dec(MonetaryChild(ubl, "PayableAmount")), Dec(CiiSummation(cii, "DuePayableAmount")));
        Assert.Equal(Dec(UblDocumentTaxAmount(ubl)), Dec(CiiSummation(cii, "TaxTotalAmount")));

        // Per-(category, rate) tax amounts identical.
        Assert.Equal(UblPerCategoryTax(ubl), CiiPerCategoryTax(cii));
    }

    [Fact]
    public void Business_process_is_set_on_both_syntaxes()
    {
        var model = Model(
            lines: [Line(1, "Pos", 1m, "C62", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m, tax: 19m, gross: 119m);

        var ublText = Encoding.UTF8.GetString(XRechnungGenerator.GenerateUbl(model));
        var ciiText = Encoding.UTF8.GetString(XRechnungGenerator.GenerateCii(model));

        Assert.Contains(EInvoiceMapper.BusinessProcess, ublText);
        Assert.Contains(EInvoiceMapper.BusinessProcess, ciiText);
    }

    [Fact]
    public void Schlussrechnung_emits_frozen_BT113_and_residual_BT115_in_both_syntaxes()
    {
        var prepayments = new[]
        {
            new InvoicePdfModel.PrepaymentRow
            {
                LineNumber = 1,
                AbschlagNumber = "AR-2026-00001",
                AbschlagDate = new DateOnly(2026, 5, 15),
                NetAmount = 200m,
                VatAmount = 38m,
                GrossAmount = 238m,
            },
            new InvoicePdfModel.PrepaymentRow
            {
                LineNumber = 2,
                AbschlagNumber = "AR-2026-00002",
                AbschlagDate = new DateOnly(2026, 6, 15),
                NetAmount = 100m,
                VatAmount = 19m,
                GrossAmount = 119m,
            },
        };
        var model = Model(
            lines: [Line(1, "Gesamtprojekt", 1m, "C62", 1000m, 1000m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 1000m, 190m)],
            net: 1000m,
            tax: 190m,
            gross: 1190m,
            prepayments: prepayments,
            amountDue: 833m);

        var ubl = Parse(XRechnungGenerator.GenerateUbl(model));
        var cii = Parse(XRechnungGenerator.GenerateCii(model));

        Assert.Equal(357m, Dec(MonetaryChild(ubl, "PrepaidAmount")));
        Assert.Equal(357m, Dec(CiiSummation(cii, "TotalPrepaidAmount")));
        Assert.Equal(833m, Dec(MonetaryChild(ubl, "PayableAmount")));
        Assert.Equal(833m, Dec(CiiSummation(cii, "DuePayableAmount")));
        Assert.Equal(1190m - 357m, Dec(MonetaryChild(ubl, "PayableAmount")));
        Assert.Equal(UblPerCategoryTax(ubl), CiiPerCategoryTax(cii));
        Assert.Equal(190m, Dec(UblDocumentTaxAmount(ubl)));
    }

    // ================================================================ Fixture builders

    private static InvoicePdfModel.LineRow Line(
        int number, string name, decimal qty, string unit,
        decimal unitPrice, decimal lineNet, TaxCategory category, decimal rate) => new()
        {
            LineNumber = number,
            Name = name,
            Quantity = qty,
            UnitCode = unit,
            NetUnitPrice = unitPrice,
            LineNetAmount = lineNet,
            TaxCategory = category,
            VatRatePercent = rate,
        };

    private static InvoicePdfModel.BreakdownRow Row(
        TaxCategory category, decimal rate, decimal @base, decimal tax,
        string? exemptionCode = null, string? exemptionText = null) => new()
        {
            TaxCategory = category,
            VatRatePercent = rate,
            TaxableBase = @base,
            TaxAmount = tax,
            ExemptionReasonCode = exemptionCode,
            ExemptionReasonText = exemptionText,
        };

    private static InvoicePdfModel Model(
        IReadOnlyList<InvoicePdfModel.LineRow> lines,
        IReadOnlyList<InvoicePdfModel.BreakdownRow> rows,
        decimal net, decimal tax, decimal gross,
        string? buyerReference = "LW-123",
        bool withIban = true,
        bool isKleinunternehmer = false,
        IReadOnlyList<InvoicePdfModel.PrepaymentRow>? prepayments = null,
        decimal? amountDue = null) => new()
        {
            DocumentNumber = "RE-2026-00042",
            DocumentDate = new DateOnly(2026, 7, 13),
            ServiceDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 27),
            Currency = "EUR",
            BuyerReference = buyerReference,
            Notes = "Vielen Dank.",
            IsKleinunternehmer = isKleinunternehmer,
            ReverseCharge = lines.Any(l => l.TaxCategory == TaxCategory.AE),
            Issuer = new InvoicePdfModel.IssuerBlock
            {
                LegalName = "Muster GmbH",
                Address = new InvoicePdfModel.AddressBlock
                {
                    Street = "Hauptstraße 1",
                    PostalCode = "10115",
                    City = "Berlin",
                    CountryCode = "DE",
                },
                VatId = "DE123456789",
                IsKleinunternehmer = isKleinunternehmer,
                Iban = withIban ? "DE02120300000000202051" : null,
                Bic = withIban ? "BYLADEM1001" : null,
                BankName = withIban ? "Musterbank" : null,
                ManagingDirector = "Max Muster",
                ContactEmail = "info@muster.de",
                ContactPhone = "+49 30 123456",
            },
            Recipient = new InvoicePdfModel.RecipientBlock
            {
                Name = "Kunde AG",
                BillingAddress = new InvoicePdfModel.AddressBlock
                {
                    Street = "Kundenweg 5",
                    PostalCode = "80331",
                    City = "München",
                    CountryCode = "DE",
                },
                VatId = "DE987654321",
                Email = "kunde@example.com",
            },
            Lines = lines,
            BreakdownRows = rows,
            Prepayments = prepayments ?? [],
            TotalNet = net,
            TotalTax = tax,
            TotalGross = gross,
            AmountDue = amountDue ?? gross,
        };

    // ================================================================ XML helpers (local-name based)

    private static XDocument Parse(byte[] xml) => XDocument.Parse(Encoding.UTF8.GetString(xml));

    private static IEnumerable<XElement> Local(XDocument doc, string name) =>
        doc.Descendants().Where(e => e.Name.LocalName == name);

    private static IEnumerable<string> Vals(XDocument doc, string name) =>
        Local(doc, name).Select(e => e.Value);

    private static string Child(XElement parent, string name) =>
        parent.Elements().First(e => e.Name.LocalName == name).Value;

    private static decimal Dec(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    // UBL category letters live in a TaxCategory group's ID child (line + breakdown level).
    private static IReadOnlyList<string> UblCategories(XDocument ubl) =>
        [.. Local(ubl, "TaxCategory").Select(c => Child(c, "ID"))];

    private static string MonetaryChild(XDocument ubl, string child) =>
        Child(Local(ubl, "LegalMonetaryTotal").Single(), child);

    // The document-level TaxTotal is a direct child of the Invoice root (not the line ones).
    private static string UblDocumentTaxAmount(XDocument ubl) =>
        Child(ubl.Root!.Elements().First(e => e.Name.LocalName == "TaxTotal"), "TaxAmount");

    private static string CiiSummation(XDocument cii, string child) =>
        Child(Local(cii, "SpecifiedTradeSettlementHeaderMonetarySummation").Single(), child);

    private static Dictionary<string, decimal> UblPerCategoryTax(XDocument ubl) =>
        Local(ubl, "TaxSubtotal").ToDictionary(
            sub =>
            {
                var cat = sub.Descendants().First(e => e.Name.LocalName == "TaxCategory");
                return $"{Child(cat, "ID")}@{Dec(Child(cat, "Percent"))}";
            },
            sub => Dec(sub.Elements().First(e => e.Name.LocalName == "TaxAmount").Value));

    private static Dictionary<string, decimal> CiiPerCategoryTax(XDocument cii) =>
        // Restrict to the document-level BG-23 rows (which carry CalculatedAmount); the
        // line-level ApplicableTradeTax groups have only category + rate, no amounts.
        Local(cii, "ApplicableTradeTax")
            .Where(t => t.Elements().Any(e => e.Name.LocalName == "CalculatedAmount"))
            .ToDictionary(
                t => $"{Child(t, "CategoryCode")}@{Dec(Child(t, "RateApplicablePercent"))}",
                t => Dec(Child(t, "CalculatedAmount")));
}
