namespace Numera.Modules.Sales.Pdf;

/// <summary>Flat, presentation-ready dunning notice projection built from frozen invoice data.</summary>
public sealed record DunningNoticeModel
{
    public string Language { get; init; } = "de";
    public byte[]? LogoBytes { get; init; }
    public required InvoicePdfModel.IssuerBlock Issuer { get; init; }
    public required InvoicePdfModel.RecipientBlock Recipient { get; init; }
    public required string InvoiceNumber { get; init; }
    public DateOnly InvoiceDate { get; init; }
    public string Currency { get; init; } = "EUR";
    public decimal OverdueAmount { get; init; }
    public decimal Fee { get; init; }
    public decimal Interest { get; init; }
    public decimal InterestRatePercent { get; init; }
    public int DaysOverdue { get; init; }
    public decimal TotalToPay { get; init; }
    public DateOnly NewDueDate { get; init; }
    public required string LevelName { get; init; }
    public required string TemplateText { get; init; }
    public bool IsFinalNotice { get; init; }
}
