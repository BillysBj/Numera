namespace Numera.Platform.Money;

/// <summary>
/// A taxable amount grouped by its VAT rate and EN 16931 category. The document
/// VAT total is computed from a collection of these buckets — each rounded
/// independently, then summed.
/// </summary>
/// <param name="TaxableBase">The net base the rate applies to (allowances already deducted).</param>
/// <param name="RatePercent">The VAT rate as a percentage (e.g. <c>19</c> for 19%).</param>
/// <param name="Category">The EN 16931 tax category the bucket belongs to.</param>
public readonly record struct TaxBucket(decimal TaxableBase, decimal RatePercent, TaxCategory Category);

/// <summary>
/// The single, central VAT rounding authority for the platform. Rounding mode
/// and order are legally load-bearing: banker's rounding or per-document
/// rounding cause KoSIT rejections and are un-retrofittable once amounts are
/// persisted, so all callers MUST route VAT math through this policy.
/// </summary>
public static class RoundingPolicy
{
    /// <summary>
    /// Rounds the VAT for a single category to 2 decimal places using
    /// half-away-from-zero (kaufmännische Rundung), never banker's ToEven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per EN 16931 the tax amount for a category (BT-117) is the taxable base
    /// (BT-116) multiplied by the rate (BT-119), rounded to two decimals — see
    /// business rules BR-S-08 and BR-CO-17. This implementation locks the half
    /// case to <see cref="MidpointRounding.AwayFromZero"/>, so an unrounded
    /// x.xx5 rounds up (0.125 -&gt; 0.13), diverging from ToEven (0.12).
    /// </para>
    /// <para>
    /// AwayFromZero is the correct default here, but its universality is to be
    /// RE-LOCKED against KoSIT-validated reference invoices in the e-invoicing
    /// phase (RESEARCH Open Question 1). The golden-file suite is the regression
    /// net guarding that decision.
    /// </para>
    /// </remarks>
    /// <param name="taxableBase">The net base the rate applies to.</param>
    /// <param name="ratePercent">The VAT rate as a percentage (e.g. <c>19</c>).</param>
    /// <returns>The category VAT, rounded to 2 decimal places away from zero.</returns>
    public static decimal RoundTax(decimal taxableBase, decimal ratePercent)
        => Math.Round(taxableBase * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Computes the document VAT total as the SUM of the per-category rounded
    /// amounts — never <c>Math.Round(grandTotal, 2)</c>. This ordering is what
    /// EN 16931 (BR-CO-14: document tax total = sum of category tax amounts)
    /// requires; rounding the combined base would diverge by cents.
    /// </summary>
    /// <param name="buckets">The taxable buckets making up the document.</param>
    /// <returns>The document VAT total as the sum of rounded per-category tax.</returns>
    public static decimal DocumentVatTotal(IEnumerable<TaxBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        return buckets.Sum(bucket => RoundTax(bucket.TaxableBase, bucket.RatePercent));
    }
}
