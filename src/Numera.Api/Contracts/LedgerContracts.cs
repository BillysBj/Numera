using Numera.Modules.Ledger;

namespace Numera.Api.Contracts;

/// <summary>The tenant's one-time ledger setup choices.</summary>
public sealed record LedgerSetupRequest(
    ChartVariant ChartVariant,
    Besteuerungsart Besteuerungsart,
    Gewinnermittlungsart Gewinnermittlungsart,
    int? FiscalYearStartMonth);

/// <summary>The tenant's persisted ledger configuration.</summary>
public sealed record LedgerSettingsDto(
    Guid Id,
    ChartVariant ChartVariant,
    Besteuerungsart Besteuerungsart,
    Gewinnermittlungsart Gewinnermittlungsart,
    int FiscalYearStartMonth);

/// <summary>The configuration and chart size created by ledger setup.</summary>
public sealed record LedgerSetupResponse(LedgerSettingsDto Settings, int AccountCount);
