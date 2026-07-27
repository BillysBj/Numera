using System.Text;

using s2industries.ZUGFeRD;

using UglyToad.PdfPig;

namespace Numera.Modules.Sales.EInvoice.Inbound;

/// <summary>
/// Detects, extracts and parses a received (inbound) e-invoice (Phase-5 EINV-04). Static + stateless
/// (no DbContext, no HTTP) — mirrors <see cref="EInvoiceMapper"/>/<c>SnapshotReader</c>. Given the raw
/// upload bytes it:
/// <list type="number">
///   <item>detects a PDF (magic <c>%PDF</c> / <c>application/pdf</c>) vs a raw XML upload;</item>
///   <item>for a PDF, extracts the embedded e-invoice XML via PdfPig (the /EmbeddedFiles name tree,
///   looking for <c>factur-x.xml</c> / <c>zugferd-invoice.xml</c> / <c>xrechnung.xml</c>);</item>
///   <item>parses the XML via <see cref="InvoiceDescriptor.Load(System.IO.Stream)"/> — the SAME
///   ZUGFeRD-csharp library used outbound, auto-detecting UBL/CII + ZUGFeRD version;</item>
///   <item>projects the descriptor to a human-readable <see cref="InboundReadModel"/>.</item>
/// </list>
/// A plain PDF (no embedded e-invoice) or an XML that is not a parseable EN 16931 invoice is NOT an
/// e-invoice and yields a failure result (never a throw) so the endpoint can 422 cleanly. The raw
/// upload bytes are NEVER mutated here (GoBD; RESEARCH anti-pattern "mutating inbound originals").
/// </summary>
public static class InboundParser
{
    // The canonical embedded-XML name stems a ZUGFeRD / Factur-X / XRechnung PDF/A-3 uses. Matched
    // as a case-insensitive substring because the /EmbeddedFiles name-tree KEY varies by producer:
    // some tools use the full "factur-x.xml", others just the stem "factur-x" (e.g. QuestPDF), so a
    // stem match plus a content sniff (see TryExtractEmbeddedXml) covers both.
    private static readonly string[] EInvoiceAttachmentStems =
        ["factur-x", "zugferd-invoice", "xrechnung", "cii", "order-x"];

    /// <summary>
    /// Detects the format, extracts the embedded XML (for a PDF), and parses the e-invoice into a
    /// read-model. Never throws for a bad/absent e-invoice — returns a failure result instead.
    /// </summary>
    /// <param name="bytes">The raw uploaded bytes (preserved unchanged).</param>
    /// <param name="contentType">The upload content-type (application/pdf, application/xml, text/xml).</param>
    /// <param name="fileName">The upload file name (used only as a detection hint).</param>
    public static InboundParseResult Parse(byte[] bytes, string? contentType, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return InboundParseResult.Fail("Die hochgeladene Datei ist leer.");
        }

        var isPdf = LooksLikePdf(bytes, contentType, fileName);

        byte[] xml;
        InboundFormat format;
        if (isPdf)
        {
            var extracted = TryExtractEmbeddedXml(bytes);
            if (extracted is null)
            {
                return InboundParseResult.Fail(
                    "Das PDF enthält keine eingebettete E-Rechnung (factur-x.xml / zugferd-invoice.xml).");
            }

            xml = extracted;
            format = InboundFormat.ZugferdPdf;
        }
        else
        {
            xml = bytes;
            format = DetectXmlSyntax(bytes);
        }

        InvoiceDescriptor descriptor;
        try
        {
            using var ms = new MemoryStream(xml, writable: false);
            descriptor = InvoiceDescriptor.Load(ms);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return InboundParseResult.Fail(
                "Die Datei konnte nicht als EN-16931-E-Rechnung gelesen werden (kein gültiges XRechnung/ZUGFeRD-Dokument).");
        }

