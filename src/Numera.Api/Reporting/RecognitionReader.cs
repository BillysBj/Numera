using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Api.Reporting;

/// <summary>
/// Reads the frozen accounting facts used to recognize report amounts under either
/// Soll-Versteuerung or payment-date cash recognition.
/// </summary>
/// <remarks>
/// The raw SQL is automatically tenant-scoped by PostgreSQL RLS after
/// <c>TenantConnectionInterceptor</c> binds the ambient tenant to the connection.
/// No report query adds its own tenant predicate.
/// </remarks>
public sealed class RecognitionReader(NumeraDbContext db)
{
    /// <summary>
    /// Sums tax-bearing postings by USt-VA Kennziffer and frozen posting tax metadata
    /// for journal entries whose booking date falls in the inclusive range.
    /// </summary>
    public async Task<IReadOnlyList<SollRecognitionRow>> ReadSollAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        // Storno entries retain the frozen tax metadata and reverse the posting
        // direction, so callers can net the grouped directions without a special case.
        var rows = await db.Database.SqlQuery<SollRecognitionRow>(
            $"""
            SELECT a.ustva_kennziffer AS "Kennziffer",
                   a.type AS "AccountType",
                   p.tax_category AS "TaxCategory",
                   p.tax_rate_percent AS "TaxRatePercent",
                   p.direction AS "Direction",
                   SUM(p.amount) AS "Amount"
              FROM postings p
              JOIN journal_entries je ON je.id = p.journal_entry_id
              JOIN accounts a ON a.id = p.account_id
             WHERE je.entry_date >= {from}
               AND je.entry_date <= {to}
               AND a.ustva_kennziffer IS NOT NULL
             GROUP BY a.ustva_kennziffer,
                      a.type,
                      p.tax_category,
                      p.tax_rate_percent,
                      p.direction
             ORDER BY a.ustva_kennziffer,
                      a.type,
                      p.tax_category,
                      p.tax_rate_percent,
                      p.direction
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows;
    }

    /// <summary>
    /// Recognizes each allocated share of an invoice's frozen tax breakdown on the
    /// payment value date. This same read powers both Ist-USt-VA (Plan 02) and EÜR
    /// cash-basis income (Plan 03), preventing either report from counting both the
    /// invoice booking and its payment booking.
    /// </summary>
    public async Task<IReadOnlyList<CashRecognitionRow>> ReadCashRecognitionAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        // Reversal allocations are negative. Their negative pro-rata factor therefore
        // nets recognition out on the reversal payment's own value date without filtering.
        var rows = await db.Database.SqlQuery<CashRecognitionRow>(
            $"""
            SELECT b.tax_category AS "TaxCategory",
                   b.vat_rate_percent AS "VatRatePercent",
                   (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.taxable_base AS "NetAmount",
                   (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.tax_amount AS "VatAmount",
                   p.value_date AS "RecognizedOn"
              FROM payment p
              JOIN payment_allocation pa ON pa.payment_id = p.id
              JOIN open_items oi ON oi.id = pa.open_item_id
              JOIN sales_documents sd ON sd.id = oi.document_id
              JOIN sales_document_tax_breakdown b ON b.document_id = sd.id
             WHERE p.value_date >= {from}
               AND p.value_date <= {to}
             ORDER BY p.value_date,
                      p.id,
                      pa.id,
                      b.tax_category,
                      b.vat_rate_percent,
                      b.id
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows;
    }
}
