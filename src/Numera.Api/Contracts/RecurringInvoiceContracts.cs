using Numera.Modules.Sales.Recurring;
using Numera.Platform.Money;

namespace Numera.Api.Contracts;

public sealed record UpsertRecurringInvoiceTemplateRequest(
    string Name,
    Guid? PartnerId,
    string? Currency,
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    RecurringIntervalUnit IntervalUnit,
    int IntervalCount,
    DateOnly StartOn,
    RecurringEndMode EndMode,
    DateOnly? EndDate,
    int? MaxOccurrences,
    bool AutoFinalize,
    bool AutoSend,
    RecurringStatus Status,
    IReadOnlyList<RecurringInvoiceTemplateLineRequest> Lines);

public sealed record RecurringInvoiceTemplateLineRequest(
    Guid? CatalogItemId,
    string Name,
    string? Description,
    decimal Quantity,
    string UnitCode,
    decimal NetUnitPrice,
    TaxCategory TaxCategory,
    decimal VatRatePercent);

public sealed record RecurringInvoiceTemplateResponse(
    Guid Id,
    string Name,
    Guid? PartnerId,
    string Currency,
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    RecurringIntervalUnit IntervalUnit,
    int IntervalCount,
    DateOnly StartOn,
    RecurringEndMode EndMode,
    DateOnly? EndDate,
    int? MaxOccurrences,
    DateOnly NextRunOn,
    DateOnly? LastGeneratedPeriodEnd,
    int GeneratedCount,
    RecurringStatus Status,
    bool AutoFinalize,
    bool AutoSend,
    IReadOnlyList<RecurringInvoiceTemplateLineResponse> Lines);

public sealed record RecurringInvoiceTemplateLineResponse(
    Guid Id,
    int LineNumber,
    Guid? CatalogItemId,
    string Name,
    string? Description,
    decimal Quantity,
    string UnitCode,
    decimal NetUnitPrice,
    TaxCategory TaxCategory,
    decimal VatRatePercent);
