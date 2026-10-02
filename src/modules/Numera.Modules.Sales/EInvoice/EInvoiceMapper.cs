using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using s2industries.ZUGFeRD;

namespace Numera.Modules.Sales.EInvoice;

/// <summary>
/// THE single EN 16931 mapping layer: turns a finalized invoice's frozen
/// <see cref="InvoicePdfModel"/> into ONE ZUGFeRD-csharp <see cref="InvoiceDescriptor"/>.
/// </summary>
/// <remarks>
/// <para>
/// LOCKED — same GoBD guarantee as <see cref="SnapshotReader"/>: this mapper reads ONLY the
/// frozen snapshot (issuer/recipient blocks + persisted lines + BG-23 breakdown + frozen
/// totals) and NEVER live <c>CompanyProfile</c> / <c>BusinessPartner</c> master data. Every
/// monetary value is transcribed field-by-field from the frozen model — nothing is ever
/// recomputed here (EN 16931 BR-CO-* / the PDF↔XML value identity behind EINV-02 depend on
/// that; RESEARCH Pitfall 3).
/// </para>
/// <para>
/// The four XRechnung CIUS-mandatory fields the frozen <see cref="InvoicePdfModel"/> does not
/// model directly are filled here from existing frozen fields + constants (NO
/// <c>sales_documents</c> migration — RESEARCH "GAPS to close"):
/// <list type="bullet">
///   <item>G1 — BT-10 Käuferreferenz (BR-DE-15): <see cref="InvoicePdfModel.BuyerReference"/>,
///   DEFAULTED to <see cref="DefaultBuyerReference"/> when absent so BT-10 is always present.</item>
///   <item>G2 — BT-34 / BT-49 electronic addresses with EAS scheme <c>EM</c> (email).</item>
///   <item>G3 — BG-6 seller contact (BT-41/42/43, BR-DE-2).</item>
///   <item>G4 — BT-81 payment means: 58 (SEPA credit transfer) with an IBAN, else 30.</item>
/// </list>
/// A genuinely-absent G1/G2/G3 value is surfaced by the 05-03 pre-finalize dry-run BEFORE a
/// number is burned; the exact §19 (category E) VATEX code/text is pinned by the 05-03
/// live-conformance golden file, so here it is transcribed from the frozen row as-is.
/// </para>
/// <para>
/// Upgrade seam: ZUGFeRD-csharp 18 is the last OSS major; FactoorSharp + XRechnung 4.0 later.
/// Keep this mapper the single change-locus for that bump.
/// </para>
/// </remarks>
public static class EInvoiceMapper
{
    /// <summary>
    /// XRechnung business process (BT-23), REQUIRED since XRechnung 3.0.1 (RESEARCH Pattern 1).
    /// </summary>
    public const string BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <summary>
    /// Documented BT-10 fallback (BR-DE-15) used when the frozen model carries no buyer
    /// reference — a non-empty placeholder keeps every XRechnung structurally valid; a truly
    /// missing reference is caught by the 05-03 pre-finalize dry-run.
    /// </summary>
    public const string DefaultBuyerReference = "NA";

