using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Sales.Dunning;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Reads, seeds and replaces the current tenant's dunning ladder.</summary>
public sealed class DunningConfigService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAuditWriter _audit;

    /// <summary>Creates the scoped configuration service.</summary>
    public DunningConfigService(NumeraDbContext db, ICurrentTenant currentTenant, IAuditWriter audit)
    {
        _db = db;
        _currentTenant = currentTenant;
        _audit = audit;
    }

    /// <summary>Gets the ladder, materializing the German defaults when it is absent.</summary>
    public async Task<IReadOnlyList<DunningLevelConfigDto>> GetConfigAsync(CancellationToken ct = default)
    {
        var levels = await _db.Set<DunningLevelConfig>()
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (levels.Count > 0)
        {
            return levels.Select(ToDto).ToList();
        }

        var tenantId = RequireTenant();
        foreach (var item in DunningDefaults.Levels)
        {
            _db.Add(new DunningLevelConfig
            {
                TenantId = tenantId,
                Level = item.Level,
                Name = item.Name,
                DaysAfterDue = item.DaysAfterDue,
                Fee = item.Fee,
                ChargeInterest = item.ChargeInterest,
                InterestRatePercent = DunningDefaults.InterestRatePercent,
                TemplateTextDe = item.TemplateTextDe,
                TemplateTextEn = item.TemplateTextEn,
            });
        }

        var seeded = _db.ChangeTracker.Entries<DunningLevelConfig>()
            .Select(e => e.Entity)
            .OrderBy(x => x.Level)
            .ToList();
        await _audit.RecordAsync(
            new DunningConfigAuditEvent("dunning.config.seeded", null, null, Snapshot(seeded)), ct)
            .ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return seeded.Select(ToDto).ToList();
    }

    /// <summary>Validates and atomically replaces the complete tenant ladder.</summary>
    public async Task<DunningConfigUpdateResult> UpsertConfigAsync(
        IReadOnlyList<DunningLevelConfigDto> levels,
        CancellationToken ct = default)
    {
        var errors = Validate(levels);
        if (errors.Count > 0)
        {
            return DunningConfigUpdateResult.Invalid(errors);
        }

        var tenantId = RequireTenant();
        var existing = await _db.Set<DunningLevelConfig>()
            .OrderBy(x => x.Level)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var before = Snapshot(existing);
        _db.RemoveRange(existing);

        var replacements = levels.Select(x => new DunningLevelConfig
        {
            TenantId = tenantId,
            Level = x.Level,
            Name = x.Name.Trim(),
            DaysAfterDue = x.DaysAfterDue,
            Fee = x.Fee,
            ChargeInterest = x.ChargeInterest,
            InterestRatePercent = x.InterestRatePercent,
            TemplateTextDe = x.TemplateTextDe.Trim(),
            TemplateTextEn = x.TemplateTextEn.Trim(),
        }).ToList();
        foreach (var replacement in replacements)
        {
            _db.Add(replacement);
        }

        await _audit.RecordAsync(
            new DunningConfigAuditEvent("dunning.config.updated", null, before, Snapshot(replacements)), ct)
            .ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return DunningConfigUpdateResult.Ok(replacements.Select(ToDto).ToList());
    }

    private Guid RequireTenant() => _currentTenant.TenantId
        ?? throw new InvalidOperationException("A tenant is required to configure dunning.");

    private static Dictionary<string, string[]> Validate(IReadOnlyList<DunningLevelConfigDto> levels)
    {
        var errors = new Dictionary<string, string[]>();
        if (levels.Count == 0 || levels.Select(x => x.Level).Order().Where((level, index) => level != index).Any())
        {
            errors["levels"] = ["Die Mahnstufen müssen lückenlos bei 0 beginnen."];
        }

        if (levels.Any(x => x.DaysAfterDue < 0) ||
            levels.OrderBy(x => x.Level).Select(x => x.DaysAfterDue).Zip(
                levels.OrderBy(x => x.Level).Skip(1).Select(x => x.DaysAfterDue),
                (left, right) => right < left).Any(x => x))
        {
            errors["daysAfterDue"] = ["Fristen müssen nichtnegativ und nach Stufe nicht fallend sein."];
        }

        if (levels.Any(x => x.Fee < 0m))
        {
            errors["fee"] = ["Gebühren dürfen nicht negativ sein."];
        }

        if (levels.Any(x => x.InterestRatePercent < 0m))
        {
            errors["interestRatePercent"] = ["Zinssätze dürfen nicht negativ sein."];
        }

        if (levels.Any(x => string.IsNullOrWhiteSpace(x.Name) ||
                            string.IsNullOrWhiteSpace(x.TemplateTextDe) ||
                            string.IsNullOrWhiteSpace(x.TemplateTextEn)))
        {
            errors["text"] = ["Name sowie deutsche und englische Vorlagentexte sind erforderlich."];
        }

        return errors;
    }

    private static DunningLevelConfigDto ToDto(DunningLevelConfig x) => new(
        x.Level, x.Name, x.DaysAfterDue, x.Fee, x.ChargeInterest,
        x.InterestRatePercent, x.TemplateTextDe, x.TemplateTextEn);

    private static string Snapshot(IEnumerable<DunningLevelConfig> levels) =>
        JsonSerializer.Serialize(levels.Select(ToDto), AuditJson);
}

/// <summary>Typed result used by the HTTP endpoint for success or validation errors.</summary>
public sealed record DunningConfigUpdateResult(
    bool IsValid,
    IReadOnlyList<DunningLevelConfigDto>? Levels,
    Dictionary<string, string[]> Errors)
{
    /// <summary>Creates a successful result.</summary>
    public static DunningConfigUpdateResult Ok(IReadOnlyList<DunningLevelConfigDto> levels) =>
        new(true, levels, []);

    /// <summary>Creates a validation failure.</summary>
    public static DunningConfigUpdateResult Invalid(Dictionary<string, string[]> errors) =>
        new(false, null, errors);
}

/// <summary>Dunning configuration audit mutation; tenant and actor are stamped by the writer.</summary>
internal sealed record DunningConfigAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(DunningLevelConfig);
}
