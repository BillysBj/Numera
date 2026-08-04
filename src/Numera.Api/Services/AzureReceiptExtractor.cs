using System.Globalization;

using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;

using Microsoft.Extensions.Options;

using Numera.Modules.Sales.Belege;

namespace Numera.Api.Services;

/// <summary>Configuration for the opt-in Azure AI Document Intelligence adapter.</summary>
public sealed class AzureDocumentIntelligenceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "DocumentIntelligence";

    /// <summary>The EU-region Document Intelligence resource endpoint.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The resource API key.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Extracts invoice fields with Azure AI Document Intelligence's <c>prebuilt-invoice</c>
/// model. The adapter is registered only when endpoint and API key are configured.
/// </summary>
public sealed class AzureReceiptExtractor : IReceiptExtractor
{
    private static readonly ReceiptExtraction EmptyExtraction = new(
        SupplierName: null,
        SupplierVatId: null,
        InvoiceNumber: null,
        InvoiceDate: null,
        NetAmount: null,
        VatAmount: null,
        GrossAmount: null,
        VatRatePercent: null,
        Currency: null);

    private readonly DocumentIntelligenceClient _client;

    /// <summary>Creates an extractor from the bound Document Intelligence configuration.</summary>
    public AzureReceiptExtractor(IOptions<AzureDocumentIntelligenceOptions> options)
    {
        var value = options.Value;
        _client = new DocumentIntelligenceClient(
            new Uri(value.Endpoint),
            new AzureKeyCredential(value.ApiKey));
    }

    /// <inheritdoc />
    public async Task<ReceiptExtraction> ExtractAsync(
        byte[] bytes,
        string contentType,
        CancellationToken ct)
    {
        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-invoice",
            BinaryData.FromBytes(bytes),
            cancellationToken: ct);

        var document = operation.Value.Documents.FirstOrDefault();
        if (document is null)
        {
            return EmptyExtraction;
        }

        var netAmount = MapDecimal(document, "SubTotal");
        var vatAmount = MapDecimal(document, "TotalTax");
        var grossAmount = MapDecimal(document, "InvoiceTotal");

        return new ReceiptExtraction(
            SupplierName: MapString(document, "VendorName"),
            SupplierVatId: MapString(document, "VendorTaxId"),
            InvoiceNumber: MapString(document, "InvoiceId"),
            InvoiceDate: MapDate(document, "InvoiceDate"),
            NetAmount: netAmount,
            VatAmount: vatAmount,
            GrossAmount: grossAmount,
            VatRatePercent: null,
            Currency: FindCurrency(document, "InvoiceTotal", "SubTotal", "TotalTax"));
    }

    private static ExtractedField<string>? MapString(AnalyzedDocument document, string fieldName)
    {
        if (!document.Fields.TryGetValue(fieldName, out var field))
        {
            return null;
        }

        var value = field.ValueString ?? field.Content;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new ExtractedField<string>(value, field.Confidence ?? 0);
    }

    private static ExtractedField<DateOnly>? MapDate(AnalyzedDocument document, string fieldName)
    {
        if (!document.Fields.TryGetValue(fieldName, out var field))
        {
            return null;
        }

        DateOnly value;
        if (field.ValueDate is { } normalizedDate)
        {
            value = DateOnly.FromDateTime(normalizedDate.DateTime);
        }
        else if (!DateOnly.TryParse(
                     field.Content,
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out value))
        {
            return null;
        }

        return new ExtractedField<DateOnly>(value, field.Confidence ?? 0);
    }

    private static ExtractedField<decimal>? MapDecimal(
        AnalyzedDocument document,
        string fieldName)
    {
        if (!document.Fields.TryGetValue(fieldName, out var field)
            || !TryParseDecimal(field, out var value))
        {
            return null;
        }

        return new ExtractedField<decimal>(value, field.Confidence ?? 0);
    }

    private static bool TryParseDecimal(DocumentField field, out decimal value)
    {
        // The Azure SDK exposes normalized numeric/currency values as doubles. Round-trip the
        // normalized representation and parse it immediately so money leaves this boundary only
        // as decimal and no binary floating-point value enters the receipt/ledger model.
        string? normalized = null;
        if (field.FieldType == DocumentFieldType.Currency)
        {
            normalized = field.ValueCurrency.Amount.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (field.FieldType == DocumentFieldType.Double
                 && field.ValueDouble is { } number)
        {
            normalized = number.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (field.FieldType == DocumentFieldType.Int64
                 && field.ValueInt64 is { } integer)
        {
            normalized = integer.ToString(CultureInfo.InvariantCulture);
        }

        if (normalized is not null
            && decimal.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return decimal.TryParse(
                   field.Content,
                   NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                   CultureInfo.InvariantCulture,
                   out value)
               || decimal.TryParse(
                   field.Content,
                   NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                   CultureInfo.GetCultureInfo("de-DE"),
                   out value);
    }

    private static string? FindCurrency(
        AnalyzedDocument document,
        params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (document.Fields.TryGetValue(fieldName, out var field)
                && field.FieldType == DocumentFieldType.Currency
                && !string.IsNullOrWhiteSpace(field.ValueCurrency.CurrencyCode))
            {
                return field.ValueCurrency.CurrencyCode;
            }
        }

        return null;
    }
}
