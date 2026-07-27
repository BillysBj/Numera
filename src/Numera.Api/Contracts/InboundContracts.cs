using System.Text.Json;

using Numera.Api.Services;

namespace Numera.Api.Contracts;

/// <summary>
/// The wire DTOs for the inbound e-invoice (Eingangsbelege) surface (Phase-5 EINV-04/EINV-05).
/// Enum-valued fields cross the wire as their NUMERIC ordinals (the Api has no
/// JsonStringEnumConverter) — the frontend mirrors <c>EInvoiceValidationStatus</c> +
/// <c>InboundFormat</c> exactly.
/// </summary>
public static class InboundContracts
{
    /// <summary>One row of the inbound list (GET /api/inbound-documents).</summary>
    /// <param name="Id">The inbound document id.</param>
    /// <param name="DetectedFormat">The detected input format (InboundFormat ordinal).</param>
    /// <param name="SellerName">The supplier / seller name (BT-27).</param>
    /// <param name="SellerVatId">The seller VAT id (BT-31).</param>
    /// <param name="InvoiceNumber">The invoice number (BT-1).</param>
    /// <param name="InvoiceDate">The invoice date (BT-2), ISO date or null.</param>
    /// <param name="TotalGross">The grand total (BT-112) or null.</param>
    /// <param name="Currency">ISO 4217 currency (BT-5).</param>
    /// <param name="ValidationStatus">The KoSIT verdict (EInvoiceValidationStatus ordinal).</param>
    /// <param name="MatchedPartnerId">The matched supplier id, or null ("nicht zugeordnet").</param>
    /// <param name="OriginalFileName">The uploaded file name.</param>
    /// <param name="UploadedAt">When the e-invoice was uploaded.</param>
    public sealed record InboundListItem(
        Guid Id,
        int DetectedFormat,
        string? SellerName,
        string? SellerVatId,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        decimal? TotalGross,
        string? Currency,
        int ValidationStatus,
        Guid? MatchedPartnerId,
        string OriginalFileName,
        DateTimeOffset UploadedAt);

    /// <summary>The paged inbound list envelope.</summary>
    public sealed record InboundListResponse(
        IReadOnlyList<InboundListItem> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>
    /// The full inbound detail (GET /api/inbound-documents/{id}): the summary fields, the parsed
    /// human-readable read-model (as a nested JSON object), and the explained KoSIT findings.
    /// </summary>
    /// <param name="Id">The inbound document id.</param>
    /// <param name="DetectedFormat">The detected input format (InboundFormat ordinal).</param>
    /// <param name="ValidationStatus">The KoSIT verdict (EInvoiceValidationStatus ordinal).</param>
    /// <param name="MatchedPartnerId">The matched supplier id, or null.</param>
    /// <param name="OriginalFileName">The uploaded file name.</param>
    /// <param name="OriginalContentType">The uploaded content-type.</param>
    /// <param name="ByteSize">Size of the immutable original in bytes.</param>
    /// <param name="UploadedAt">When the e-invoice was uploaded.</param>
    /// <param name="SellerName">Denormalized seller name.</param>
    /// <param name="SellerVatId">Denormalized seller VAT id.</param>
    /// <param name="InvoiceNumber">Denormalized invoice number.</param>
    /// <param name="InvoiceDate">Denormalized invoice date.</param>
    /// <param name="TotalGross">Denormalized grand total.</param>
    /// <param name="Currency">Denormalized currency.</param>
    /// <param name="ReadModel">The parsed read-model as a nested JSON object (seller/buyer/lines/breakdown/totals).</param>
    /// <param name="Findings">The explained KoSIT findings (DE authoritative + EN).</param>
    public sealed record InboundDetail(
        Guid Id,
        int DetectedFormat,
        int ValidationStatus,
        Guid? MatchedPartnerId,
        string OriginalFileName,
        string OriginalContentType,
        long ByteSize,
        DateTimeOffset UploadedAt,
        string? SellerName,
        string? SellerVatId,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        decimal? TotalGross,
        string? Currency,
        JsonElement? ReadModel,
        IReadOnlyList<EInvoiceFinding> Findings);
}
