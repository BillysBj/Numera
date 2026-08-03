using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>Whether new bookings may be inserted into a fiscal period.</summary>
public enum FiscalPeriodStatus
{
    /// <summary>The period accepts new bookings.</summary>
    Open = 0,

    /// <summary>The period is festgeschrieben and rejects new bookings.</summary>
    Locked = 1,
}

/// <summary>A tenant's calendar-month booking period and its lock status.</summary>
public sealed class FiscalPeriod : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Calendar year of the period.</summary>
    public int Year { get; set; }

    /// <summary>Calendar month of the period (1-12).</summary>
    public int Month { get; set; }

    /// <summary>Whether the period is open or locked.</summary>
    public FiscalPeriodStatus Status { get; set; }

    /// <summary>When the period was locked.</summary>
    public DateTimeOffset? LockedAt { get; set; }
}
