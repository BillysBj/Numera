using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Sales;

/// <summary>
/// One row of the EN 16931 VAT breakdown (BG-23) of a <see cref="SalesDocument"/> —
/// a distinct (category, rate) group with its taxable base and tax amount. Persisted
/// and frozen at finalize so the §14 UStG "nach Steuersätzen aufgeschlüsselt"
/// requirement is met from stored data, not recomputed at render time.
/// </summary>
/// <remarks>
/// Computed at finalize via <c>RoundingPolicy.RoundTax</c> per bucket (per-category
/// rounding, never round-the-grand-total). Frozen once the parent leaves Draft by the
/// same <c>sales_document_child_immutable</c> trigger applied to the lines. The
/// exemption code (BT-121) / text (BT-120) carry the legally load-bearing Pflichttext
/// for AE/K/E/G scenarios.
/// </remarks>
[Table("sales_document_tax_breakdown")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class SalesDocumentTaxBreakdown : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning document (FK → sales_documents.id).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>VAT category of this group (BT-118).</summary>
    public TaxCategory TaxCategory { get; set; }

    /// <summary>VAT rate of this group as a percentage (BT-119).</summary>
    [Precision(5, 2)]
    public decimal VatRatePercent { get; set; }

    /// <summary>Taxable base for the group (BT-116) = Σ line nets in the group.</summary>
    [Precision(19, 4)]
    public decimal TaxableBase { get; set; }

    /// <summary>Tax amount for the group (BT-117) = RoundingPolicy.RoundTax(base, rate).</summary>
    [Precision(19, 4)]
    public decimal TaxAmount { get; set; }

    /// <summary>EN 16931 exemption reason code (BT-121), e.g. VATEX-EU-AE. Nullable.</summary>
    public string? ExemptionReasonCode { get; set; }

    /// <summary>EN 16931 exemption reason text (BT-120) — the human Pflichttext. Nullable.</summary>
    public string? ExemptionReasonText { get; set; }
}
