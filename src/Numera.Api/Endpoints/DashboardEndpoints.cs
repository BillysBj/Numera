using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>
/// Read-only aggregates for the overview (Dashboard). All queries are RLS-scoped to the
/// current tenant (query filters + Postgres RLS); nothing here mutates. Figures are an
/// overview estimate — the authoritative books live in the USt-VA / EÜR reports.
/// </summary>
public static class DashboardEndpoints
{
    // "Umsatz" counts issued (finalized/sent/paid) final invoices — Rechnung and
    // Schlussrechnung. Abschlagsrechnungen are interim and excluded to avoid double counting
    // against the Schlussrechnung that nets them.
    private static readonly DocumentType[] RevenueTypes =
        [DocumentType.Rechnung, DocumentType.Schlussrechnung];

    private static readonly DocumentStatus[] IssuedStatuses =
        [DocumentStatus.Finalized, DocumentStatus.Sent, DocumentStatus.Paid];

    /// <summary>Maps <c>GET /api/dashboard</c>.</summary>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var yearStart = new DateOnly(today.Year, 1, 1);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            // First day of the month 11 months ago → a rolling 12-month window incl. this month.
            var windowStart = monthStart.AddMonths(-11);

            // --- Revenue (net) from issued final invoices in the 12-month window ---
            var invoiceRows = await db.Set<SalesDocument>().AsNoTracking()
                .Where(d => RevenueTypes.Contains(d.DocumentType)
                    && IssuedStatuses.Contains(d.Status)
                    && d.DocumentDate >= windowStart)
                .Select(d => new { d.DocumentDate, d.TotalNet })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var months = new List<MonthPoint>(12);
            for (var i = 0; i < 12; i++)
            {
                var m = windowStart.AddMonths(i);
                var sum = invoiceRows
                    .Where(r => r.DocumentDate.Year == m.Year && r.DocumentDate.Month == m.Month)
                    .Sum(r => r.TotalNet);
                months.Add(new MonthPoint($"{m.Year:0000}-{m.Month:00}", sum));
            }

            var revenueYear = invoiceRows
                .Where(r => r.DocumentDate >= yearStart)
                .Sum(r => r.TotalNet);

            // --- Open items: outstanding + aging buckets ---
            var openRows = await db.Set<OpenItem>().AsNoTracking()
                .Where(o => o.Status == OpenItemStatus.Open || o.Status == OpenItemStatus.PartiallyPaid)
                .Select(o => new { o.OpenAmount, o.DueDate })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            decimal notDue = 0, d1_30 = 0, d31_60 = 0, d60Plus = 0;
            foreach (var o in openRows)
            {
                var daysOver = today.DayNumber - o.DueDate.DayNumber;
                if (daysOver <= 0) notDue += o.OpenAmount;
                else if (daysOver <= 30) d1_30 += o.OpenAmount;
                else if (daysOver <= 60) d31_60 += o.OpenAmount;
                else d60Plus += o.OpenAmount;
            }

            var openTotal = notDue + d1_30 + d31_60 + d60Plus;
            var overdue = d1_30 + d31_60 + d60Plus;

            // --- Documents issued this month (non-draft) ---
            var documentsThisMonth = await db.Set<SalesDocument>().AsNoTracking()
                .CountAsync(d => d.DocumentDate >= monthStart && d.Status != DocumentStatus.Draft, ct)
                .ConfigureAwait(false);

            // --- Document status breakdown ---
            var statusRows = await db.Set<SalesDocument>().AsNoTracking()
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            int Count(params DocumentStatus[] statuses) =>
                statusRows.Where(r => statuses.Contains(r.Status)).Sum(r => r.Count);

            var docStatus = new DocStatusCounts(
                Draft: Count(DocumentStatus.Draft),
                Finalized: Count(DocumentStatus.Finalized, DocumentStatus.Sent),
                Paid: Count(DocumentStatus.Paid),
                Cancelled: Count(DocumentStatus.Cancelled));

            return Results.Ok(new DashboardResponse(
                revenueYear,
                openTotal,
                overdue,
                documentsThisMonth,
                months,
                new AgingBuckets(notDue, d1_30, d31_60, d60Plus),
                docStatus));
        })
        .RequireAuthorization();

        return app;
    }
}

/// <summary>Overview aggregates for the dashboard (all amounts are net EUR unless noted).</summary>
public sealed record DashboardResponse(
    decimal RevenueYear,
    decimal OpenItemsTotal,
    decimal OverdueTotal,
    int DocumentsThisMonth,
    IReadOnlyList<MonthPoint> RevenueByMonth,
    AgingBuckets Aging,
    DocStatusCounts DocStatus);

/// <summary>One month's net revenue; <paramref name="Month"/> is "yyyy-MM".</summary>
public sealed record MonthPoint(string Month, decimal Net);

/// <summary>Outstanding open-item amounts bucketed by days overdue.</summary>
public sealed record AgingBuckets(decimal NotDue, decimal D1_30, decimal D31_60, decimal D60Plus);

/// <summary>Document counts by lifecycle state (Finalized folds in Sent).</summary>
public sealed record DocStatusCounts(int Draft, int Finalized, int Paid, int Cancelled);
