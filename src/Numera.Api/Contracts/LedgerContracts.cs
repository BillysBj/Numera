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

/// <summary>One summarized row in the Buchungsjournal.</summary>
public sealed record JournalEntryListItem(
    Guid Id,
    string? JournalNumber,
    DateOnly EntryDate,
    LedgerSourceType SourceType,
    string SourceRef,
    string Description,
    PostingType PostingType,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsFestgeschrieben);

/// <summary>One account movement with its natural-side running balance.</summary>
public sealed record AccountStatementRow(
    Guid PostingId,
    DateOnly EntryDate,
    string? JournalNumber,
    string? CounterAccount,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>A keyset-paginated read result.</summary>
public sealed record PagedEnvelope<T>(
    IReadOnlyList<T> Items,
    Guid? NextCursor,
    decimal? OpeningBalance = null);
