using Numera.Modules.Sales.Payments;

namespace Numera.Api.Contracts;

/// <summary>Records an outgoing payment allocated to the receipt in the route.</summary>
public sealed record RecordSupplierPaymentRequest(
    decimal? Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference);

/// <summary>One supplier payment (or reversal) recorded against the receipt.</summary>
public sealed record SupplierPaymentListItem(
    Guid Id,
    decimal Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference,
    Guid? ReversesPaymentId,
    DateTimeOffset RecordedAt);

/// <summary>All supplier payments recorded against a single receipt, oldest first.</summary>
public sealed record SupplierPaymentListResponse(IReadOnlyList<SupplierPaymentListItem> Items);
