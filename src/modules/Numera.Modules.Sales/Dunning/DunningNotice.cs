using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Dunning;

/// <summary>Issued dunning notice and its independently stated ancillary claims.</summary>
[Table("dunning_notice")]
[Index(nameof(TenantId), nameof(OpenItemId))]
public sealed class DunningNotice : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Receivable addressed by this notice.</summary>
    public Guid OpenItemId { get; set; }

    /// <summary>Sales document addressed by this notice.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Dunning level issued.</summary>
    public int Level { get; set; }

    /// <summary>Date of issue.</summary>
    public DateOnly IssuedOn { get; set; }

    /// <summary>New payment deadline stated in the notice.</summary>
    public DateOnly NewDueDate { get; set; }

    /// <summary>Outstanding principal at issue time.</summary>
    [Precision(19, 4)]
    public decimal OverdueAmount { get; set; }

    /// <summary>Ancillary dunning fee.</summary>
    [Precision(19, 4)]
    public decimal Fee { get; set; }

    /// <summary>Ancillary default interest.</summary>
    [Precision(19, 4)]
    public decimal Interest { get; set; }

    /// <summary>Annual interest rate used for the calculation.</summary>
    [Precision(19, 4)]
    public decimal InterestRatePercent { get; set; }

    /// <summary>Total stated in the letter: principal plus fee and interest.</summary>
    [Precision(19, 4)]
    public decimal TotalToPay { get; set; }

    /// <summary>Rendered PDF populated by the send job.</summary>
    public byte[]? RenderedPdf { get; set; }

    /// <summary>0 Pending, 1 Sent, 2 Failed.</summary>
    public int Status { get; set; }

    /// <summary>Successful dispatch timestamp.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Most recent dispatch error.</summary>
    public string? LastError { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
