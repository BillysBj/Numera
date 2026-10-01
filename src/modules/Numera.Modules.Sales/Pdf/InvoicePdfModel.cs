using Numera.Platform.Money;

namespace Numera.Modules.Sales.Pdf;

/// <summary>
/// A flat, presentation-ready projection of a FINALIZED <see cref="SalesDocument"/>,
/// built ENTIRELY from the frozen snapshot (issuer/recipient jsonb + persisted lines +
/// BG-23 breakdown + totals) by <see cref="SnapshotReader"/>. It is the sole input to
/// <c>InvoiceDocument</c>.
/// </summary>
/// <remarks>
/// LOCKED: this model is derived only from data frozen at finalize — never from live
/// <c>CompanyProfile</c> / <c>BusinessPartner</c>. The single exception is the tenant
/// logo, which is presentation (not legal content) and therefore not part of the frozen
/// legal snapshot; it is passed in as raw bytes. The render <see cref="Language"/>
/// selects the label set and the formatting culture (de-DE default), while the
/// legally-frozen Pflichttexte in <see cref="BreakdownRows"/> are always printed verbatim.
/// </remarks>
public sealed record InvoicePdfModel
{
    /// <summary>Render language: "de" (default) or "en". Selects labels + formatting culture.</summary>
    public string Language { get; init; } = "de";

    /// <summary>Optional tenant logo bytes (presentation only, read live — not snapshotted).</summary>
    public byte[]? LogoBytes { get; init; }

    /// <summary>Content type of <see cref="LogoBytes"/> (e.g. image/png). Informational.</summary>
    public string? LogoContentType { get; init; }

    // --- Issuer (frozen IssuerSnapshot, BG-4) --------------------------------

    /// <summary>Issuer block frozen from company_profile at finalize.</summary>
    public required IssuerBlock Issuer { get; init; }

    // --- Recipient (frozen RecipientSnapshot, BG-7) --------------------------

    /// <summary>Recipient block frozen from the business partner at finalize.</summary>
    public required RecipientBlock Recipient { get; init; }

    // --- Header (persisted on the document row) ------------------------------

    /// <summary>The kind of document — drives the printed title (Angebot/Rechnung/…).</summary>
    public DocumentType DocumentType { get; init; } = DocumentType.Rechnung;

    /// <summary>Legal document number (BT-1).</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>Document / issue date (BT-2).</summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>Service date / period start (BT-72). Nullable.</summary>
    public DateOnly? ServiceDate { get; init; }

    /// <summary>Service period end (BG-14). Nullable.</summary>
    public DateOnly? ServicePeriodEnd { get; init; }

    /// <summary>Payment due date (BT-9). Nullable.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>ISO 4217 currency (BT-5).</summary>
    public string Currency { get; init; } = "EUR";

    /// <summary>Frozen foreign-currency units per EUR used when the document was finalized.</summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Frozen reference date of <see cref="ExchangeRate"/>.</summary>
    public DateOnly? ExchangeRateDate { get; init; }

    /// <summary>Frozen invoice VAT total in EUR (BT-111).</summary>
    public decimal? TotalTaxEur { get; init; }

    /// <summary>Leitweg-ID / buyer reference (BT-10). Nullable.</summary>
    public string? BuyerReference { get; init; }

    /// <summary>Free document note (BT-22). Nullable.</summary>
    public string? Notes { get; init; }

    // --- Lines (BG-25) -------------------------------------------------------

    /// <summary>The document line items in order.</summary>
    public IReadOnlyList<LineRow> Lines { get; init; } = [];

    // --- VAT breakdown (BG-23) -----------------------------------------------

    /// <summary>The EN 16931 VAT breakdown rows, one per (category, rate).</summary>
    public IReadOnlyList<BreakdownRow> BreakdownRows { get; init; } = [];

    // --- Frozen down-payment deductions (BT-113 source) ----------------------

    /// <summary>Itemized Abschläge frozen on the Schlussrechnung at finalize.</summary>
    public IReadOnlyList<PrepaymentRow> Prepayments { get; init; } = [];

    /// <summary>Total frozen gross prepayment deducted as BT-113.</summary>
    public decimal TotalPrepaid => Prepayments.Sum(p => p.GrossAmount);

    // --- Totals (persisted, frozen) ------------------------------------------

    /// <summary>Sum of line nets (BT-106/109).</summary>
    public decimal TotalNet { get; init; }

    /// <summary>Sum of breakdown tax (BT-110).</summary>
    public decimal TotalTax { get; init; }

    /// <summary>Grand total (BT-112).</summary>
    public decimal TotalGross { get; init; }

    /// <summary>Amount due (BT-115).</summary>
    public decimal AmountDue { get; init; }

    // --- Flags ---------------------------------------------------------------

    /// <summary>Issuer §19 Kleinunternehmer status at finalize (document carries no VAT).</summary>
    public bool IsKleinunternehmer { get; init; }

    /// <summary>True when any line is reverse-charge (AE / §13b).</summary>
    public bool ReverseCharge { get; init; }

    /// <summary>Frozen issuer block (BG-4/BG-5 + tax identity + bank/imprint).</summary>
    public sealed record IssuerBlock
    {
        /// <summary>Legal name (BT-27).</summary>
        public string? LegalName { get; init; }

        /// <summary>Postal address (BG-5).</summary>
        public AddressBlock Address { get; init; } = new();

        /// <summary>USt-IdNr (BT-31). Nullable.</summary>
        public string? VatId { get; init; }

