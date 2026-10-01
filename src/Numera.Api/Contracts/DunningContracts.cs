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

/// <summary>Summary of a manually initiated tenant dunning run.</summary>
public sealed record DunningRunResponse(int Issued, int Skipped);

/// <summary>Issued notice; monetary claims and numeric dispatch status are frozen facts.</summary>
public sealed record DunningNoticeListItem(
    Guid Id, string DocumentNumber, string? Recipient, int Level, DateOnly IssuedOn,
    decimal Fee, decimal TotalToPay, string Currency, int Status);

public sealed record DunningNoticePage(IReadOnlyList<DunningNoticeListItem> Items, int Total);
