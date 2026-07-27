namespace Numera.Modules.Sales.Payments;

/// <summary>
/// How an incoming payment (Zahlungseingang) was received. A booked-fact attribute of a
/// <see cref="Payment"/>; purely descriptive (it does not change the money math).
/// </summary>
/// <remarks>
/// The ordinals are APPEND-ONLY and cross the wire as NUMBERS (the Api has no
/// <c>JsonStringEnumConverter</c>) — the TypeScript mirror keys off these exact wire numbers
/// (RESEARCH.md Pitfall 5). NEVER reorder or renumber an existing member: append new methods at
/// the end only, or existing stored rows + the frontend badge map silently drift.
/// </remarks>
public enum PaymentMethod
{
    /// <summary>SEPA/other bank transfer (Überweisung). Wire number 0.</summary>
    BankTransfer = 0,

    /// <summary>Cash (Barzahlung). Wire number 1.</summary>
    Cash = 1,

    /// <summary>Card payment (EC-/Kreditkarte). Wire number 2.</summary>
    Card = 2,

    /// <summary>SEPA direct debit (Lastschrift). Wire number 3.</summary>
    Sepa = 3,

    /// <summary>Any other method (PayPal, cheque, …). Wire number 4.</summary>
    Other = 4,
}
