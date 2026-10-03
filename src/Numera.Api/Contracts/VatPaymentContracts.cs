using Numera.Modules.Ledger.Tax;

namespace Numera.Api.Contracts;

/// <summary>A cash payment to or refund/offset from the Finanzamt.</summary>
public sealed record RecordVatPaymentRequest(
    decimal? Amount, VatPaymentKind Kind, DateOnly ValueDate, string? Reference);

/// <summary>A settlement or its append-only reversal. Amount is always positive.</summary>
public sealed record VatPaymentListItem(
    Guid Id, decimal Amount, VatPaymentKind Kind, DateOnly ValueDate,
    string? Reference, Guid? ReversesPaymentId, DateTimeOffset RecordedAt);

/// <summary>Finanzamt settlements, newest recorded first.</summary>
public sealed record VatPaymentListResponse(IReadOnlyList<VatPaymentListItem> Items);
