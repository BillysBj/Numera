using Numera.Platform.Money;

namespace Numera.Modules.Sales.Vat;

/// <summary>
/// The minimal per-line VAT input the <see cref="VatCalculationService"/> needs: the
/// EN 16931 tax category (BT-151), the applicable VAT rate (BT-152) and the line net
/// amount (BT-131, allowances/charges already applied). Prices, quantities and text are
/// deliberately out of scope — the service only buckets and taxes net amounts.
/// </summary>
/// <param name="Category">EN 16931 VAT category (S standard, AE §13b, K intra-EU, E §19/exempt, Z zero, G export, O out-of-scope).</param>
/// <param name="RatePercent">The VAT rate as a percentage (e.g. <c>19</c>); ignored for non-standard categories, which are always treated as rate 0.</param>
/// <param name="LineNetAmount">The line's net (taxable) amount the category rate applies to.</param>
public readonly record struct VatLineInput(TaxCategory Category, decimal RatePercent, decimal LineNetAmount);
