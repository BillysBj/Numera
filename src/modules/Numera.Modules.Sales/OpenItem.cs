using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales;

/// <summary>
/// An offener Posten (receivable) created atomically when an invoice is finalized
/// (RESEARCH.md Pattern 5, OPDN-01). Denormalizes the document number and gross for the
/// OP-Übersicht list. Payments (Phase 6) reduce <see cref="OpenAmount"/> and flip
/// <see cref="Status"/>; on Storno the original invoice's open item is set to
/// <see cref="OpenItemStatus.Cancelled"/> with a zero open amount.
/// </summary>
/// <remarks>
/// The <see cref="OpenAmount"/> + <see cref="Status"/> columns are the Phase-6 payment
/// seam; no payment recording is built now. Skonto fields are a snapshot of the
/// partner's discount terms at issue.
/// </remarks>
[Table("open_items")]
[Index(nameof(TenantId), nameof(Status), nameof(DueDate))]
public sealed class OpenItem : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The finalized invoice this receivable belongs to (FK → sales_documents.id).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Debtor partner reference. Nullable.</summary>
    public Guid? PartnerId { get; set; }

    /// <summary>Denormalized document number for the OP list.</summary>
    public required string DocumentNumber { get; set; }

    /// <summary>ISO 4217 currency (BT-5).</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Gross amount at issue.</summary>
    [Precision(19, 4)]
    public decimal OriginalAmount { get; set; }

    /// <summary>Remaining open amount; = original at issue, reduced by payments (Phase 6).</summary>
    [Precision(19, 4)]
    public decimal OpenAmount { get; set; }

    /// <summary>Settlement state.</summary>
    public OpenItemStatus Status { get; set; } = OpenItemStatus.Open;

    /// <summary>Issue date (= document_date).</summary>
    public DateOnly IssuedOn { get; set; }

    /// <summary>Payment due date (= document_date + partner net terms).</summary>
    public DateOnly DueDate { get; set; }

    // --- Skonto snapshot (Phase 6 payment seam) ------------------------------

    /// <summary>Snapshot of the partner Skonto percentage. Nullable.</summary>
    [Precision(5, 2)]
    public decimal? SkontoPercent { get; set; }

    /// <summary>Snapshot of the partner Skonto period in days. Nullable.</summary>
    public int? SkontoDays { get; set; }

    /// <summary>The date until which Skonto applies (= document_date + skonto_days). Nullable.</summary>
    public DateOnly? SkontoDueDate { get; set; }

    /// <summary>Preview of the Skonto discount amount, RoundingPolicy-consistent. Nullable.</summary>
    [Precision(19, 4)]
    public decimal? SkontoAmount { get; set; }
}
