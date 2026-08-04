using System.Text.Json;

namespace Numera.Api.Contracts;

/// <summary>Wire records for the unified receipt review queue.</summary>
public static class ReceiptContracts
{
    /// <summary>One row in the paged receipt list.</summary>
    public sealed record ReceiptListItem(
        Guid Id,
        int Source,
        int Status,
        string? SupplierName,
        string? SupplierVatId,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        decimal? NetAmount,
        decimal? VatAmount,
        decimal? GrossAmount,
        decimal? VatRatePercent,
        string? Currency,
        JsonElement? FieldConfidence,
        Guid? MatchedPartnerId,
        string? OriginalFileName,
        DateTimeOffset CreatedAt);

    /// <summary>The paged review queue envelope.</summary>
    public sealed record ReceiptListResponse(
        IReadOnlyList<ReceiptListItem> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>A receipt plus provenance and immutable-original metadata.</summary>
    public sealed record ReceiptDetail(
        Guid Id,
        int Source,
        int Status,
        Guid? InboundDocumentId,
        Guid? ArchiveId,
        string ContentHash,
        string? SupplierName,
        string? SupplierVatId,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        DateOnly? ExpenseDate,
        decimal? NetAmount,
        decimal? VatAmount,
        decimal? GrossAmount,
        decimal? VatRatePercent,
        string? Currency,
        JsonElement? FieldConfidence,
        Guid? MatchedPartnerId,
        string? ExpenseAccountOverride,
        Guid? JournalEntryId,
        string? OriginalFileName,
        string? OriginalContentType,
        long? ByteSize,
        DateTimeOffset? ReceivedAt,
        Guid? UploadedByUserId,
        DateTimeOffset CreatedAt);
}
