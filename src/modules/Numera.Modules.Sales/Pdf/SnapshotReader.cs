using System.Text.Json;

namespace Numera.Modules.Sales.Pdf;

/// <summary>
/// Maps a FINALIZED <see cref="SalesDocument"/> to a flat <see cref="InvoicePdfModel"/>
/// by parsing the frozen <c>IssuerSnapshot</c> / <c>RecipientSnapshot</c> jsonb and
/// pulling the persisted lines / breakdown / dates / totals straight off the row.
/// </summary>
/// <remarks>
/// <para>
/// LOCKED (RESEARCH.md Pattern 3): the reader NEVER touches live master data
/// (<c>CompanyProfile</c> / <c>BusinessPartner</c>). It is intentionally stateless (no
/// <c>DbContext</c>) so it is host-agnostic — the render job, an endpoint, and unit tests
/// all call it the same way.
/// </para>
/// <para>
/// The snapshots are emitted with <see cref="JsonSerializerDefaults.Web"/>
/// (see <c>SalesDocumentEndpoints.SerializeIssuer/SerializeRecipient</c>), i.e. camelCase.
/// Parsing here is case-insensitive so it tolerates any casing the freezer may emit, and
/// every field access is defensive (a missing/absent property yields null, never a throw).
/// </para>
/// </remarks>
public static class SnapshotReader
{
    /// <summary>
    /// Builds a render model from the frozen snapshot of <paramref name="doc"/>.
    /// </summary>
    /// <param name="doc">The finalized document (with <c>Lines</c> and <c>TaxBreakdown</c> loaded).</param>
    /// <param name="prepayments">
    /// Frozen <see cref="SalesDocumentPrepayment"/> rows loaded for this document. The aggregate
    /// currently has no parent collection, so callers must query these rows by
    /// <see cref="SalesDocumentPrepayment.DocumentId"/> and pass them here.
    /// </param>
    /// <param name="logoBytes">Optional tenant logo bytes (presentation only).</param>
    /// <param name="logoContentType">Content type of <paramref name="logoBytes"/>.</param>
    /// <param name="language">Render language: "de" (default) or "en".</param>
    public static InvoicePdfModel FromDocument(
        SalesDocument doc,
        byte[]? logoBytes = null,
        string? logoContentType = null,
        string language = "de",
        IReadOnlyList<SalesDocumentPrepayment>? prepayments = null)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var lang = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";

        return new InvoicePdfModel
        {
            Language = lang,
            LogoBytes = logoBytes,
            LogoContentType = logoContentType,

            Issuer = ParseIssuer(doc.IssuerSnapshot),
            Recipient = ParseRecipient(doc.RecipientSnapshot),

            DocumentType = doc.DocumentType,
            DocumentNumber = doc.DocumentNumber,
            DocumentDate = doc.DocumentDate,
            ServiceDate = doc.ServiceDate,
            ServicePeriodEnd = doc.ServicePeriodEnd,
            DueDate = doc.DueDate,
            Currency = doc.Currency,
            ExchangeRate = doc.ExchangeRate,
            ExchangeRateDate = doc.ExchangeRateDate,
            TotalTaxEur = doc.TotalTaxEur,
            BuyerReference = doc.BuyerReference,
            Notes = doc.Notes,

            Lines = [.. doc.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new InvoicePdfModel.LineRow
                {
                    LineNumber = l.LineNumber,
                    Name = l.Name,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitCode = l.UnitCode,
                    NetUnitPrice = l.NetUnitPrice,
                    DiscountPercent = l.DiscountPercent,
                    LineNetAmount = l.LineNetAmount,
                    TaxCategory = l.TaxCategory,
                    VatRatePercent = l.VatRatePercent,
                })],

            BreakdownRows = [.. doc.TaxBreakdown
                .OrderBy(b => b.TaxCategory)
                .ThenBy(b => b.VatRatePercent)
                .Select(b => new InvoicePdfModel.BreakdownRow
                {
                    TaxCategory = b.TaxCategory,
                    VatRatePercent = b.VatRatePercent,
                    TaxableBase = b.TaxableBase,
                    TaxAmount = b.TaxAmount,
                    ExemptionReasonCode = b.ExemptionReasonCode,
                    ExemptionReasonText = b.ExemptionReasonText,
                })],

