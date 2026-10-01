namespace Numera.Modules.Sales.Belege;

/// <summary>Settlement state of a booked supplier receipt.</summary>
public enum ReceiptPaymentStatus
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
}
