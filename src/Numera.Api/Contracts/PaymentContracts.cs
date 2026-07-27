using Numera.Modules.Sales.Payments;

namespace Numera.Api.Contracts;

/// <summary>Records one incoming payment and its allocations. Enums cross the wire as numbers.</summary>
public sealed record RecordPaymentRequest(
    decimal? Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference,
    IReadOnlyList<PaymentAllocationInput> Allocations);

/// <summary>An amount of a payment allocated to one open item.</summary>
public sealed record PaymentAllocationInput(Guid OpenItemId, decimal Amount);

/// <summary>A payment row returned by the paged payment list. Enums cross the wire as numbers.</summary>
public sealed record PaymentListItem(
    Guid Id,
    decimal Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference,
    Guid? ReversesPaymentId,
    DateTimeOffset RecordedAt);