        var readModel = Project(descriptor);
        return InboundParseResult.Ok(format, xml, readModel);
    }

    private static bool LooksLikePdf(byte[] bytes, string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(fileName)
            && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Magic bytes "%PDF".
        return bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
    }

    // Pulls the embedded e-invoice XML out of a ZUGFeRD/Factur-X PDF/A-3 via PdfPig's /EmbeddedFiles
    // name tree. Prefers the canonical factur-x/zugferd/xrechnung names; falls back to any *.xml
    // attachment. Returns null when the PDF carries no embedded XML (a plain PDF is not an e-invoice).
    private static byte[]? TryExtractEmbeddedXml(byte[] pdfBytes)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            if (!doc.Advanced.TryGetEmbeddedFiles(out var files) || files is null || files.Count == 0)
            {
                return null;
            }

            // 1. Prefer an attachment whose name matches a canonical e-invoice stem or the .xml
            //    extension (producer-independent — see EInvoiceAttachmentStems).
            var preferred = files.FirstOrDefault(f => f.Name is { } name
                && (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    || EInvoiceAttachmentStems.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))));

            // 2. Fall back to the first embedded file whose CONTENT sniffs as XML (robust to an
            //    arbitrary attachment name from a foreign sender).
            var chosen = preferred ?? files.FirstOrDefault(f => SniffsAsXml(f.Bytes));
            if (chosen is null)
            {
                return null;
            }

            var arr = chosen.Bytes.ToArray();
            return arr.Length > 0 ? arr : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A corrupt / unreadable PDF is treated as "no embedded e-invoice" (endpoint 422s cleanly).
            return null;
        }
    }

    // Sniffs whether bytes look like an XML document: the first non-whitespace byte (past an
    // optional UTF-8 BOM) is '<'. Cheap, encoding-tolerant, and enough to pick the XML attachment
    // out of a PDF whose embedded-file name does not follow the ZUGFeRD convention.
    private static bool SniffsAsXml(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            i = 3; // skip UTF-8 BOM
        }

        for (; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return b == (byte)'<';
        }

        return false;
    }

    // Distinguishes raw XRechnung UBL from CII by the root element. A CII document is a
    // <rsm:CrossIndustryInvoice>; a UBL invoice is a <Invoice> in the UBL namespace. Scanning the
    // decoded head for the CII marker is enough (namespace-agnostic, avoids a full XML parse here).
    private static InboundFormat DetectXmlSyntax(byte[] xml)
    {
        var head = Encoding.UTF8.GetString(xml, 0, Math.Min(xml.Length, 4096));
        return head.Contains("CrossIndustryInvoice", StringComparison.Ordinal)
            ? InboundFormat.XmlCii
            : InboundFormat.XmlUbl;
    }

    // Projects a parsed descriptor to the human-readable read-model. Reads only what the detail view
    // needs; every value is transcribed from the parsed document, nothing recomputed.
    private static InboundReadModel Project(InvoiceDescriptor d)
    {
        var seller = d.Seller;
        var buyer = d.Buyer;

        return new InboundReadModel
        {
            InvoiceNumber = d.InvoiceNo,
            InvoiceDate = ToDateOnly(d.InvoiceDate),
            Currency = d.Currency.ToString(),
            Seller = new InboundReadModel.PartyBlock
            {
                Name = seller?.Name,
                Street = seller?.Street,
                PostalCode = seller?.Postcode,
                City = seller?.City,
                CountryCode = seller?.Country?.ToString(),
                VatId = TaxRegistrationOf(d.SellerTaxRegistration, TaxRegistrationSchemeID.VA),
                TaxNumber = TaxRegistrationOf(d.SellerTaxRegistration, TaxRegistrationSchemeID.FC),
            },
            Buyer = new InboundReadModel.PartyBlock
            {
                Name = buyer?.Name,
                Street = buyer?.Street,
                PostalCode = buyer?.Postcode,
                City = buyer?.City,
                CountryCode = buyer?.Country?.ToString(),
                VatId = TaxRegistrationOf(d.BuyerTaxRegistration, TaxRegistrationSchemeID.VA),
                TaxNumber = TaxRegistrationOf(d.BuyerTaxRegistration, TaxRegistrationSchemeID.FC),
            },
            TotalNet = d.LineTotalAmount ?? d.TaxBasisAmount,
            TotalTax = d.TaxTotalAmount,
            TotalGross = d.GrandTotalAmount,
            AmountDue = d.DuePayableAmount,
            Lines = [.. (d.TradeLineItems ?? []).Select(l => new InboundReadModel.LineRow
            {
                Name = l.Name,
                Description = l.Description,
                Quantity = l.BilledQuantity,
                UnitCode = l.UnitCode?.ToString(),
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = l.LineTotalAmount,
                TaxCategory = l.TaxCategoryCode?.ToString(),
                VatRatePercent = l.TaxPercent,
            })],
            BreakdownRows = [.. (d.Taxes ?? []).Select(t => new InboundReadModel.BreakdownRow
            {
                TaxCategory = t.CategoryCode?.ToString(),
                VatRatePercent = t.Percent,
                TaxableBase = t.BasisAmount,
                TaxAmount = t.TaxAmount,
                ExemptionReasonCode = t.ExemptionReasonCode?.ToString(),
                ExemptionReasonText = t.ExemptionReason,
            })],
        };
    }

    private static string? TaxRegistrationOf(
        IEnumerable<TaxRegistration>? registrations,
        TaxRegistrationSchemeID scheme) =>
        registrations?.FirstOrDefault(r => r.SchemeID == scheme)?.No;

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is { } dt ? DateOnly.FromDateTime(dt) : null;
}

/// <summary>
/// The outcome of <see cref="InboundParser.Parse"/>: either a parsed e-invoice (the detected format,
/// the extracted XML, and the human-readable read-model) or a failure with a German reason (the
/// upload was not an e-invoice / could not be read).
/// </summary>
/// <param name="Success">True when the upload parsed as an EN 16931 e-invoice.</param>
/// <param name="FailureReason">The German reason when <see cref="Success"/> is false, else null.</param>
/// <param name="Format">The detected input format (valid only when <see cref="Success"/>).</param>
/// <param name="ExtractedXml">The e-invoice XML (raw upload for XML, extracted for a PDF).</param>
/// <param name="ReadModel">The human-readable projection (valid only when <see cref="Success"/>).</param>
public sealed record InboundParseResult(
    bool Success,
    string? FailureReason,
    InboundFormat Format,
    byte[]? ExtractedXml,
    InboundReadModel? ReadModel)
{
    /// <summary>Builds a successful parse result.</summary>
    public static InboundParseResult Ok(InboundFormat format, byte[] xml, InboundReadModel readModel) =>
        new(true, null, format, xml, readModel);

    /// <summary>Builds a failure result carrying a human-readable German reason.</summary>
    public static InboundParseResult Fail(string reason) =>
        new(false, reason, default, null, null);
}
