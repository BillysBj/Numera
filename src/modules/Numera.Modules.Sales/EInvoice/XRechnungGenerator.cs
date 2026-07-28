using Numera.Modules.Sales.Pdf;

using s2industries.ZUGFeRD;

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// Serializes a finalized invoice's frozen <see cref="InvoicePdfModel"/> to the two
/// legally-accepted XRechnung syntaxes — UBL and CII — over the single
/// <see cref="EInvoiceMapper"/> descriptor.
/// </summary>
/// <remarks>
/// <para>
/// Every method builds ONE <see cref="InvoiceDescriptor"/> per invocation via
/// <see cref="EInvoiceMapper.ToDescriptor"/> and saves it; because UBL and CII come from the
/// SAME descriptor they provably carry identical values — this is the structural EINV-02
/// guarantee (one source, two serializations), not a reconciliation of two pipelines.
/// </para>
/// <para>
/// Pure: no DB, no HTTP, no side effects. The 05-03 service composes persistence + KoSIT
/// validation around this; the 05-04 ZUGFeRD PDF/A-3 embed reuses
/// <see cref="GenerateCiiForZugferd"/> so the hybrid carrier serializes through THIS exact
/// path, never a second one.
/// </para>
/// <para>
/// Upgrade seam: ZUGFeRD-csharp 18 is the last OSS major; FactoorSharp + XRechnung 4.0 later.
/// </para>
/// </remarks>
public static class XRechnungGenerator
{
    private const ZUGFeRDVersion Version = ZUGFeRDVersion.Version23;
    private const Profile XRechnungProfile = Profile.XRechnung;

    /// <summary>
    /// Generates the XRechnung <b>UBL</b> XML bytes for <paramref name="model"/>.
    /// </summary>
    public static byte[] GenerateUbl(InvoicePdfModel model) =>
        Save(model, ZUGFeRDFormats.UBL);

    /// <summary>
    /// Generates the XRechnung <b>CII</b> XML bytes for <paramref name="model"/>.
    /// </summary>
    public static byte[] GenerateCii(InvoicePdfModel model) =>
        Save(model, ZUGFeRDFormats.CII);

    /// <summary>
    /// The CII (Profile.XRechnung) bytes the 05-04 ZUGFeRD PDF/A-3 carrier embeds — the SAME
    /// serialization path as <see cref="GenerateCii"/>, exposed under an intent-revealing name
    /// so the hybrid PDF and the standalone CII can never diverge.
    /// </summary>
    public static byte[] GenerateCiiForZugferd(InvoicePdfModel model) =>
        GenerateCii(model);

    /// <summary>
    /// A sensible download file name for the given <paramref name="format"/>, e.g.
    /// <c>RE-2026-00001-ubl.xml</c> / <c>RE-2026-00001-cii.xml</c>.
    /// </summary>
    public static string XmlFileName(InvoicePdfModel model, EInvoiceFormat format)
    {
        ArgumentNullException.ThrowIfNull(model);

        var number = string.IsNullOrWhiteSpace(model.DocumentNumber) ? "rechnung" : model.DocumentNumber!;
        var suffix = format switch
        {
            EInvoiceFormat.XRechnungUbl => "ubl",
            EInvoiceFormat.XRechnungCii => "cii",
            EInvoiceFormat.ZugferdPdfA3 => "zugferd",
            _ => "xml",
        };

        return $"{number}-{suffix}.xml";
    }

    private static byte[] Save(InvoicePdfModel model, ZUGFeRDFormats format)
    {
        var descriptor = EInvoiceMapper.ToDescriptor(model);

        using var stream = new MemoryStream();
        descriptor.Save(stream, Version, XRechnungProfile, format);

        if (string.Equals(model.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
            || model.TotalTaxEur is not decimal totalTaxEur)
        {
            return stream.ToArray();
        }

        stream.Position = 0;
        var document = XDocument.Load(stream);
        InjectAccountingCurrencyTaxTotal(document, format, totalTaxEur);

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
        }))
        {
            document.Save(writer);
        }

        return output.ToArray();
    }

    private static void InjectAccountingCurrencyTaxTotal(
        XDocument document,
        ZUGFeRDFormats format,
        decimal totalTaxEur)
    {
        var amount = totalTaxEur.ToString("0.00", CultureInfo.InvariantCulture);

        if (format == ZUGFeRDFormats.UBL)
        {
            XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
            XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
            var ublTaxTotal = document.Root?.Elements(cac + "TaxTotal").Single();

            ublTaxTotal?.AddAfterSelf(
                new XElement(
                    cac + "TaxTotal",
                    new XElement(
                        cbc + "TaxAmount",
                        new XAttribute("currencyID", "EUR"),
                        amount)));
            return;
        }

        var ram = document.Root?.GetNamespaceOfPrefix("ram")
            ?? throw new InvalidOperationException("Generated CII has no ram namespace.");
        var settlement = document
            .Descendants(ram + "ApplicableHeaderTradeSettlement")
            .Single();
        var ciiTaxTotal = settlement.Descendants(ram + "TaxTotalAmount").Single();

        ciiTaxTotal.AddAfterSelf(
            new XElement(
                ram + "TaxTotalAmount",
                new XAttribute("currencyID", "EUR"),
                amount));
    }
}
