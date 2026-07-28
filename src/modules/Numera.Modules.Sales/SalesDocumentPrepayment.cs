using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales;

/// <summary>
/// A frozen down-payment deduction (EN 16931 BT-113) captured on a Schlussrechnung.
/// </summary>
[Table("sales_document_prepayment")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class SalesDocumentPrepayment : ITenantEntity
{
    /// <summary>Client-generated UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning Schlussrechnung.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Navigation declaring the parent FK; rows are inserted with <c>db.Add</c>.</summary>
    [ForeignKey(nameof(DocumentId))]
    public SalesDocument Document { get; set; } = null!;

    /// <summary>Provenance of the deducted Abschlagsrechnung.</summary>
    public Guid AbschlagDocumentId { get; set; }

    /// <summary>Frozen legal number of the deducted Abschlagsrechnung.</summary>
    public string AbschlagNumber { get; set; } = string.Empty;

    /// <summary>Frozen issue date of the deducted Abschlagsrechnung.</summary>
    public DateOnly AbschlagDate { get; set; }

    /// <summary>Frozen net amount.</summary>
    [Precision(19, 4)]
    public decimal NetAmount { get; set; }

    /// <summary>Frozen VAT amount, calculated via RoundingPolicy upstream.</summary>
    [Precision(19, 4)]
    public decimal VatAmount { get; set; }

    /// <summary>Frozen gross amount deducted from BT-115.</summary>
    [Precision(19, 4)]
    public decimal GrossAmount { get; set; }
}
