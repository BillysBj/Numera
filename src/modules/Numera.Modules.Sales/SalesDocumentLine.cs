using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Sales;

/// <summary>
/// A single line of a <see cref="SalesDocument"/> (EN 16931 BG-25). Structurally
/// identical across all document kinds — a Lieferschein simply renders without the
/// price columns (RESEARCH.md Pattern 1). Every value here is a SNAPSHOT: once a line
/// exists, editing or archiving the source catalog item never mutates it (GoBD).
/// </summary>
/// <remarks>
/// Frozen once the parent document leaves Draft by the <c>sales_document_child_immutable</c>
/// trigger (keyed off the parent's status), which also blocks INSERTs into a finalized
/// parent. Money precision follows the platform contract: amounts <c>numeric(19,4)</c>,
/// unit price / quantity <c>numeric(19,6)</c>, rate <c>numeric(5,2)</c>.
/// </remarks>
[Table("sales_document_lines")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class SalesDocumentLine : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning document (FK → sales_documents.id).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>1-based ordering position (BT-126).</summary>
    public int LineNumber { get; set; }

    /// <summary>Provenance only: the catalog item this line was created from. Data below is a SNAPSHOT.</summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary>Line name / item title (BT-153) — a snapshot of the catalog name or free text. Required.</summary>
    public required string Name { get; set; }

    /// <summary>Longer line description (BT-154). Nullable.</summary>
    public string? Description { get; set; }

    /// <summary>Invoiced quantity (BT-129).</summary>
    [Precision(19, 6)]
    public decimal Quantity { get; set; }

    /// <summary>UN/ECE Rec 20 unit code (BT-130), e.g. C62 / HUR. Required.</summary>
    public required string UnitCode { get; set; }

    /// <summary>Net unit price (BT-146).</summary>
    [Precision(19, 6)]
    public decimal NetUnitPrice { get; set; }

    /// <summary>Line allowance percentage (BT-138), from zero up to but excluding 100.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Line net amount (BT-131), after the line discount.</summary>
    [Precision(19, 4)]
    public decimal LineNetAmount { get; set; }

    /// <summary>EN 16931 VAT category (BT-151).</summary>
    public TaxCategory TaxCategory { get; set; }

    /// <summary>VAT rate as a percentage (BT-152); 0 for AE/K/E.</summary>
    [Precision(5, 2)]
    public decimal VatRatePercent { get; set; }
}
