namespace Numera.Api.Contracts;

/// <summary>Editable configuration for one dunning level.</summary>
public sealed record DunningLevelConfigDto(
    int Level,
    string Name,
    int DaysAfterDue,
    decimal Fee,
    bool ChargeInterest,
    decimal InterestRatePercent,
    string TemplateTextDe,
    string TemplateTextEn);

/// <summary>Complete tenant dunning configuration response.</summary>
public sealed record DunningConfigResponse(IReadOnlyList<DunningLevelConfigDto> Levels);

/// <summary>Replaces the tenant's complete dunning ladder.</summary>
public sealed record UpdateDunningConfigRequest(IReadOnlyList<DunningLevelConfigDto> Levels);
