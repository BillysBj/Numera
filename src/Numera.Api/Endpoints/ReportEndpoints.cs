using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Pdf;
using Numera.Api.Reporting;
using Numera.Api.Reporting.Elster;
using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated USt-VA and EÜR review, drill-down and export endpoints.</summary>
public static class ReportEndpoints
{
    /// <summary>Maps tenant-scoped report reads and downloads below <c>/api/reports</c>.</summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization();

        // Kleinunternehmer receive HTTP 200 with the calculator's explicit gate shape:
        // isKleinunternehmer=true, empty lines and no fabricated declaration values.
        group.MapGet("/ustva", async (
            int jahr,
            string zeitraum,
            UstVaCalculator calculator,
            CancellationToken ct) =>
            Results.Ok(await GetUstVaAsync(jahr, zeitraum, calculator, ct).ConfigureAwait(false)));

        group.MapGet("/ustva/kz/{kz}/entries", async (
            string kz,
            int jahr,
            string zeitraum,
            NumeraDbContext db,
            UstVaCalculator calculator,
            CancellationToken ct) =>
        {
            var result = await GetUstVaEntriesAsync(
                kz, jahr, zeitraum, db, calculator, ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/euer", async (
            int jahr,
            DateOnly from,
            DateOnly to,
            EuerCalculator calculator,
            CancellationToken ct) =>
            Results.Ok(await GetEuerAsync(jahr, from, to, calculator, ct).ConfigureAwait(false)));

        group.MapGet("/ustva/export.xml", async (
            int jahr,
            string zeitraum,
            NumeraDbContext db,
            UstVaCalculator calculator,
            CancellationToken ct) =>
        {
            var export = await ExportUstVaXmlAsync(
                jahr, zeitraum, db, calculator, ct).ConfigureAwait(false);
            return export is null
                ? (IResult)Results.Conflict(new
                {
                    isKleinunternehmer = true,
                    message = "Kleinunternehmer geben keine USt-Voranmeldung ab.",
                })
                : Results.File(export.Bytes, export.ContentType, export.FileName);
        });

        group.MapGet("/ustva/export.pdf", async (
            int jahr,
            string zeitraum,
            UstVaCalculator calculator,
            CancellationToken ct) =>
        {
            var export = await ExportUstVaPdfAsync(
                jahr, zeitraum, calculator, ct).ConfigureAwait(false);
            return export is null
                ? (IResult)Results.Conflict(new
                {
                    isKleinunternehmer = true,
                    message = "Kleinunternehmer geben keine USt-Voranmeldung ab.",
                })
                : Results.File(export.Bytes, export.ContentType, export.FileName);
        });

        group.MapGet("/euer/export.pdf", async (
            int jahr,
            DateOnly from,
            DateOnly to,
            EuerCalculator calculator,
            CancellationToken ct) =>
        {
            var export = await ExportEuerPdfAsync(
                jahr, from, to, calculator, ct).ConfigureAwait(false);
            return Results.File(export.Bytes, export.ContentType, export.FileName);
        });

        return app;
    }

    internal static Task<UstVaReport> GetUstVaAsync(
        int jahr,
        string zeitraum,
        UstVaCalculator calculator,
        CancellationToken ct) =>
        calculator.ComputeAsync(jahr, zeitraum, ct);

    internal static Task<EuerReport> GetEuerAsync(
        int jahr,
        DateOnly from,
        DateOnly to,
        EuerCalculator calculator,
        CancellationToken ct) =>
        calculator.ComputeAsync(jahr, from, to, ct);

    /// <summary>
    /// Returns either journal-date contributions (Soll) or invoice/payment attribution
    /// on the payment value date (Ist). The <c>recognitionBasis</c> discriminator lets a
    /// client render either source without mistaking an invoice date for cash recognition.
    /// </summary>
    internal static async Task<UstVaDrillDownResult?> GetUstVaEntriesAsync(
        string kz,
        int jahr,
        string zeitraum,
        NumeraDbContext db,
        UstVaCalculator calculator,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kz);
        var report = await calculator.ComputeAsync(jahr, zeitraum, ct).ConfigureAwait(false);
        if (report.IsKleinunternehmer)
        {
            return new UstVaDrillDownResult(
                kz,
                report.Besteuerungsart,
                RecognitionBasis: "gated",
                IsKleinunternehmer: true,
                Entries: []);
        }

