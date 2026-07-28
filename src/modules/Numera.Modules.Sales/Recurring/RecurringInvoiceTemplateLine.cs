using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Sales.Recurring;

/// <summary>A reusable sales-document line snapshot belonging to a recurring template.</summary>
[Table("recurring_invoice_template_lines")]
[Index(nameof(TenantId), nameof(TemplateId))]
public sealed class RecurringInvoiceTemplateLine : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid TemplateId { get; set; }
    public int LineNumber { get; set; }
    public Guid? CatalogItemId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    [Precision(19, 6)]
    public decimal Quantity { get; set; }

    public required string UnitCode { get; set; }

    [Precision(19, 6)]
    public decimal NetUnitPrice { get; set; }

    public TaxCategory TaxCategory { get; set; }

    [Precision(5, 2)]
    public decimal VatRatePercent { get; set; }
}
