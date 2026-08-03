using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Gaplessly numbers journal entries and permanently locks calendar periods.</summary>
public sealed class FestschreibungService(
    NumeraDbContext db,
    ICurrentTenant currentTenant,
    IAuditWriter audit)
{
    /// <summary>Locks one calendar month in a single transaction.</summary>
    public async Task<PeriodLockResult> LockPeriodAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return PeriodLockResult.Invalid("Year must be 1-9999 and month must be 1-12.");
        }

        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to lock a fiscal period.");
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Serialize locks for the same tenant + calendar year so two month locks cannot
        // observe the same maximum and allocate overlapping journal-number ranges.
        var lockKey = $"ledger-journal:{tenantId:D}:{year}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            ct).ConfigureAwait(false);

        var period = await db.Set<FiscalPeriod>()
            .SingleOrDefaultAsync(candidate => candidate.Year == year && candidate.Month == month, ct)
            .ConfigureAwait(false);
        if (period is { Status: FiscalPeriodStatus.Locked })
        {
            return PeriodLockResult.Conflict(period.Id, "The fiscal period is already locked.");
        }

        if (period is null)
        {
            period = new FiscalPeriod
            {
                TenantId = tenantId,
                Year = year,
                Month = month,
                Status = FiscalPeriodStatus.Open,
            };
            db.Add(period);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var prefix = $"{year}-";
        var existingNumbers = await db.Set<JournalEntry>()
            .AsNoTracking()
            .Where(entry => entry.JournalNumber != null && entry.JournalNumber.StartsWith(prefix))
            .Select(entry => entry.JournalNumber!)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var sequence = existingNumbers
            .Select(number => int.TryParse(number.AsSpan(prefix.Length), out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

        var entries = await db.Set<JournalEntry>()
            .Where(entry =>
                entry.EntryDate >= periodStart
                && entry.EntryDate <= periodEnd
                && entry.JournalNumber == null
                && entry.FestgeschriebenAt == null)
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lockedAt = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            entry.JournalNumber = $"{year}-{++sequence:000000}";
            entry.FestgeschriebenAt = lockedAt;
            entry.PeriodId = period.Id;
        }

        period.Status = FiscalPeriodStatus.Locked;
        period.LockedAt = lockedAt;
        await audit.RecordAsync(
            new FestschreibungAuditEvent(
                "ledger.period_locked",
                period.Id,
                Before: null,
                After: JsonSerializer.Serialize(new
                {
                    period.Id,
                    period.Year,
                    period.Month,
                    EntriesLocked = entries.Count,
                    period.LockedAt,
                })),
            ct).ConfigureAwait(false);

        // The journal update changes only journal_number, festgeschrieben_at and period_id;
        // every frozen business column remains untouched for the whitelist trigger.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PeriodLockResult.Success(period.Id, entries.Count);
    }
}

/// <summary>Typed outcome for the owner-only period-lock command.</summary>
public sealed record PeriodLockResult(
    PeriodLockStatus Status,
    Guid? PeriodId = null,
    int EntriesLocked = 0,
    string? Error = null)
{
    public static PeriodLockResult Success(Guid periodId, int entriesLocked) =>
        new(PeriodLockStatus.Success, periodId, entriesLocked);

    public static PeriodLockResult Conflict(Guid periodId, string error) =>
        new(PeriodLockStatus.Conflict, periodId, Error: error);

    public static PeriodLockResult Invalid(string error) =>
        new(PeriodLockStatus.Invalid, Error: error);
}

/// <summary>Finite outcomes exposed by Festschreibung.</summary>
public enum PeriodLockStatus
{
    Success,
    Conflict,
    Invalid,
}

internal sealed record FestschreibungAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(FiscalPeriod);
}