    /// <summary>
    /// Maps a frozen <see cref="InvoicePdfModel"/> to one populated
    /// <see cref="InvoiceDescriptor"/> ready to serialize as XRechnung UBL / CII (05-01) and
    /// to embed in the ZUGFeRD PDF/A-3 (05-04). Pure — no DB, no HTTP.
    /// </summary>
    public static InvoiceDescriptor ToDescriptor(InvoicePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var desc = InvoiceDescriptor.CreateInvoice(
            model.DocumentNumber ?? string.Empty,
            model.DocumentDate.ToDateTime(TimeOnly.MinValue),
            ParseCurrency(model.Currency));

        // Only type-380 Rechnung is e-invoiced in v1 (381/384 correction handling is a 05-03
        // concern; RESEARCH Pitfall 7 does not arise here). BT-3.
        desc.Type = InvoiceType.Invoice;
        desc.BusinessProcess = BusinessProcess;

        // G1 — BT-10 Käuferreferenz (BR-DE-15): mandatory for EVERY XRechnung, defaulted.
        desc.ReferenceOrderNo = string.IsNullOrWhiteSpace(model.BuyerReference)
            ? DefaultBuyerReference
            : model.BuyerReference!;

        MapSeller(desc, model.Issuer);
        MapBuyer(desc, model.Recipient);
        MapDelivery(desc, model);
        MapDates(desc, model);
        MapLines(desc, model.Lines, model.Currency);
        MapVatBreakdown(desc, model.BreakdownRows);

        // Totals — transcribed from the frozen persisted values, NEVER recomputed
        // (RESEARCH Pitfall 3; prevents BR-CO-10/13/15 drift). BT-106/109/112/115.
        var totalPrepaid = model.TotalPrepaid;
        desc.TotalPrepaidAmount = totalPrepaid;
        desc.SetTotals(
            lineTotalAmount: model.TotalNet,
            chargeTotalAmount: null,
            allowanceTotalAmount: null,
            taxBasisAmount: model.TotalNet,
            taxTotalAmount: model.TotalTax,
            grandTotalAmount: model.TotalGross,
            totalPrepaidAmount: totalPrepaid,
            duePayableAmount: model.AmountDue,
            roundingAmount: null);

        if (!string.Equals(model.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
            && model.TotalTaxEur is not null)
        {
            // BT-6 and BT-111 are a mandatory pair under BR-53. BT-111 is the frozen
            // accounting-currency VAT amount and is injected after serialization because
            // ZUGFeRD-csharp 18 has no descriptor property for BT-111.
            desc.TaxCurrency = CurrencyCodes.EUR;
        }

        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            desc.AddNote(model.Notes!); // BT-22
        }

        return desc;
    }

    private static void MapSeller(InvoiceDescriptor desc, InvoicePdfModel.IssuerBlock issuer)
    {
        // Seller BG-4 / postal address BG-5 (BT-35..40).
        desc.SetSeller(
            name: issuer.LegalName ?? string.Empty,
            postcode: issuer.Address.PostalCode ?? string.Empty,
            city: issuer.Address.City ?? string.Empty,
            street: issuer.Address.Street ?? string.Empty,
            country: ParseCountry(issuer.Address.CountryCode));

        // BT-27 Seller name (BR-06): the UBL writer only emits cac:PartyLegalEntity/RegistrationName
        // when a legal organization is present — without it BR-06 rejects. The CII SellerTradeParty
        // already carries ram:Name, so this closes the UBL-side gap from the SAME frozen legal name.
        desc.Seller.SpecifiedLegalOrganization =
            new LegalOrganization { TradingBusinessName = issuer.LegalName ?? string.Empty };

        // Tax identity: USt-IdNr (BT-31, scheme VA) and/or Steuernummer (BT-32, scheme FC).
        if (!string.IsNullOrWhiteSpace(issuer.VatId))
        {
            desc.AddSellerTaxRegistration(issuer.VatId!, TaxRegistrationSchemeID.VA);
        }

        if (!string.IsNullOrWhiteSpace(issuer.TaxNumber))
        {
            desc.AddSellerTaxRegistration(issuer.TaxNumber!, TaxRegistrationSchemeID.FC);
        }

        // G2 — seller electronic address BT-34 with EAS scheme EM (email).
        if (!string.IsNullOrWhiteSpace(issuer.ContactEmail))
        {
            desc.SetSellerElectronicAddress(
                issuer.ContactEmail!,
                ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);
        }

        // G3 — seller contact group BG-6 (BR-DE-2): BT-41 name / BT-42 phone / BT-43 email.
        desc.SetSellerContact(
            name: issuer.ManagingDirector ?? issuer.LegalName ?? string.Empty,
            emailAddress: issuer.ContactEmail,
            phoneno: issuer.ContactPhone);

        // Bank / IBAN BT-84 + BIC BT-86.
        if (!string.IsNullOrWhiteSpace(issuer.Iban))
        {
            desc.AddCreditorFinancialAccount(
                issuer.Iban!,
                issuer.Bic ?? string.Empty,
                bankName: issuer.BankName);
        }

        // G4 — payment means BT-81: 58 (SEPA credit transfer) with an IBAN, else 30.
        desc.SetPaymentMeans(string.IsNullOrWhiteSpace(issuer.Iban)
            ? PaymentMeansTypeCodes.CreditTransferNonSEPA
            : PaymentMeansTypeCodes.SEPACreditTransfer);
    }

