namespace Numera.Modules.Sales.EInvoice.Inbound;

/// <summary>
/// The human-readable projection of a parsed inbound e-invoice (Phase-5 EINV-04) — enough for a
/// person to read the received document: seller + buyer, number/dates/currency, totals, the line
/// items and the VAT breakdown. Serialized as jsonb onto <c>inbound_document.read_model</c> and
/// rendered by the inbound detail page. Derived from the immutable original (which is preserved
/// separately, byte-for-byte); this projection is regenerated on re-processing.
/// </summary>
/// <remarks>
/// Nullable throughout on purpose: a received e-invoice comes from an arbitrary sender and may omit
/// optional EN 16931 fields; the view simply shows what is present. Category / unit codes are kept
/// as their raw string codes (as parsed), not re-mapped to Numera enums — this is a foreign document.
/// </remarks>
public sealed record InboundReadModel
{
    /// <summary>Invoice number (BT-1).</summary>
    public string? InvoiceNumber { get; init; }

    /// <summary>Invoice / issue date (BT-2).</summary>
    public DateOnly? InvoiceDate { get; init; }

    /// <summary>ISO 4217 currency (BT-5).</summary>
    public string? Currency { get; init; }

    /// <summary>The seller / supplier party (BG-4).</summary>
    public PartyBlock Seller { get; init; } = new();

    /// <summary>The buyer party (BG-7) — normally this tenant.</summary>
    public PartyBlock Buyer { get; init; } = new();

    /// <summary>Sum of line nets (BT-106).</summary>
    public decimal? TotalNet { get; init; }

    /// <summary>Total VAT (BT-110).</summary>
    public decimal? TotalTax { get; init; }

    /// <summary>Grand total (BT-112).</summary>
    public decimal? TotalGross { get; init; }

    /// <summary>Amount due for payment (BT-115).</summary>
    public decimal? AmountDue { get; init; }

    /// <summary>The line items (BG-25).</summary>
    public IReadOnlyList<LineRow> Lines { get; init; } = [];

    /// <summary>The VAT breakdown (BG-23), one per (category, rate).</summary>
    public IReadOnlyList<BreakdownRow> BreakdownRows { get; init; } = [];

    /// <summary>A party (seller / buyer) as read from the inbound document.</summary>
    public sealed record PartyBlock
    {
        /// <summary>Party name (BT-27 seller / BT-44 buyer).</summary>
        public string? Name { get; init; }

        /// <summary>Street + number (BT-35 / BT-50).</summary>
        public string? Street { get; init; }

        /// <summary>Postal code (BT-38 / BT-53).</summary>
        public string? PostalCode { get; init; }

        /// <summary>City (BT-37 / BT-52).</summary>
        public string? City { get; init; }

        /// <summary>ISO 3166 country code (BT-40 / BT-55).</summary>
        public string? CountryCode { get; init; }

        /// <summary>VAT id / USt-IdNr (BT-31 seller / BT-48 buyer) — the supplier-match key.</summary>
        public string? VatId { get; init; }

        /// <summary>National tax number (BT-32).</summary>
        public string? TaxNumber { get; init; }
    }

    /// <summary>A single line item (BG-25).</summary>
    public sealed record LineRow
    {
        /// <summary>Line name / item (BT-153).</summary>
        public string? Name { get; init; }

        /// <summary>Longer description (BT-154).</summary>
        public string? Description { get; init; }

        /// <summary>Billed quantity (BT-129).</summary>
        public decimal? Quantity { get; init; }

        /// <summary>UN/ECE unit code (BT-130).</summary>
        public string? UnitCode { get; init; }

        /// <summary>Net unit price (BT-146).</summary>
        public decimal? NetUnitPrice { get; init; }

        /// <summary>Line net amount (BT-131).</summary>
        public decimal? LineNetAmount { get; init; }

        /// <summary>VAT category code (BT-151), as parsed (e.g. S / AE / K).</summary>
        public string? TaxCategory { get; init; }

        /// <summary>VAT rate percent (BT-152).</summary>
        public decimal? VatRatePercent { get; init; }
    }

    /// <summary>A single VAT breakdown row (BG-23).</summary>
    public sealed record BreakdownRow
    {
        /// <summary>VAT category code (BT-118), as parsed.</summary>
        public string? TaxCategory { get; init; }

        /// <summary>VAT rate percent (BT-119).</summary>
        public decimal? VatRatePercent { get; init; }

        /// <summary>Taxable base (BT-116).</summary>
        public decimal? TaxableBase { get; init; }

        /// <summary>Tax amount (BT-117).</summary>
        public decimal? TaxAmount { get; init; }

        /// <summary>Exemption reason code (BT-121).</summary>
        public string? ExemptionReasonCode { get; init; }

        /// <summary>Exemption reason text (BT-120).</summary>
        public string? ExemptionReasonText { get; init; }
    }
}
