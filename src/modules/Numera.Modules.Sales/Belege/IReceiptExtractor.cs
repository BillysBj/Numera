namespace Numera.Modules.Sales.Belege;

/// <summary>
/// Provider-neutral port for extracting reviewable receipt fields from an original document.
/// Extraction only proposes values; callers must never book them without human confirmation.
/// </summary>
public interface IReceiptExtractor
{
    /// <summary>Extracts structured fields and their provider confidence from a receipt.</summary>
    Task<ReceiptExtraction> ExtractAsync(
        byte[] bytes,
        string contentType,
        CancellationToken ct);
}

/// <summary>Structured receipt fields proposed for human review.</summary>
public sealed record ReceiptExtraction(
    ExtractedField<string>? SupplierName,
    ExtractedField<string>? SupplierVatId,
    ExtractedField<string>? InvoiceNumber,
    ExtractedField<DateOnly>? InvoiceDate,
    ExtractedField<decimal>? NetAmount,
    ExtractedField<decimal>? VatAmount,
    ExtractedField<decimal>? GrossAmount,
    ExtractedField<decimal>? VatRatePercent,
    string? Currency);

/// <summary>A proposed field value and the provider's confidence in that value.</summary>
public sealed record ExtractedField<T>(T Value, double Confidence);
