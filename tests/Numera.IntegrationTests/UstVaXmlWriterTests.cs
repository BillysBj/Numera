using System.Text;
using System.Xml.Linq;

using Numera.Api.Reporting;
using Numera.Api.Reporting.Elster;
using Numera.Modules.Ledger;
using Numera.Modules.Sales;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Pure serialization checks for the manual ELSTER USt-VA upload payload.</summary>
public sealed class UstVaXmlWriterTests
{
    private static readonly Encoding ElsterEncoding = CreateElsterEncoding();

    [Fact]
    public void Writes_v2026_anmeldungssteuern_root_and_iso_8859_15_declaration()
    {
        var bytes = UstVaXmlWriter.Write(NewReport(NewBaseLine("81", 100m)), NewProfile());
        var text = ElsterEncoding.GetString(bytes);
        var document = XDocument.Parse(text);
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("Anmeldungssteuern", root.Name.LocalName);
        Assert.Equal("http://finkonsens.de/elster/elsteranmeldung/ustva/v2026", root.Name.NamespaceName);
        Assert.Equal("2026", root.Attribute("version")?.Value);
        Assert.Contains("encoding=\"iso-8859-15\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standalone=\"no\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emits_only_the_kennziffern_present_in_the_report()
    {
        var document = WriteDocument(NewReport(
            NewBaseLine("81", 100m),
            NewBaseLine("86", 200m),
            NewTaxLine("83", 33m)));
        var declaration = UstvaElement(document);
        var names = declaration.Elements()
            .Where(element => element.Name.LocalName.StartsWith("Kz", StringComparison.Ordinal))
            .Select(element => element.Name.LocalName)
            .ToList();

        Assert.Equal(["Kz81", "Kz86", "Kz83"], names);
        Assert.DoesNotContain("Kz41", names);
    }

    [Fact]
    public void Formats_bases_as_integer_euros_and_kz83_with_two_invariant_decimals()
    {
        var document = WriteDocument(NewReport(
            NewBaseLine("81", 1234.99m),
            NewTaxLine("83", 234.5m)));
        var declaration = UstvaElement(document);

        Assert.Equal("1234", declaration.Element(declaration.Name.Namespace + "Kz81")?.Value);
        Assert.Equal("234.50", declaration.Element(declaration.Name.Namespace + "Kz83")?.Value);
    }

    [Fact]
    public void Writes_the_13_digit_number_converted_for_the_profiles_bundesland()
    {
        var document = WriteDocument(NewReport(NewTaxLine("83", 19m)));
        var declaration = UstvaElement(document);

        Assert.Equal("9151081508154", declaration.Element(declaration.Name.Namespace + "Steuernummer")?.Value);
    }

    [Theory]
    [InlineData(Bundesland.BadenWuerttemberg, "12/345/67890", "2812034567890")]
    [InlineData(Bundesland.Bayern, "151/815/08154", "9151081508154")]
    [InlineData(Bundesland.Hessen, "012 345 67890", "2612034567890")]
    [InlineData(Bundesland.NordrheinWestfalen, "123/4567/8901", "5123045678901")]
    public void Converts_the_state_specific_common_layouts(
        Bundesland bundesland,
        string localNumber,
        string expected) =>
        Assert.Equal(expected, SteuernummerConverter.Convert(localNumber, bundesland));

    [Fact]
    public void Rejects_a_vat_id_in_place_of_the_local_steuernummer()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SteuernummerConverter.Convert("DE811907980", Bundesland.Bayern));

        Assert.Contains("USt-IdNr", exception.Message, StringComparison.Ordinal);
    }

    private static XDocument WriteDocument(UstVaReport report)
    {
        var bytes = UstVaXmlWriter.Write(report, NewProfile());
        return XDocument.Parse(ElsterEncoding.GetString(bytes));
    }

    private static XElement UstvaElement(XDocument document)
    {
        var root = Assert.IsType<XElement>(document.Root);
        var element = root
            .Element(root.Name.Namespace + "Steuerfall")?
            .Element(root.Name.Namespace + "Umsatzsteuervoranmeldung");
        return Assert.IsType<XElement>(element);
    }

    private static UstVaReport NewReport(params UstVaLine[] lines) => new(
        Jahr: 2026,
        Zeitraum: "03",
        Besteuerungsart: Besteuerungsart.Soll,
        IsFestgeschrieben: true,
        Lines: lines,
        Zahllast: lines.Where(line => line.Kz == "83").Sum(line => line.Steuer ?? 0m),
        Hinweis: null,
        IsKleinunternehmer: false);

    private static UstVaLine NewBaseLine(string kz, decimal value) =>
        new(kz, $"Kz {kz}", value, Steuer: null, IsComputed: false);

    private static UstVaLine NewTaxLine(string kz, decimal value) =>
        new(kz, $"Kz {kz}", Bemessungsgrundlage: null, value, IsComputed: true);

    private static CompanyProfile NewProfile() => new()
    {
        TenantId = Guid.CreateVersion7(),
        LegalName = "Muster GmbH",
        Address = new Address
        {
            Street = "Musterstr. 1",
            PostalCode = "80331",
            City = "München",
            CountryCode = "DE",
        },
        TaxNumber = "151/815/08154",
        Bundesland = Bundesland.Bayern,
    };

    private static Encoding CreateElsterEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("ISO-8859-15");
    }
}
