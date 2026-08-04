using System.Text.Json;

namespace Numera.Api.Contracts;

/// <summary>Wire records for the unified receipt review queue.</summary>
public static class ReceiptContracts
{
    /// <summary>The complete set of human-confirmed fields persisted by the review step.</summary>
    public sealed record ReviewReceiptRequest(
        Guid? SupplierPartnerId,
        string? ExpenseAccountOverride,
        decimal? VatRatePercent,
        decimal NetAmount,
        decimal VatAmount,
        decimal GrossAmount,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        DateOnly? ExpenseDate);

    /// <summary>The resulting review stamp.</summary>
    public sealed record ReviewReceiptResponse(
        Guid Id,
        int Status,
        Guid ReviewedByUserId,
        DateTimeOffset ReviewedAt);

    /// <summary>The supplier resolved for a receipt booking proposal.</summary>
    public sealed record ReceiptProposalSupplier(
        Guid? PartnerId,
        string? Name,
        string CreditorAccount);

    /// <summary>One frozen VAT-rate row in the single expense posting input.</summary>
    public sealed record ReceiptProposalBreakdown(
        decimal VatRatePercent,
        decimal NetAmount,
        decimal VatAmount);

    /// <summary>One debit or credit leg previewed from the expense posting source.</summary>
    public sealed record ReceiptProposalPostingLeg(
        string AccountNumber,
        int Direction,
        decimal Amount,
        decimal? VatRatePercent,
        int? TaxKey);

    /// <summary>The non-persisting preview of the exact input and legs used by confirm-book.</summary>
    public sealed record ReceiptBookingProposalResponse(
        Guid ReceiptId,
        ReceiptProposalSupplier Supplier,
        string ExpenseAccount,
        DateOnly EntryDate,
        IReadOnlyList<ReceiptProposalBreakdown> Breakdowns,
        IReadOnlyList<ReceiptProposalPostingLeg> PostingLegs,
        decimal TotalNet,
        decimal TotalVat,
        decimal TotalGross);

    /// <summary>The idempotent result of confirm-book.</summary>
    public sealed record ReceiptBookingResponse(
        Guid ReceiptId,
        Guid JournalEntryId,
        int Status,
        bool AlreadyBooked);

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
