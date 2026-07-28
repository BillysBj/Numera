using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Recurring;

public enum RecurringIntervalUnit
{
    Monthly,
    Quarterly,
    Yearly,
    Weekly,
}

public enum RecurringEndMode
{
    Never,
    UntilDate,
    AfterCount,
}

public enum RecurringStatus
{
    Active,
    Paused,
    Ended,
}

/// <summary>A tenant-scoped definition used to generate one invoice per scheduled period.</summary>
[Table("recurring_invoice_templates")]
[Index(nameof(TenantId), nameof(Status))]
public sealed class RecurringInvoiceTemplate : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public required string Name { get; set; }
    public Guid? PartnerId { get; set; }
    public string Currency { get; set; } = "EUR";

    [Precision(19, 6)]
    public decimal? ExchangeRate { get; set; }

    public DateOnly? ExchangeRateDate { get; set; }
    public RecurringIntervalUnit IntervalUnit { get; set; }
    public int IntervalCount { get; set; } = 1;
    public DateOnly StartOn { get; set; }
    public RecurringEndMode EndMode { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
    public DateOnly NextRunOn { get; set; }
    public DateOnly? LastGeneratedPeriodEnd { get; set; }
    public int GeneratedCount { get; set; }
    public RecurringStatus Status { get; set; } = RecurringStatus.Paused;
    public bool AutoFinalize { get; set; } = true;
    public bool AutoSend { get; set; }

    [ForeignKey(nameof(RecurringInvoiceTemplateLine.TemplateId))]
    public List<RecurringInvoiceTemplateLine> Lines { get; set; } = [];
}