    private static void MapBuyer(InvoiceDescriptor desc, InvoicePdfModel.RecipientBlock recipient)
    {
        // Buyer BG-7 / billing address BG-8.
        desc.SetBuyer(
            name: recipient.Name ?? string.Empty,
            postcode: recipient.BillingAddress.PostalCode ?? string.Empty,
            city: recipient.BillingAddress.City ?? string.Empty,
            street: recipient.BillingAddress.Street ?? string.Empty,
            country: ParseCountry(recipient.BillingAddress.CountryCode));

        // BT-44 Buyer name (BR-07): same UBL-writer requirement as the seller — set the buyer's
        // legal organization so cac:PartyLegalEntity/RegistrationName is emitted from the frozen name.
        desc.Buyer.SpecifiedLegalOrganization =
            new LegalOrganization { TradingBusinessName = recipient.Name ?? string.Empty };

        // Buyer VAT id BT-48 when present.
        if (!string.IsNullOrWhiteSpace(recipient.VatId))
        {
            desc.AddBuyerTaxRegistration(recipient.VatId!, TaxRegistrationSchemeID.VA);
        }

        // G2 — buyer electronic address BT-49 with EAS scheme EM (email).
        if (!string.IsNullOrWhiteSpace(recipient.Email))
        {
            desc.SetBuyerElectronicAddress(
                recipient.Email!,
                ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);
        }
    }

    private static void MapDelivery(InvoiceDescriptor desc, InvoicePdfModel model)
    {
        // BG-13 Deliver-to. For an intra-community supply (category K) EN 16931 BR-IC-12 requires
        // the Deliver-to country code (BT-80) to be present. The goods are delivered to the buyer,
        // so we default the ship-to to the frozen buyer address (its country carries BT-80). Only
        // emitted when a K breakdown row exists — other categories do not need BG-13.
        var isIntraCommunity = model.BreakdownRows.Any(r => r.TaxCategory == TaxCategory.K)
            || model.Lines.Any(l => l.TaxCategory == TaxCategory.K);

        if (!isIntraCommunity)
        {
            return;
        }

        var buyer = model.Recipient;
        desc.ShipTo = new Party
        {
            Name = buyer.Name ?? string.Empty,
            Street = buyer.BillingAddress.Street ?? string.Empty,
            Postcode = buyer.BillingAddress.PostalCode ?? string.Empty,
            City = buyer.BillingAddress.City ?? string.Empty,
            Country = ParseCountry(buyer.BillingAddress.CountryCode),
        };
    }

    private static void MapDates(InvoiceDescriptor desc, InvoicePdfModel model)
    {
        // Due date BT-9 — as a payment term due date. A description (BT-20) MUST be supplied:
        // a null/blank description makes the ZUGFeRD-csharp CII writer emit an empty
        // <ram:Description> element, which KoSIT rejects (PEPPOL-EN16931-R008 "no empty elements").
        // The text is derived from the frozen due date, so nothing is invented beyond the template.
        if (model.DueDate is { } due)
        {
            desc.AddTradePaymentTerms(
                $"Zahlbar ohne Abzug bis {due:dd.MM.yyyy}.",
                due.ToDateTime(TimeOnly.MinValue));
        }

        // Service date BT-72 / service period BG-14.
        if (model.ServiceDate is { } start)
        {
            if (model.ServicePeriodEnd is { } end)
            {
                desc.SetBillingPeriod(
                    start.ToDateTime(TimeOnly.MinValue),
                    end.ToDateTime(TimeOnly.MinValue));
            }
            else
            {
                desc.ActualDeliveryDate = start.ToDateTime(TimeOnly.MinValue);
            }
        }
        else if (model.UsesInvoiceDateAsSupplyDate)
        {
            // BT-72 matches the human-readable supply-date fallback in the PDF.
            desc.ActualDeliveryDate = model.DocumentDate.ToDateTime(TimeOnly.MinValue);
        }
    }

