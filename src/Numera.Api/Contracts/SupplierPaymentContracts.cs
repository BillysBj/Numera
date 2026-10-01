using Numera.Modules.Sales.Payments;

namespace Numera.Api.Contracts;

/// <summary>Records an outgoing payment allocated to the receipt in the route.</summary>
public sealed record RecordSupplierPaymentRequest(
    decimal? Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference);
