namespace Numera.Modules.Sales.Belege;

/// <summary>
/// Zero-dependency receipt extractor used by default. It deliberately proposes no values so
/// the review flow starts with manual entry rather than relying on a cloud OCR provider.
/// </summary>
public sealed class StubReceiptExtractor : IReceiptExtractor
{
    /// <inheritdoc />
    public Task<ReceiptExtraction> ExtractAsync(
        byte[] bytes,
        string contentType,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(new ReceiptExtraction(
            SupplierName: null,
            SupplierVatId: null,
            InvoiceNumber: null,
            InvoiceDate: null,
            NetAmount: null,
            VatAmount: null,
            GrossAmount: null,
            VatRatePercent: null,
            Currency: null));
    }
}