            Prepayments = [.. (prepayments ?? [])
                .OrderBy(p => p.AbschlagDate)
                .ThenBy(p => p.AbschlagNumber, StringComparer.Ordinal)
                .Select((p, index) => new InvoicePdfModel.PrepaymentRow
                {
                    LineNumber = index + 1,
                    AbschlagNumber = p.AbschlagNumber,
                    AbschlagDate = p.AbschlagDate,
                    NetAmount = p.NetAmount,
                    VatAmount = p.VatAmount,
                    GrossAmount = p.GrossAmount,
                })],

            TotalNet = doc.TotalNet,
            TotalTax = doc.TotalTax,
            TotalGross = doc.TotalGross,
            AmountDue = doc.AmountDue,
            IsKleinunternehmer = doc.IsKleinunternehmer,
            ReverseCharge = doc.ReverseCharge,
        };
    }

    private static InvoicePdfModel.IssuerBlock ParseIssuer(string? json)
    {
        if (!TryParse(json, out var root))
        {
            return new InvoicePdfModel.IssuerBlock();
        }

        var address = ParseAddress(GetProperty(root, "address"));
        var bank = GetProperty(root, "bank");

        return new InvoicePdfModel.IssuerBlock
        {
            LegalName = GetString(root, "legalName"),
            Address = address,
            VatId = GetString(root, "vatId"),
            TaxNumber = GetString(root, "taxNumber"),
            IsKleinunternehmer = GetBool(root, "isKleinunternehmer"),
            Iban = GetString(bank, "iban"),
            Bic = GetString(bank, "bic"),
            BankName = GetString(bank, "bankName"),
            RegisterCourt = GetString(root, "registerCourt"),
            RegisterNumber = GetString(root, "registerNumber"),
            ManagingDirector = GetString(root, "managingDirector"),
            ContactEmail = GetString(root, "contactEmail"),
            ContactPhone = GetString(root, "contactPhone"),
        };
    }

    private static InvoicePdfModel.RecipientBlock ParseRecipient(string? json)
    {
        if (!TryParse(json, out var root))
        {
            return new InvoicePdfModel.RecipientBlock();
        }

        return new InvoicePdfModel.RecipientBlock
        {
            CustomerNumber = GetString(root, "customerNumber"),
            Name = GetString(root, "name"),
            LegalForm = GetString(root, "legalForm"),
            BillingAddress = ParseAddress(GetProperty(root, "billingAddress")),
            VatId = GetString(root, "vatId"),
            TaxNumber = GetString(root, "taxNumber"),
            Email = GetString(root, "email"),
            SkontoPercent = GetDecimal(root, "skontoPercent"),
            SkontoDays = GetInt(root, "skontoDays"),
        };
    }

    private static InvoicePdfModel.AddressBlock ParseAddress(JsonElement? el) => new()
    {
        Street = GetString(el, "street"),
        Line2 = GetString(el, "line2"),
        PostalCode = GetString(el, "postalCode"),
        City = GetString(el, "city"),
        CountryCode = GetString(el, "countryCode"),
        PoBox = GetString(el, "poBox"),
    };

    private static bool TryParse(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Case-insensitive property lookup (tolerates camelCase / PascalCase alike).
    private static JsonElement? GetProperty(JsonElement? parent, string name)
    {
        if (parent is not { ValueKind: JsonValueKind.Object } el)
        {
            return null;
        }

        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement? parent, string name)
    {
        var prop = GetProperty(parent, name);
        return prop is { ValueKind: JsonValueKind.String } el ? el.GetString() : null;
    }

    private static decimal? GetDecimal(JsonElement? parent, string name)
    {
        var prop = GetProperty(parent, name);
        return prop is { ValueKind: JsonValueKind.Number } el && el.TryGetDecimal(out var v) ? v : null;
    }

    private static int? GetInt(JsonElement? parent, string name)
    {
        var prop = GetProperty(parent, name);
        return prop is { ValueKind: JsonValueKind.Number } el && el.TryGetInt32(out var v) ? v : null;
    }

    private static bool GetBool(JsonElement? parent, string name)
    {
        var prop = GetProperty(parent, name);
        return prop is { ValueKind: JsonValueKind.True };
    }
}