    private static void MapLines(InvoiceDescriptor desc, IReadOnlyList<InvoicePdfModel.LineRow> lines, string currency)
    {
        foreach (var line in lines)
        {
            // BG-25: name BT-153, quantity BT-129, unit BT-130, net unit price BT-146,
            // line net amount BT-131, category BT-151, rate BT-152.
            var item = desc.AddTradeLineItem(
                name: line.Name,
                netUnitPrice: line.NetUnitPrice,
                unitCode: ParseUnit(line.UnitCode),
                description: line.Description,
                billedQuantity: line.Quantity,
                lineTotalAmount: line.LineNetAmount,
                taxType: TaxTypes.VAT,
                categoryCode: MapCategory(line.TaxCategory),
                taxPercent: line.VatRatePercent);

            if (line.DiscountPercent > 0m)
            {
                var basis = line.Quantity * line.NetUnitPrice;
                item.AddSpecifiedTradeAllowance(
                    currency: ParseCurrency(currency),
                    basisAmount: RoundingPolicy.RoundAmount(basis),
                    actualAmount: RoundingPolicy.RoundAmount(basis - line.LineNetAmount),
                    chargePercentage: line.DiscountPercent,
                    reason: "Rabatt",
                    reasonCode: AllowanceReasonCodes.Discount);
            }
        }
    }

    private static void MapVatBreakdown(
        InvoiceDescriptor desc,
        IReadOnlyList<InvoicePdfModel.BreakdownRow> rows)
    {
        foreach (var row in rows)
        {
            // BG-23: base BT-116, rate BT-119, tax BT-117, category BT-118, exemption
            // code BT-121 + reason BT-120 — all STRAIGHT off the frozen row (never recomputed).
            desc.AddApplicableTradeTax(
                basisAmount: row.TaxableBase,
                percent: row.VatRatePercent,
                taxAmount: row.TaxAmount,
                typeCode: TaxTypes.VAT,
                categoryCode: MapCategory(row.TaxCategory),
                exemptionReasonCode: MapExemptionCode(row.ExemptionReasonCode),
                exemptionReason: row.ExemptionReasonText);
        }
    }

    /// <summary>
    /// THE single place EN 16931 UNCL5305 category codes are decided (BT-118 / BT-151).
    /// </summary>
    private static TaxCategoryCodes MapCategory(TaxCategory category) => category switch
    {
        TaxCategory.S => TaxCategoryCodes.S,   // Standard rate
        TaxCategory.AE => TaxCategoryCodes.AE, // VAT reverse charge (§13b)
        TaxCategory.K => TaxCategoryCodes.K,   // Intra-community supply
        TaxCategory.E => TaxCategoryCodes.E,   // Exempt (§19 Kleinunternehmer)
        TaxCategory.Z => TaxCategoryCodes.Z,   // Zero-rated
        TaxCategory.G => TaxCategoryCodes.G,   // Free export
        TaxCategory.O => TaxCategoryCodes.O,   // Out of scope
        _ => TaxCategoryCodes.S,
    };

    /// <summary>
    /// Maps a frozen VATEX exemption code string (BT-121, e.g. <c>VATEX-EU-AE</c>) to the
    /// ZUGFeRD-csharp enum, transcribing whatever the frozen row carries (dash-form to the
    /// underscore enum name). Unknown/blank codes map to null so only the BT-120 reason text
    /// is emitted.
    /// </summary>
    private static TaxExemptionReasonCodes? MapExemptionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return Enum.TryParse<TaxExemptionReasonCodes>(code.Replace('-', '_'), ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    private static CurrencyCodes ParseCurrency(string? code) =>
        Enum.TryParse<CurrencyCodes>(code, ignoreCase: true, out var parsed) ? parsed : CurrencyCodes.EUR;

    private static CountryCodes? ParseCountry(string? code) =>
        Enum.TryParse<CountryCodes>(code, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>
    /// Maps a UN/ECE Rec 20 unit-code string (BT-130) to the ZUGFeRD-csharp
    /// <see cref="QuantityCodes"/> enum (whose numeric codes are name-prefixed with '_'),
    /// falling back to <c>C62</c> (one/piece) for an unknown code.
    /// </summary>
    private static QuantityCodes ParseUnit(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return QuantityCodes.C62;
        }

        if (Enum.TryParse<QuantityCodes>(code, ignoreCase: true, out var direct))
        {
            return direct;
        }

        return char.IsDigit(code[0]) && Enum.TryParse<QuantityCodes>("_" + code, ignoreCase: true, out var numeric)
            ? numeric
            : QuantityCodes.C62;
    }
}
