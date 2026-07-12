using Numera.Platform.Money;

namespace Numera.Modules.Sales.Vat;

/// <summary>
/// The single authority that turns invoice lines into the EN 16931 VAT breakdown (BG-23).
/// Every place VAT is computed — invoice finalize (03-05), the Phase-4 PDF and the Phase-5
/// e-invoice — routes through here so the arithmetic and the mandatory Pflichttexte have
/// exactly one implementation (RESEARCH.md Q5, LOCKED). Pure and stateless: no DbContext,
/// no I/O — so it is freely reusable and golden-file testable.
/// </summary>
/// <remarks>
/// Algorithm (LOCKED):
/// <list type="bullet">
/// <item>Group lines by (Category, effective rate). Taxable base = Σ line nets in the bucket.</item>
/// <item>Only the standard category <c>S</c> is taxed; its tax = <see cref="RoundingPolicy.RoundTax"/>
/// (half-away-from-zero, per category). Every other category is treated as rate 0 / tax 0 and
/// carries its <see cref="Pflichttext"/> code + note.</item>
/// <item>Document VAT total = SUM of the per-category rounded amounts — never
/// <c>Math.Round(grandTotal)</c> (EN 16931 BR-CO-14).</item>
/// <item>Kleinunternehmer §19 is a document-wide issuer property: when set it OVERRIDES every
/// per-line category into one <c>E</c> / rate-0 / zero-tax bucket (RESEARCH.md Pitfall 3).</item>
/// </list>
/// </remarks>
public static class VatCalculationService
{
    /// <summary>
    /// Buckets <paramref name="lines"/> into EN 16931 BG-23 breakdown rows.
    /// </summary>
    /// <param name="lines">The document's per-line VAT inputs (net amounts + category + rate).</param>
    /// <param name="isKleinunternehmer">
    /// The issuer's §19 status. When true, all lines collapse into a single exempt
    /// (category <c>E</c>, rate 0, zero-tax) row with the §19 note, regardless of per-line category.
    /// </param>
    /// <returns>One <see cref="VatBreakdownRow"/> per distinct taxed/exempt bucket.</returns>
    public static IReadOnlyList<VatBreakdownRow> Calculate(
        IEnumerable<VatLineInput> lines,
        bool isKleinunternehmer)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var materialized = lines as IReadOnlyList<VatLineInput> ?? lines.ToList();

        // Kleinunternehmer §19 override: one exempt bucket for the whole document.
        if (isKleinunternehmer)
        {
            var totalNet = materialized.Sum(l => l.LineNetAmount);
            return [BuildRow(TaxCategory.E, 0m, totalNet)];
        }

        return materialized
            .GroupBy(l => new { l.Category, Rate = EffectiveRate(l.Category, l.RatePercent) })
            .Select(g => BuildRow(g.Key.Category, g.Key.Rate, g.Sum(l => l.LineNetAmount)))
            .ToList();
    }

    /// <summary>
    /// The document VAT total: the SUM of the already-per-category-rounded row amounts.
    /// Never re-rounds the grand total (EN 16931 BR-CO-14).
    /// </summary>
    public static decimal DocumentVatTotal(IEnumerable<VatBreakdownRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Sum(r => r.TaxAmount);
    }

    /// <summary>Only the standard category keeps its rate; every other category is rate 0.</summary>
    private static decimal EffectiveRate(TaxCategory category, decimal ratePercent)
        => category == TaxCategory.S ? ratePercent : 0m;

    private static VatBreakdownRow BuildRow(TaxCategory category, decimal ratePercent, decimal taxableBase)
    {
        // Only the standard category produces tax; all others are zero-rate + Pflichttext.
        var taxAmount = category == TaxCategory.S
            ? RoundingPolicy.RoundTax(taxableBase, ratePercent)
            : 0m;

        var (code, text) = Pflichttext.For(category);

        return new VatBreakdownRow(category, ratePercent, taxableBase, taxAmount, code, text);
    }
}
