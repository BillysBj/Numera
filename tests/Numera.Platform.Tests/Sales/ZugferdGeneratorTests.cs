using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using QuestPDF.Infrastructure;

using Xunit;

namespace Numera.Platform.Tests.Sales;

/// <summary>
/// Value-identity suite for the ZUGFeRD / Factur-X hybrid (<see cref="ZugferdGenerator"/>). Pure
/// unit — no DB, no KoSIT: an <see cref="InvoicePdfModel"/> fixture is rendered to a ZUGFeRD
/// PDF/A-3b and the embedded <c>factur-x.xml</c> is extracted back out and asserted.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing proof (EINV-02) is that the embedded CII is byte-for-byte
/// <see cref="XRechnungGenerator.GenerateCiiForZugferd"/> over the SAME model that drove the printed
/// page, so the human-readable page and the machine-readable XML carry identical amounts. We extract
/// the embedded file by scanning the PDF's <c>stream…endstream</c> bodies (FlateDecode-inflating
/// each) and picking the one that is the EN 16931 CII — no PDF library needed (QuestPDF's
/// <c>DocumentOperation</c> writes; ZUGFeRD-csharp 18 cannot read a PDF).
/// </para>
/// <para>
/// PDF/A CONFORMANCE: this asserts the PDF/A-3b XMP marker (<c>pdfaid:part</c>=3) is present. Full
/// PDF/A validation (veraPDF) is NOT wired into CI yet — TODO(05-verify): add a veraPDF CI step over
/// a generated ZUGFeRD sample (RESEARCH Pitfall 5). We deliberately do not block the plan on a
/// heavyweight external tool; the marker + the value-identity extraction give strong coverage.
/// </para>
/// </remarks>
public class ZugferdGeneratorTests
{
    private const string ReverseChargePflichttext =
        "Steuerschuldnerschaft des Leistungsempfängers (§13b UStG)";
    private const string KleinunternehmerPflichttext =
        "Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG";

