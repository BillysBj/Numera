using Numera.Modules.Sales.Pdf;

using QuestPDF.Fluent;

namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// Produces the ZUGFeRD / Factur-X hybrid (EINV-02): the §14 invoice rendered as a PDF/A-3b that
/// carries the EN 16931 / XRECHNUNG-profile CII XML embedded as a <c>factur-x.xml</c> attachment
/// (relationship <c>Source</c>) with the ZUGFeRD conformance XMP metadata.
/// </summary>
/// <remarks>
/// <para>
/// VALUE IDENTITY IS STRUCTURAL (EINV-02): the printed page and the embedded XML derive from ONE
/// <see cref="InvoicePdfModel"/>. The PDF base is <see cref="InvoiceDocument.RenderPdfA"/> over that
/// model; the embedded CII is <see cref="XRechnungGenerator.GenerateCiiForZugferd"/> over the SAME
/// model — the identical <c>InvoiceDescriptor</c> serialization the standalone XRechnung CII uses.
/// So the amounts in the human-readable page and the machine-readable XML are provably the same,
/// not reconciled after the fact.
/// </para>
/// <para>
/// PDF/A-3b + the attachment + the XMP are all produced by CORE QuestPDF 2026.7.1
/// (<see cref="DocumentOperation"/>) — NO extra PDF package, NO iText/AGPL.
/// <see cref="DocumentOperation"/> is file-based (qpdf under the hood), so this writes the base PDF
/// and the CII to a private temp directory, runs the operation, reads the result back and cleans up.
/// Pure: no DB, no HTTP; the 05-04 service composes persistence around it.
/// </para>
/// </remarks>
public static class ZugferdGenerator
{
    /// <summary>The ZUGFeRD 2.1.1 / Factur-X mandated embedded-XML file name (BT-X-nothing; fixed).</summary>
    public const string FacturXFileName = "factur-x.xml";

    /// <summary>
    /// The ZUGFeRD conformance XMP, inserted into the PDF's XMP <c>rdf:Description</c>. The profile is
    /// XRECHNUNG (our CII is <c>Profile.XRechnung</c>); the file name matches
    /// <see cref="FacturXFileName"/> so a reader can locate the embedded invoice.
    /// </summary>
    private const string ZugferdXmp =
        """
        <rdf:Description rdf:about="" xmlns:fx="urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#">
            <fx:DocumentType>INVOICE</fx:DocumentType>
            <fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>
            <fx:Version>1.0</fx:Version>
            <fx:ConformanceLevel>XRECHNUNG</fx:ConformanceLevel>
        </rdf:Description>
        """;

    /// <summary>
    /// Renders <paramref name="model"/> as a ZUGFeRD PDF/A-3b: the §14 PDF/A base + the embedded
    /// EN 16931 / XRECHNUNG CII (<c>factur-x.xml</c>, relationship <c>Source</c>) + the ZUGFeRD XMP.
    /// </summary>
    public static byte[] Generate(InvoicePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // ONE model → both artifacts (the EINV-02 anchor): the printed PDF/A base and the embedded
        // CII come from the same frozen InvoicePdfModel, so their values are identical by construction.
        var basePdf = InvoiceDocument.RenderPdfA(model);
        var cii = XRechnungGenerator.GenerateCiiForZugferd(model);

        var dir = Path.Combine(Path.GetTempPath(), "numera-zugferd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // The embedded-file name is taken from the source file name, so write the CII as factur-x.xml.
        var basePath = Path.Combine(dir, "invoice.pdf");
        var ciiPath = Path.Combine(dir, FacturXFileName);
        var outPath = Path.Combine(dir, "zugferd.pdf");

        try
        {
            File.WriteAllBytes(basePath, basePdf);
            File.WriteAllBytes(ciiPath, cii);

            var now = DateTime.UtcNow;
            DocumentOperation
                .LoadFile(basePath)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = ciiPath,
                    AttachmentName = FacturXFileName,
                    MimeType = "text/xml",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Source,
                    Description = "Factur-X / ZUGFeRD XRechnung (EN 16931 CII)",
                    CreationDate = now,
                    ModificationDate = now,
                })
                .ExtendMetadata(ZugferdXmp)
                .Save(outPath);

            return File.ReadAllBytes(outPath);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // A transient handle on the temp file must never fail invoice generation.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: best-effort cleanup of a private temp dir.
        }
    }
}