        /// <summary>Steuernummer (BT-32). Nullable.</summary>
        public string? TaxNumber { get; init; }

        /// <summary>§19 Kleinunternehmer flag frozen at finalize.</summary>
        public bool IsKleinunternehmer { get; init; }

        /// <summary>IBAN (BT-84). Nullable.</summary>
        public string? Iban { get; init; }

        /// <summary>BIC (BT-86). Nullable.</summary>
        public string? Bic { get; init; }

        /// <summary>Bank name. Nullable.</summary>
        public string? BankName { get; init; }

        /// <summary>Registergericht (imprint). Nullable.</summary>
        public string? RegisterCourt { get; init; }

        /// <summary>Registernummer (imprint). Nullable.</summary>
        public string? RegisterNumber { get; init; }

        /// <summary>Geschäftsführer (imprint). Nullable.</summary>
        public string? ManagingDirector { get; init; }

        /// <summary>Contact email. Nullable.</summary>
        public string? ContactEmail { get; init; }

        /// <summary>Contact phone. Nullable.</summary>
        public string? ContactPhone { get; init; }
    }

    /// <summary>Frozen recipient block (BG-7/BG-8 + tax identity).</summary>
    public sealed record RecipientBlock
    {
        /// <summary>Buyer name (BT-44).</summary>
        public string? Name { get; init; }

        /// <summary>Legal form. Nullable.</summary>
        public string? LegalForm { get; init; }

        /// <summary>Billing address (BG-8).</summary>
        public AddressBlock BillingAddress { get; init; } = new();

        /// <summary>Buyer USt-IdNr (BT-48). Nullable.</summary>
        public string? VatId { get; init; }

        /// <summary>Buyer Steuernummer. Nullable.</summary>
        public string? TaxNumber { get; init; }

        /// <summary>Buyer email. Nullable.</summary>
        public string? Email { get; init; }

        /// <summary>Frozen Skonto (early-payment discount) percentage. Nullable.</summary>
        public decimal? SkontoPercent { get; init; }

        /// <summary>Frozen Skonto period in days from the document date. Nullable.</summary>
        public int? SkontoDays { get; init; }
    }

    /// <summary>A postal address (BG-5 / BG-8).</summary>
    public sealed record AddressBlock
    {
        /// <summary>Street + number (BT-35).</summary>
        public string? Street { get; init; }

        /// <summary>Address line 2 (BT-36). Nullable.</summary>
        public string? Line2 { get; init; }

        /// <summary>Postal code (BT-38).</summary>
        public string? PostalCode { get; init; }

        /// <summary>City (BT-37).</summary>
        public string? City { get; init; }

        /// <summary>ISO 3166 country code (BT-40).</summary>
        public string? CountryCode { get; init; }

        /// <summary>PO box. Nullable.</summary>
        public string? PoBox { get; init; }
    }

    /// <summary>A single presentation line (BG-25).</summary>
    public sealed record LineRow
    {
        /// <summary>1-based position (BT-126).</summary>
        public int LineNumber { get; init; }

        /// <summary>Line name / title (BT-153).</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Longer description (BT-154). Nullable.</summary>
        public string? Description { get; init; }

        /// <summary>Quantity (BT-129).</summary>
        public decimal Quantity { get; init; }

        /// <summary>UN/ECE unit code (BT-130).</summary>
        public string UnitCode { get; init; } = string.Empty;

        /// <summary>Net unit price (BT-146).</summary>
        public decimal NetUnitPrice { get; init; }

        /// <summary>Frozen line allowance percentage (BT-138).</summary>
        public decimal DiscountPercent { get; init; }

        /// <summary>Line net amount (BT-131).</summary>
        public decimal LineNetAmount { get; init; }

        /// <summary>VAT category (BT-151).</summary>
        public TaxCategory TaxCategory { get; init; }

        /// <summary>VAT rate percent (BT-152).</summary>
        public decimal VatRatePercent { get; init; }
    }

    /// <summary>A single VAT breakdown row (BG-23).</summary>
    public sealed record BreakdownRow
    {
        /// <summary>VAT category (BT-118).</summary>
        public TaxCategory TaxCategory { get; init; }

        /// <summary>VAT rate percent (BT-119).</summary>
        public decimal VatRatePercent { get; init; }

        /// <summary>Taxable base (BT-116).</summary>
        public decimal TaxableBase { get; init; }

        /// <summary>Tax amount (BT-117).</summary>
        public decimal TaxAmount { get; init; }

        /// <summary>Exemption reason code (BT-121). Nullable.</summary>
        public string? ExemptionReasonCode { get; init; }

        /// <summary>Exemption reason text / Pflichttext (BT-120), printed verbatim. Nullable.</summary>
        public string? ExemptionReasonText { get; init; }
    }

    /// <summary>A frozen Abschlag deduction displayed on the Schlussrechnung.</summary>
    public sealed record PrepaymentRow
    {
        /// <summary>Stable 1-based presentation position.</summary>
        public int LineNumber { get; init; }

        /// <summary>Frozen legal number of the Abschlagsrechnung.</summary>
        public string AbschlagNumber { get; init; } = string.Empty;

        /// <summary>Frozen issue date of the Abschlagsrechnung.</summary>
        public DateOnly AbschlagDate { get; init; }

        /// <summary>Frozen net amount.</summary>
        public decimal NetAmount { get; init; }

        /// <summary>Frozen VAT amount.</summary>
        public decimal VatAmount { get; init; }

        /// <summary>Frozen gross amount deducted from BT-115.</summary>
        public decimal GrossAmount { get; init; }
    }
}