        var line = report.Lines.SingleOrDefault(candidate => candidate.Kz == kz);
        if (line is null)
        {
            return null;
        }

        var (from, to) = ResolvePeriod(jahr, zeitraum);
        var definitions = UstVaKennzifferMap.ForFiscalYear(jahr);
        var sourceDefinitions = SourceDefinitions(definitions, line);
        var entries = report.Besteuerungsart == Besteuerungsart.Soll
            ? await ReadSollEntriesAsync(
                db, from, to, line.ContributingAccountNumbers, sourceDefinitions, ct)
                .ConfigureAwait(false)
            : await ReadIstEntriesAsync(db, from, to, sourceDefinitions, ct)
                .ConfigureAwait(false);

        return new UstVaDrillDownResult(
            kz,
            report.Besteuerungsart,
            RecognitionBasis: report.Besteuerungsart == Besteuerungsart.Soll
                ? "journalEntryDate"
                : "paymentValueDate",
            IsKleinunternehmer: false,
            Entries: entries);
    }

    internal static async Task<ReportExport?> ExportUstVaXmlAsync(
        int jahr,
        string zeitraum,
        NumeraDbContext db,
        UstVaCalculator calculator,
        CancellationToken ct)
    {
        var report = await calculator.ComputeAsync(jahr, zeitraum, ct).ConfigureAwait(false);
        if (report.IsKleinunternehmer)
        {
            return null;
        }

        var profile = await db.Set<CompanyProfile>()
            .AsNoTracking()
            .SingleAsync(ct)
            .ConfigureAwait(false);
        var bytes = UstVaXmlWriter.Write(report, profile);

        // Serialize generation per tenant/period. Repeated downloads reuse the existing
        // Draft snapshot instead of creating duplicate filing rows. A prior Submitted
        // filing is linked as the declaration corrected by the new Draft.
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var lockKey = $"ustva-filing:{profile.TenantId:D}:{jahr}:{zeitraum}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            ct).ConfigureAwait(false);

        var hasDraft = await db.Set<UstVaFiling>()
            .AnyAsync(candidate =>
                candidate.Jahr == jahr
                && candidate.Zeitraum == zeitraum
                && candidate.Status == UstVaFilingStatus.Draft,
                ct)
            .ConfigureAwait(false);
        if (!hasDraft)
        {
            var correctedFilingId = await db.Set<UstVaFiling>()
                .AsNoTracking()
                .Where(candidate =>
                    candidate.Jahr == jahr
                    && candidate.Zeitraum == zeitraum
                    && candidate.Status == UstVaFilingStatus.Submitted)
                .OrderByDescending(candidate => candidate.SubmittedAt)
                .ThenByDescending(candidate => candidate.CreatedAt)
                .Select(candidate => (Guid?)candidate.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            db.Add(new UstVaFiling
            {
                TenantId = profile.TenantId,
                Jahr = report.Jahr,
                Zeitraum = report.Zeitraum,
                Besteuerungsart = report.Besteuerungsart,
                KzSnapshotJson = JsonSerializer.Serialize(report.Lines),
                Zahllast = report.Zahllast,
                XmlBytes = bytes,
                Status = UstVaFilingStatus.Draft,
                CreatedAt = DateTimeOffset.UtcNow,
                BerichtigtVonFilingId = correctedFilingId,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new ReportExport(
            bytes,
            "application/xml; charset=iso-8859-15",
            $"ustva-{jahr}-{zeitraum}.xml");
    }

    internal static async Task<ReportExport?> ExportUstVaPdfAsync(
        int jahr,
        string zeitraum,
        UstVaCalculator calculator,
        CancellationToken ct)
    {
        var report = await calculator.ComputeAsync(jahr, zeitraum, ct).ConfigureAwait(false);
        return report.IsKleinunternehmer
            ? null
            : new ReportExport(
                UstVaDocument.Render(report),
                "application/pdf",
                $"ustva-{jahr}-{zeitraum}.pdf");
    }

    internal static async Task<ReportExport> ExportEuerPdfAsync(
        int jahr,
        DateOnly from,
        DateOnly to,
        EuerCalculator calculator,
        CancellationToken ct)
    {
        var report = await calculator.ComputeAsync(jahr, from, to, ct).ConfigureAwait(false);
        return new ReportExport(
            EuerDocument.Render(report),
            "application/pdf",
            $"euer-{jahr}.pdf");
    }

    private static async Task<IReadOnlyList<UstVaDrillDownEntry>> ReadSollEntriesAsync(
        NumeraDbContext db,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<string> accountNumbers,
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        CancellationToken ct)
    {
        if (accountNumbers.Count == 0)
        {
            return [];
        }

        var numbers = accountNumbers.ToArray();
        var rows = await db.Database.SqlQuery<SollDrillDownSqlRow>(
            $"""
            SELECT je.id AS "JournalEntryId",
                   je.journal_number AS "JournalNumber",
                   je.entry_date AS "EntryDate",
                   je.source_ref AS "SourceRef",
                   je.description AS "Description",
                   p.tax_category AS "TaxCategory",
                   p.tax_rate_percent AS "TaxRatePercent",
                   p.direction AS "Direction",
                   p.amount AS "Amount"
              FROM journal_entries je
              JOIN postings p ON p.journal_entry_id = je.id
              JOIN accounts a ON a.id = p.account_id
             WHERE je.entry_date >= {from}
               AND je.entry_date <= {to}
               AND a.number = ANY ({numbers})
             ORDER BY je.entry_date, je.id, p.id
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(row => new
            {
                Row = row,
                Definition = definitions.FirstOrDefault(definition =>
                    definition.RecognitionSelector is { } selector
                    && selector.TaxCategory == row.TaxCategory
                    && selector.TaxRatePercent == row.TaxRatePercent),
            })
            .Where(item => item.Definition is not null)
            .Select(item =>
            {
                var selector = item.Definition!.RecognitionSelector!;
                var signedNet = item.Row.Direction == selector.NaturalSide
                    ? item.Row.Amount
                    : -item.Row.Amount;
                return new UstVaDrillDownEntry(
                    Kind: "journal",
                    item.Row.JournalEntryId,
                    item.Row.JournalNumber,
                    item.Row.EntryDate,
                    item.Row.SourceRef,
                    item.Row.Description,
                    DocumentId: null,
                    DocumentNumber: null,
                    InvoiceDate: null,
                    PaymentId: null,
                    PaymentValueDate: null,
                    PaymentReference: null,
                    AttributedNet: signedNet,
                    AttributedVat: RoundingPolicy.RoundAmount(
                        signedNet * selector.TaxRatePercent / 100m));
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<UstVaDrillDownEntry>> ReadIstEntriesAsync(
        NumeraDbContext db,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<IstDrillDownSqlRow>(
            $"""
            SELECT sd.id AS "DocumentId",
                   oi.document_number AS "DocumentNumber",
                   sd.document_date AS "InvoiceDate",
                   p.id AS "PaymentId",
                   p.value_date AS "PaymentValueDate",
                   p.reference AS "PaymentReference",
                   b.tax_category AS "TaxCategory",
                   b.vat_rate_percent AS "VatRatePercent",
                   (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.taxable_base AS "NetAmount",
                   (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.tax_amount AS "VatAmount"
              FROM payment p
              JOIN payment_allocation pa ON pa.payment_id = p.id
              JOIN open_items oi ON oi.id = pa.open_item_id
              JOIN sales_documents sd ON sd.id = oi.document_id
              JOIN sales_document_tax_breakdown b ON b.document_id = sd.id
             WHERE p.value_date >= {from}
               AND p.value_date <= {to}
             ORDER BY p.value_date, p.id, pa.id, b.id
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Where(row => definitions.Any(definition =>
                definition.RecognitionSelector is { } selector
                && selector.TaxCategory == row.TaxCategory
                && selector.TaxRatePercent == row.VatRatePercent))
            .Select(row => new UstVaDrillDownEntry(
                Kind: "payment",
                JournalEntryId: null,
                JournalNumber: null,
                EntryDate: null,
                SourceRef: null,
                Description: null,
                row.DocumentId,
                row.DocumentNumber,
                row.InvoiceDate,
                row.PaymentId,
                row.PaymentValueDate,
                row.PaymentReference,
                row.NetAmount,
                row.VatAmount))
            .ToList();
    }

    private static IReadOnlyList<UstVaKennzifferDefinition> SourceDefinitions(
        IReadOnlyList<UstVaKennzifferDefinition> definitions,
        UstVaLine line)
    {
        var selected = definitions.Single(definition => definition.Kz == line.Kz);
        if (!selected.IsComputed)
        {
            return [selected];
        }

        // Kz 83 is computed from the taxable 19%/7% bases only. Tax-free Kz 41
        // has a real data source but contributes no output VAT and must therefore
        // not appear in the Zahllast drill-down.
        return definitions
            .Where(definition => definition.Kz is "81" or "86")
            .ToList();
    }

    private static (DateOnly From, DateOnly To) ResolvePeriod(int jahr, string zeitraum)
    {
        var (firstMonth, monthCount) = zeitraum switch
        {
            "01" => (1, 1),
            "02" => (2, 1),
            "03" => (3, 1),
            "04" => (4, 1),
            "05" => (5, 1),
            "06" => (6, 1),
            "07" => (7, 1),
            "08" => (8, 1),
            "09" => (9, 1),
            "10" => (10, 1),
            "11" => (11, 1),
            "12" => (12, 1),
            "41" => (1, 3),
            "42" => (4, 3),
            "43" => (7, 3),
            "44" => (10, 3),
            _ => throw new ArgumentOutOfRangeException(
                nameof(zeitraum),
                zeitraum,
                "ELSTER period code must be a month (01-12) or quarter (41-44)."),
        };
        var from = new DateOnly(jahr, firstMonth, 1);
        return (from, from.AddMonths(monthCount).AddDays(-1));
    }

    private sealed class SollDrillDownSqlRow
    {
        public Guid JournalEntryId { get; set; }
        public string? JournalNumber { get; set; }
        public DateOnly EntryDate { get; set; }
        public string SourceRef { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaxCategory? TaxCategory { get; set; }
        public decimal? TaxRatePercent { get; set; }
        public PostingDirection Direction { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class IstDrillDownSqlRow
    {
        public Guid DocumentId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateOnly InvoiceDate { get; set; }
        public Guid PaymentId { get; set; }
        public DateOnly PaymentValueDate { get; set; }
        public string? PaymentReference { get; set; }
        public TaxCategory TaxCategory { get; set; }
        public decimal VatRatePercent { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
    }
}

/// <summary>Discriminated per-Kennziffer contribution list.</summary>
public sealed record UstVaDrillDownResult(
    string Kz,
    Besteuerungsart Besteuerungsart,
    string RecognitionBasis,
    bool IsKleinunternehmer,
    IReadOnlyList<UstVaDrillDownEntry> Entries);

/// <summary>One Soll journal contribution or one Ist invoice/payment attribution.</summary>
public sealed record UstVaDrillDownEntry(
    string Kind,
    Guid? JournalEntryId,
    string? JournalNumber,
    DateOnly? EntryDate,
    string? SourceRef,
    string? Description,
    Guid? DocumentId,
    string? DocumentNumber,
    DateOnly? InvoiceDate,
    Guid? PaymentId,
    DateOnly? PaymentValueDate,
    string? PaymentReference,
    decimal AttributedNet,
    decimal AttributedVat);

/// <summary>File bytes plus the HTTP metadata used by a report download endpoint.</summary>
internal sealed record ReportExport(byte[] Bytes, string ContentType, string FileName);
