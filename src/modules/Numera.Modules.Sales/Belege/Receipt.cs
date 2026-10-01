using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Belege;

/// <summary>
/// The converged receipt aggregate for camera, upload, email, and e-invoice intake. Tier-A
/// e-invoices link their immutable <c>inbound_document</c>; Tier-B receipts link a dedicated
/// <see cref="ReceiptArchive"/> containing the original bytes.
/// </summary>
/// <remarks>
/// <para>
/// This self-describing <see cref="ITenantEntity"/> is discovered by <c>NumeraDbContext</c>;
/// plain-Guid links are provenance only, with no navigation properties or explicit DbSet.
/// </para>
/// <para>
/// An extracted receipt is NEVER booked automatically. OCR and e-invoice values remain proposals
/// until a human review advances the receipt through <see cref="ReceiptStatus.Reviewed"/>.
/// </para>
/// </remarks>
[Table("receipt")]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(ContentHash))]
public sealed class Receipt : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The intake channel.</summary>
    public ReceiptSource Source { get; set; }

    /// <summary>The current human-controlled lifecycle state.</summary>
    public ReceiptStatus Status { get; set; }

    /// <summary>Tier-A provenance link to <c>inbound_document.id</c>, when applicable.</summary>
    public Guid? InboundDocumentId { get; set; }

    /// <summary>Tier-B provenance link to <c>receipt_archive.id</c>, when applicable.</summary>
    public Guid? ArchiveId { get; set; }

    /// <summary>SHA-256 hex digest of the immutable original bytes.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Extracted or confirmed supplier name.</summary>
    public string? SupplierName { get; set; }

    /// <summary>Extracted or confirmed supplier VAT identifier.</summary>
    public string? SupplierVatId { get; set; }

    /// <summary>Extracted or confirmed supplier invoice number.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Extracted or confirmed invoice date.</summary>
    public DateOnly? InvoiceDate { get; set; }

    /// <summary>Optional expense-performance date used as the posting date.</summary>
    public DateOnly? ExpenseDate { get; set; }

    /// <summary>Extracted or confirmed net amount.</summary>
    [Precision(19, 4)]
    public decimal? NetAmount { get; set; }

    /// <summary>Extracted or confirmed VAT amount.</summary>
    [Precision(19, 4)]
    public decimal? VatAmount { get; set; }

    /// <summary>Extracted or confirmed gross amount.</summary>
    [Precision(19, 4)]
    public decimal? GrossAmount { get; set; }

    /// <summary>Remaining payable, initialized when the receipt is booked.</summary>
    [Precision(19, 4)]
    public decimal? OpenAmount { get; set; }

    /// <summary>Settlement state; null for receipts booked before payable tracking.</summary>
    public ReceiptPaymentStatus? PaymentStatus { get; set; }

    /// <summary>VAT rate for a single-rate receipt; multi-rate details come from the e-invoice.</summary>
    [Precision(19, 4)]
    public decimal? VatRatePercent { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>Per-field OCR confidence serialized as JSON, null for e-invoice/manual input.</summary>
    [Column(TypeName = "jsonb")]
    public string? FieldConfidence { get; set; }

    /// <summary>Matched supplier (plain Guid provenance link to <c>partners.id</c>).</summary>
    public Guid? MatchedPartnerId { get; set; }

    /// <summary>Optional user-confirmed expense account override.</summary>
    public string? ExpenseAccountOverride { get; set; }

    /// <summary>Resulting journal entry after booking (plain Guid provenance link).</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>User who confirmed the extracted proposal.</summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>When the extracted proposal was confirmed.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>When the receipt aggregate was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
