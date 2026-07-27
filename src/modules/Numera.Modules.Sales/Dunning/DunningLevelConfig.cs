using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Dunning;

/// <summary>Tenant-specific configuration for one step of the dunning ladder.</summary>
[Table("dunning_level_config")]
[Index(nameof(TenantId), nameof(Level), IsUnique = true)]
public sealed class DunningLevelConfig : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Zero is the payment reminder; positive values are dunning levels.</summary>
    public int Level { get; set; }

    /// <summary>Display name of the level.</summary>
    public required string Name { get; set; }

    /// <summary>Threshold in days after the receivable due date.</summary>
    public int DaysAfterDue { get; set; }

    /// <summary>Dunning fee, kept separate from the receivable principal.</summary>
    [Precision(19, 4)]
    public decimal Fee { get; set; }

    /// <summary>Whether statutory default interest is charged.</summary>
    public bool ChargeInterest { get; set; }

    /// <summary>Annual interest rate in percent.</summary>
    [Precision(6, 3)]
    public decimal InterestRatePercent { get; set; }

    /// <summary>German letter text.</summary>
    public required string TemplateTextDe { get; set; }

    /// <summary>English letter text.</summary>
    public required string TemplateTextEn { get; set; }
}
