using System.Globalization;

namespace Numera.Modules.Sales.Pdf;

/// <summary>
/// The bilingual label + culture resource set for <see cref="InvoiceDocument"/>, keyed by
/// render language ("de" default, "en"). ONE layout consumes this so there is no separate
/// German/English layout to drift (RESEARCH.md Pitfall 6).
/// </summary>
/// <remarks>
/// These labels are presentation chrome only. The legally-frozen Pflichttexte (the
/// Kleinunternehmer §19 / reverse-charge §13b notes in
/// <see cref="InvoicePdfModel.BreakdownRow.ExemptionReasonText"/>) are rendered verbatim
/// and stay German even under the English label set (LOCKED). <see cref="Culture"/> drives
/// all money/date formatting so the worker's ambient culture never leaks (Pitfall 4).
/// </remarks>
public sealed record PdfLabels
{
    /// <summary>The formatting culture for money + dates (de-DE default → 1.234,56 € / 13.07.2026).</summary>
    public required CultureInfo Culture { get; init; }

    /// <summary>Document title, e.g. "Rechnung" / "Invoice".</summary>
    public required string Invoice { get; init; }

    /// <summary>"Rechnungsnummer" / "Invoice No.".</summary>
    public required string InvoiceNo { get; init; }

    /// <summary>"Kundennummer" / "Customer no.".</summary>
    public required string CustomerNumber { get; init; }

    /// <summary>"Datum" / "Date".</summary>
    public required string Date { get; init; }

    /// <summary>"Leistungsdatum" / "Service Date".</summary>
    public required string ServiceDate { get; init; }

    /// <summary>Supply-date note when the invoice date is used as the default.</summary>
    public required string ServiceDateMatchesInvoiceDate { get; init; }

    /// <summary>"Leistungszeitraum" / "Service Period".</summary>
    public required string ServicePeriod { get; init; }

    /// <summary>"Fällig am" / "Due".</summary>
    public required string Due { get; init; }

    /// <summary>"Leitweg-ID / Referenz" / "Buyer Reference".</summary>
    public required string BuyerReference { get; init; }

    /// <summary>"Pos." / "Item".</summary>
    public required string Item { get; init; }

    /// <summary>"Beschreibung" / "Description".</summary>
    public required string Description { get; init; }

    /// <summary>"Menge" / "Qty".</summary>
    public required string Qty { get; init; }

    /// <summary>"Einzelpreis" / "Unit Price".</summary>
    public required string UnitPrice { get; init; }

    /// <summary>"Betrag" / "Amount".</summary>
    public required string Amount { get; init; }

    /// <summary>"Zwischensumme netto" / "Subtotal (net)".</summary>
    public required string SubtotalNet { get; init; }

    /// <summary>"USt" / "VAT".</summary>
    public required string Vat { get; init; }

    /// <summary>"Gesamt" / "Total".</summary>
    public required string Total { get; init; }

    /// <summary>"Zahlbar bis" / "Payable by".</summary>
    public required string PayableBy { get; init; }

    /// <summary>"Bankverbindung" / "Bank Details".</summary>
    public required string BankDetails { get; init; }

    /// <summary>"IBAN" (same in both).</summary>
    public required string Iban { get; init; }

    /// <summary>"BIC" (same in both).</summary>
    public required string Bic { get; init; }

    /// <summary>"USt-IdNr" / "VAT ID".</summary>
    public required string VatId { get; init; }

    /// <summary>"Steuernummer" / "Tax No.".</summary>
    public required string TaxNo { get; init; }

    /// <summary>"Steuersatz" / "Rate" (breakdown table column).</summary>
    public required string Rate { get; init; }

    /// <summary>"Bemessungsgrundlage" / "Taxable Base" (breakdown table column).</summary>
    public required string TaxableBase { get; init; }

    /// <summary>"Steuerbetrag" / "Tax Amount" (breakdown table column).</summary>
    public required string TaxAmount { get; init; }

    /// <summary>"USt-Aufschlüsselung" / "VAT Breakdown" (section heading).</summary>
    public required string VatBreakdown { get; init; }

    /// <summary>"Rechnungsempfänger" / "Bill To".</summary>
    public required string BillTo { get; init; }

    /// <summary>Resolves the label set for a render language ("en" → English, everything else → German).</summary>
    public static PdfLabels For(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? English : German;

    /// <summary>German label set (default). Culture de-DE.</summary>
    public static PdfLabels German { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("de-DE"),
        Invoice = "Rechnung",
        InvoiceNo = "Rechnungsnummer",
        CustomerNumber = "Kundennummer",
        Date = "Datum",
        ServiceDate = "Leistungsdatum",
        ServiceDateMatchesInvoiceDate = "Leistungsdatum entspricht dem Rechnungsdatum",
        ServicePeriod = "Leistungszeitraum",
        Due = "Fällig am",
        BuyerReference = "Leitweg-ID / Referenz",
        Item = "Pos.",
        Description = "Beschreibung",
        Qty = "Menge",
        UnitPrice = "Einzelpreis",
        Amount = "Betrag",
        SubtotalNet = "Zwischensumme netto",
        Vat = "USt",
        Total = "Gesamt",
        PayableBy = "Zahlbar bis",
        BankDetails = "Bankverbindung",
        Iban = "IBAN",
        Bic = "BIC",
        VatId = "USt-IdNr",
        TaxNo = "Steuernummer",
        Rate = "Steuersatz",
        TaxableBase = "Bemessungsgrundlage",
        TaxAmount = "Steuerbetrag",
        VatBreakdown = "USt-Aufschlüsselung",
        BillTo = "Rechnungsempfänger",
    };

    /// <summary>English label set. Culture en-GB (European date order, £-free money formatted via currency string).</summary>
    public static PdfLabels English { get; } = new()
    {
        Culture = CultureInfo.GetCultureInfo("en-GB"),
        Invoice = "Invoice",
        InvoiceNo = "Invoice No.",
        CustomerNumber = "Customer no.",
        Date = "Date",
        ServiceDate = "Service Date",
        ServiceDateMatchesInvoiceDate = "Date of supply is the invoice date",
        ServicePeriod = "Service Period",
        Due = "Due",
        BuyerReference = "Buyer Reference",
        Item = "Item",
        Description = "Description",
        Qty = "Qty",
        UnitPrice = "Unit Price",
        Amount = "Amount",
        SubtotalNet = "Subtotal (net)",
        Vat = "VAT",
        Total = "Total",
        PayableBy = "Payable by",
        BankDetails = "Bank Details",
        Iban = "IBAN",
        Bic = "BIC",
        VatId = "VAT ID",
        TaxNo = "Tax No.",
        Rate = "Rate",
        TaxableBase = "Taxable Base",
        TaxAmount = "Tax Amount",
        VatBreakdown = "VAT Breakdown",
        BillTo = "Bill To",
    };
}
