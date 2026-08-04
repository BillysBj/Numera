using System.Globalization;
using System.Text;
using System.Xml;

using Numera.Modules.Sales;

namespace Numera.Api.Reporting.Elster;

/// <summary>Writes a bare ELSTER USt-VA Anmeldungssteuern Nutzdaten payload.</summary>
public static class UstVaXmlWriter
{
    private static readonly HashSet<string> ValidPeriods =
    [
        "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12",
        "41", "42", "43", "44",
    ];

    private static readonly Encoding ElsterEncoding = CreateElsterEncoding();

    /// <summary>
    /// Serializes a computed USt-VA as ISO-8859-15 Nutzdaten without an ERiC
    /// <c>Elster</c>/<c>TransferHeader</c> envelope.
    /// </summary>
    public static byte[] Write(UstVaReport report, CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(profile);

        if (report.IsKleinunternehmer || profile.IsKleinunternehmer)
        {
            throw new InvalidOperationException(
                "Kleinunternehmer do not file a USt-VA; no ELSTER VAT payload can be generated.");
        }

        if (report.Jahr is < 1000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(report), "The ELSTER report year must have four digits.");
        }

        if (!ValidPeriods.Contains(report.Zeitraum))
        {
            throw new ArgumentException(
                "The ELSTER period must be a month (01-12) or quarter (41-44).",
                nameof(report));
        }

        var steuernummer = SteuernummerConverter.Convert(profile.TaxNumber, profile.Bundesland);
        var xmlNamespace = $"http://finkonsens.de/elster/elsteranmeldung/ustva/v{report.Jahr}";

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = ElsterEncoding,
            Indent = true,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        }))
        {
            writer.WriteStartDocument(standalone: false);
            writer.WriteStartElement("Anmeldungssteuern", xmlNamespace);
            writer.WriteAttributeString("version", report.Jahr.ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString(
                "Erstellungsdatum",
                xmlNamespace,
                DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            writer.WriteStartElement("Steuerfall", xmlNamespace);
            writer.WriteStartElement("Umsatzsteuervoranmeldung", xmlNamespace);
            writer.WriteElementString("Jahr", xmlNamespace, report.Jahr.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("Zeitraum", xmlNamespace, report.Zeitraum);
            writer.WriteElementString("Steuernummer", xmlNamespace, steuernummer);

            WriteKennziffern(writer, xmlNamespace, report.Lines);

            writer.WriteEndElement(); // Umsatzsteuervoranmeldung
            writer.WriteEndElement(); // Steuerfall
            writer.WriteEndElement(); // Anmeldungssteuern
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteKennziffern(
        XmlWriter writer,
        string xmlNamespace,
        IReadOnlyList<UstVaLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Kz) || !line.Kz.All(IsAsciiDigit))
            {
                throw new ArgumentException("Every ELSTER Kennziffer must contain only digits.", nameof(lines));
            }

            if (!emitted.Add(line.Kz))
            {
                throw new ArgumentException($"Kennziffer {line.Kz} occurs more than once.", nameof(lines));
            }

            if (line.Bemessungsgrundlage is not null && line.Steuer is not null)
            {
                throw new ArgumentException(
                    $"Kennziffer {line.Kz} cannot contain both a base and a tax value.",
                    nameof(lines));
            }

            string? value = line.Bemessungsgrundlage switch
            {
                { } basis => decimal.Truncate(basis).ToString("0", CultureInfo.InvariantCulture),
                null when line.Steuer is { } tax => tax.ToString("0.00", CultureInfo.InvariantCulture),
                _ => null,
            };

            // Missing values are not represented by empty elements. Adding a new Kz to
            // the report model therefore remains an additive XML change.
            if (value is not null)
            {
                writer.WriteElementString($"Kz{line.Kz}", xmlNamespace, value);
            }
        }
    }

    private static Encoding CreateElsterEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("ISO-8859-15");
    }

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
