namespace Numera.Modules.Sales.Vat;

/// <summary>
/// One EN 16931 <b>VAT breakdown</b> row (BG-23) — a single (category, rate) bucket with
/// its taxable base (BT-116), the per-category rounded tax (BT-117), and, for zero-rate
/// categories, the exemption reason code (BT-121) and mandatory German note text (BT-120).
/// </summary>
/// <remarks>
/// The document VAT total is the SUM of the <see cref="TaxAmount"/> values across all rows
/// (each already rounded per category) — never a re-rounding of the grand total; see
/// <see cref="VatCalculationService.DocumentVatTotal"/> (EN 16931 BR-CO-14).
/// </remarks>
/// <param name="Category">The EN 16931 VAT category of the bucket (BT-118).</param>
/// <param name="RatePercent">The VAT rate of the bucket (BT-119); 0 for every non-standard category.</param>
/// <param name="TaxableBase">Sum of the line net amounts in the bucket (BT-116).</param>
/// <param name="TaxAmount">Per-category VAT rounded half-away-from-zero (BT-117); 0 for non-standard categories.</param>
/// <param name="ExemptionCode">EN 16931 VATEX exemption reason code (BT-121), or null for taxed/zero-rated-without-reason rows.</param>
/// <param name="ExemptionText">Mandatory German Pflichttext / exemption reason (BT-120), or null when no note applies.</param>
public readonly record struct VatBreakdownRow(
    Numera.Platform.Money.TaxCategory Category,
    decimal RatePercent,
    decimal TaxableBase,
    decimal TaxAmount,
    string? ExemptionCode,
    string? ExemptionText);
