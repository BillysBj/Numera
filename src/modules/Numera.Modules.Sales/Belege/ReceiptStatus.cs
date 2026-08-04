namespace Numera.Modules.Sales.Belege;

/// <summary>The human-controlled lifecycle of a received receipt.</summary>
public enum ReceiptStatus
{
    Captured,
    Extracted,
    Reviewed,
    Booked,
    Duplicate,
    Rejected,
    Quarantined,
}

/// <summary>The channel through which a receipt entered Numera.</summary>
public enum ReceiptSource
{
    Camera,
    Upload,
    Email,
    EInvoice,
}