    static ZugferdGeneratorTests()
    {
        // REQUIRED before any GeneratePdf / DocumentOperation (Pitfall 1). Idempotent.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static TheoryData<string> Fixtures() => new() { "mixed", "kleinunternehmer" };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Output_is_a_pdf_larger_than_the_plain_render(string fixture)
    {
        var model = Model(fixture);

        var zugferd = ZugferdGenerator.Generate(model);
        var plain = InvoiceDocument.Render(model);

        Assert.Equal("%PDF"u8.ToArray(), zugferd[..4]);
        Assert.True(
            zugferd.Length > plain.Length,
            $"ZUGFeRD PDF ({zugferd.Length} B) should exceed the plain §14 render ({plain.Length} B).");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Embeds_factur_x_with_source_relationship_and_pdfa_marker(string fixture)
    {
        var model = Model(fixture);

        var zugferd = ZugferdGenerator.Generate(model);

        // The embedded-file infrastructure + AF Source relationship + the fixed factur-x.xml name.
        Assert.True(ContainsToken(zugferd, "EmbeddedFile"), "Missing /EmbeddedFile.");
        Assert.True(ContainsToken(zugferd, "AFRelationship"), "Missing /AFRelationship (AF/PDF-A3).");
        Assert.True(ContainsToken(zugferd, "factur-x.xml"), "Missing the factur-x.xml file name.");

        // PDF/A-3b conformance marker (veraPDF is a documented follow-up; see class remarks).
        Assert.True(ContainsToken(zugferd, "pdfaid"), "Missing the PDF/A (pdfaid) XMP marker.");
        Assert.True(
            ContainsToken(zugferd, "part>3") || ContainsToken(zugferd, "part=\"3\""),
            "PDF/A conformance is not part 3 (PDF/A-3).");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Embedded_cii_is_byte_identical_to_the_standalone_cii(string fixture)
    {
        var model = Model(fixture);

        var zugferd = ZugferdGenerator.Generate(model);
        var embedded = ExtractEmbeddedCii(zugferd);
        var standalone = XRechnungGenerator.GenerateCiiForZugferd(model);

        // EINV-02: the embedded factur-x.xml is the SAME serialization the standalone XRechnung CII
        // uses — one frozen model, one descriptor, one path — so it is byte-for-byte identical.
        Assert.Equal(standalone, embedded);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Embedded_xml_values_equal_the_printed_model_values(string fixture)
    {
        var model = Model(fixture);

        var zugferd = ZugferdGenerator.Generate(model);
        var cii = XDocument.Parse(Encoding.UTF8.GetString(ExtractEmbeddedCii(zugferd)));

        // The printed page's totals (from the model) == the embedded XML's summation (EINV-02).
        Assert.Equal(model.TotalNet, Dec(CiiSummation(cii, "TaxBasisTotalAmount")));
        Assert.Equal(model.TotalTax, Dec(CiiSummation(cii, "TaxTotalAmount")));
        Assert.Equal(model.TotalGross, Dec(CiiSummation(cii, "GrandTotalAmount")));
        Assert.Equal(model.AmountDue, Dec(CiiSummation(cii, "DuePayableAmount")));
    }

    // ================================================================ Embedded-file extraction

    // Extracts the embedded EN 16931 CII (rsm:CrossIndustryInvoice) from the ZUGFeRD PDF by scanning
    // every stream body — raw and FlateDecode-inflated — and returning the one that is the CII.
    private static byte[] ExtractEmbeddedCii(byte[] pdf)
    {
        foreach (var candidate in StreamCandidates(pdf))
        {
            if (IndexOf(candidate, "CrossIndustryInvoice"u8, 0) >= 0)
            {
                return candidate;
            }
        }

        throw new Xunit.Sdk.XunitException("No embedded CII (CrossIndustryInvoice) stream found in the PDF.");
    }

    // True when the ASCII token appears in the raw PDF or in any inflated stream body (dictionary
    // entries may live in a compressed object stream).
    private static bool ContainsToken(byte[] pdf, string token)
    {
        var needle = Encoding.ASCII.GetBytes(token);
        if (IndexOf(pdf, needle, 0) >= 0)
        {
            return true;
        }

        foreach (var candidate in StreamCandidates(pdf))
        {
            if (IndexOf(candidate, needle, 0) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<byte[]> StreamCandidates(byte[] pdf)
    {
        var streamKw = "stream"u8.ToArray();
        var endKw = "endstream"u8.ToArray();
        var idx = 0;

        while (true)
        {
            var s = IndexOf(pdf, streamKw, idx);
            if (s < 0)
            {
                yield break;
            }

            var dataStart = s + streamKw.Length;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\r')
            {
                dataStart++;
            }

            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\n')
            {
                dataStart++;
            }

            var e = IndexOf(pdf, endKw, dataStart);
            if (e < 0)
            {
                yield break;
            }

            var body = pdf[dataStart..e];
            yield return body;

            var inflated = TryInflate(body);
            if (inflated is not null)
            {
                yield return inflated;
            }

            idx = e + endKw.Length;
        }
    }

    private static byte[]? TryInflate(byte[] data)
    {
        // Try zlib (2-byte header) then raw deflate — PDF FlateDecode is zlib-wrapped.
        foreach (var skip in new[] { 2, 0 })
        {
            if (data.Length <= skip)
            {
                continue;
            }

            try
            {
                using var input = new MemoryStream(data, skip, data.Length - skip);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                if (output.Length > 0)
                {
                    return output.ToArray();
                }
            }
            catch (InvalidDataException)
            {
                // Not this filter/offset; try the next.
            }
        }

        return null;
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int start)
    {
        for (var i = start; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    // ================================================================ CII total helpers

    private static string CiiSummation(XDocument cii, string child) =>
        cii.Descendants()
            .First(e => e.Name.LocalName == "SpecifiedTradeSettlementHeaderMonetarySummation")
            .Elements()
            .First(e => e.Name.LocalName == child)
            .Value;

    private static decimal Dec(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    // ================================================================ Fixtures

    private static InvoicePdfModel Model(string fixture) => fixture switch
    {
        "mixed" => Build(
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
            net: 400m, tax: 64m, gross: 464m,
            isKleinunternehmer: false),

        "kleinunternehmer" => Build(
            lines: [Line(1, "Leistung", 1m, "C62", 200m, 200m, TaxCategory.E, 0m)],
            rows: [Row(TaxCategory.E, 0m, 200m, 0m, "VATEX-EU-D", KleinunternehmerPflichttext)],
            net: 200m, tax: 0m, gross: 200m,
            isKleinunternehmer: true),

        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown fixture."),
    };

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

    private static InvoicePdfModel Build(
        IReadOnlyList<InvoicePdfModel.LineRow> lines,
        IReadOnlyList<InvoicePdfModel.BreakdownRow> rows,
        decimal net, decimal tax, decimal gross,
        bool isKleinunternehmer) => new()
        {
            DocumentNumber = "RE-2026-00042",
            DocumentDate = new DateOnly(2026, 7, 13),
            ServiceDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 27),
            Currency = "EUR",
            BuyerReference = "LW-991-2026",
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
                Iban = "DE02120300000000202051",
                Bic = "BYLADEM1001",
                BankName = "Musterbank",
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
            TotalNet = net,
            TotalTax = tax,
            TotalGross = gross,
            AmountDue = gross,
        };
}
