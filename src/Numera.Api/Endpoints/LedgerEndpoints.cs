using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>RLS-scoped journal, account-statement and Festschreibung endpoints.</summary>
public static class LedgerEndpoints
{
    /// <summary>Maps authenticated ledger reads and the owner-only period lock.</summary>
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger").RequireAuthorization();

        group.MapGet("/journal", async (
            NumeraDbContext db,
            CancellationToken ct,
            DateOnly? from = null,
            DateOnly? to = null,
            LedgerSourceType? sourceType = null,
            Guid? after = null,
            int pageSize = 50) =>
            Results.Ok(await GetJournalAsync(db, from, to, sourceType, after, pageSize, ct)
                .ConfigureAwait(false)));

        group.MapGet("/accounts/{accountId:guid}/statement", async (
            Guid accountId,
            NumeraDbContext db,
            CancellationToken ct,
            DateOnly? from = null,
            DateOnly? to = null,
            Guid? after = null,
            int pageSize = 50) =>
        {
            var result = await GetAccountStatementAsync(
                db, accountId, from, to, after, pageSize, ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/periods/{year:int}/{month:int}/lock", LockPeriodEndpointAsync)
            .RequireAuthorization("RequireOwner");

        return app;
    }

    internal static async Task<PagedEnvelope<JournalEntryListItem>> GetJournalAsync(
        NumeraDbContext db,
        DateOnly? from,
        DateOnly? to,
        LedgerSourceType? sourceType,
        Guid? after,
        int pageSize,
        CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cursor = after is { } cursorId
            ? await db.Set<JournalEntry>()
                .AsNoTracking()
                .Where(entry => entry.Id == cursorId)
                .Select(entry => new JournalCursor(entry.JournalNumber, entry.EntryDate))
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : null;
        if (after is not null && cursor is null)
        {
            return new PagedEnvelope<JournalEntryListItem>([], null);
        }

        var fromDate = from ?? DateOnly.MinValue;
        var toDate = to ?? DateOnly.MaxValue;
        var filterSourceType = sourceType is null ? 0 : 1;
        var sourceTypeValue = (int)(sourceType ?? LedgerSourceType.Manual);
        var hasCursor = after is null ? 0 : 1;
        var cursorIsNumbered = cursor?.JournalNumber is null ? 0 : 1;
        var cursorNumber = cursor?.JournalNumber ?? string.Empty;
        var cursorDate = cursor?.EntryDate ?? DateOnly.MinValue;
        var cursorIdValue = after ?? Guid.Empty;
        var take = pageSize + 1;

        var rawRows = await db.Database.SqlQuery<JournalSqlRow>(
            $"""
            SELECT je.id AS "Id",
                   je.journal_number AS "JournalNumber",
                   je.entry_date AS "EntryDate",
                   je.source_type AS "SourceType",
                   je.source_ref AS "SourceRef",
                   je.description AS "Description",
                   je.posting_type AS "PostingType",
                   COALESCE(SUM(p.amount) FILTER (WHERE p.direction = 1), 0) AS "TotalDebit",
                   COALESCE(SUM(p.amount) FILTER (WHERE p.direction = 2), 0) AS "TotalCredit",
                   (je.festgeschrieben_at IS NOT NULL) AS "IsFestgeschrieben"
              FROM journal_entries je
              LEFT JOIN postings p ON p.journal_entry_id = je.id
             WHERE je.entry_date >= {fromDate}
               AND je.entry_date <= {toDate}
               AND ({filterSourceType} = 0 OR je.source_type = {sourceTypeValue})
               AND (
                    {hasCursor} = 0
                    OR (
                        {cursorIsNumbered} = 1
                        AND (
                            je.journal_number IS NULL
                            OR je.journal_number > {cursorNumber}
                            OR (je.journal_number = {cursorNumber} AND je.id > {cursorIdValue})
                        )
                    )
                    OR (
                        {cursorIsNumbered} = 0
                        AND je.journal_number IS NULL
                        AND (
                            je.entry_date > {cursorDate}
                            OR (je.entry_date = {cursorDate} AND je.id > {cursorIdValue})
                        )
                    )
               )
             GROUP BY je.id
             ORDER BY (je.journal_number IS NULL), je.journal_number, je.entry_date, je.id
             LIMIT {take}
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasMore = rawRows.Count > pageSize;
        var pageRows = rawRows.Take(pageSize).ToList();
        var items = pageRows
            .Select(row => new JournalEntryListItem(
                row.Id,
                row.JournalNumber,
                row.EntryDate,
                (LedgerSourceType)row.SourceType,
                row.SourceRef,
                row.Description,
                (PostingType)row.PostingType,
                row.TotalDebit,
                row.TotalCredit,
                row.IsFestgeschrieben))
            .ToList();
        return new PagedEnvelope<JournalEntryListItem>(
            items,
            hasMore ? items[^1].Id : null);
    }

    internal static async Task<PagedEnvelope<AccountStatementRow>?> GetAccountStatementAsync(
        NumeraDbContext db,
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        Guid? after,
        int pageSize,
        CancellationToken ct)
    {
        var account = await db.Set<Account>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == accountId)
            .Select(candidate => new { candidate.Id, candidate.Type })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var naturalDirection = account.Type is AccountType.Asset or AccountType.Expense
            ? PostingDirection.Debit
            : PostingDirection.Credit;
        decimal openingBalance = 0m;
        if (from is { } openingDate)
        {
            openingBalance = await (
                from posting in db.Set<Posting>().AsNoTracking()
                join entry in db.Set<JournalEntry>().AsNoTracking()
                    on posting.JournalEntryId equals entry.Id
                join period in db.Set<FiscalPeriod>().AsNoTracking()
                    on entry.PeriodId equals period.Id
                where posting.AccountId == accountId
                    && period.Status == FiscalPeriodStatus.Locked
                    && entry.EntryDate < openingDate
                select (decimal?)(posting.Direction == naturalDirection
                    ? posting.Amount
                    : -posting.Amount))
                .SumAsync(ct)
                .ConfigureAwait(false) ?? 0m;
        }

        var cursorDate = after is { } cursorId
            ? await (
                from posting in db.Set<Posting>().AsNoTracking()
                join entry in db.Set<JournalEntry>().AsNoTracking()
                    on posting.JournalEntryId equals entry.Id
                where posting.Id == cursorId && posting.AccountId == accountId
                select (DateOnly?)entry.EntryDate)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : null;
        if (after is not null && cursorDate is null)
        {
            return new PagedEnvelope<AccountStatementRow>([], null, openingBalance);
        }

        var fromDate = from ?? DateOnly.MinValue;
        var toDate = to ?? DateOnly.MaxValue;
        var cursorDateValue = cursorDate ?? DateOnly.MinValue;
        var cursorIdValue = after ?? Guid.Empty;
        var naturalDirectionValue = (int)naturalDirection;
        var take = pageSize + 1;
        var hasCursor = after is null ? 0 : 1;

        var rawRows = await db.Database.SqlQuery<AccountStatementSqlRow>(
            $"""
            WITH statement_rows AS (
                SELECT p.id AS "PostingId",
                       je.entry_date AS "EntryDate",
                       je.journal_number AS "JournalNumber",
                       (
                           SELECT string_agg(DISTINCT counter.number, ', ' ORDER BY counter.number)
                             FROM postings other_posting
                             JOIN accounts counter ON counter.id = other_posting.account_id
                            WHERE other_posting.journal_entry_id = p.journal_entry_id
                              AND other_posting.account_id <> p.account_id
                       ) AS "CounterAccount",
                       CASE WHEN p.direction = 1 THEN p.amount ELSE 0 END AS "Debit",
                       CASE WHEN p.direction = 2 THEN p.amount ELSE 0 END AS "Credit",
                       {openingBalance} + SUM(
                           CASE WHEN p.direction = {naturalDirectionValue}
                                THEN p.amount ELSE -p.amount END
                       ) OVER (ORDER BY je.entry_date, p.id ROWS UNBOUNDED PRECEDING) AS "RunningBalance"
                  FROM postings p
                  JOIN journal_entries je ON je.id = p.journal_entry_id
                 WHERE p.account_id = {accountId}
                   AND je.entry_date >= {fromDate}
                   AND je.entry_date <= {toDate}
            )
            SELECT "PostingId", "EntryDate", "JournalNumber", "CounterAccount",
                   "Debit", "Credit", "RunningBalance"
              FROM statement_rows
             WHERE {hasCursor} = 0
                OR "EntryDate" > {cursorDateValue}
                OR ("EntryDate" = {cursorDateValue} AND "PostingId" > {cursorIdValue})
             ORDER BY "EntryDate", "PostingId"
             LIMIT {take}
            """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasMore = rawRows.Count > pageSize;
        var items = rawRows
            .Take(pageSize)
            .Select(row => new AccountStatementRow(
                row.PostingId,
                row.EntryDate,
                row.JournalNumber,
                row.CounterAccount,
                row.Debit,
                row.Credit,
                row.RunningBalance))
            .ToList();
        return new PagedEnvelope<AccountStatementRow>(
            items,
            hasMore ? items[^1].PostingId : null,
            openingBalance);
    }

    internal static async Task<IResult> LockPeriodEndpointAsync(
        int year,
        int month,
        FestschreibungService service,
        CancellationToken ct)
    {
        var result = await service.LockPeriodAsync(year, month, ct).ConfigureAwait(false);
        return result.Status switch
        {
            PeriodLockStatus.Success => Results.Ok(result),
            PeriodLockStatus.Conflict => Results.Conflict(result),
            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["period"] = [result.Error ?? "Invalid period."] }),
        };
    }

    private sealed record JournalCursor(string? JournalNumber, DateOnly EntryDate);

    private sealed class JournalSqlRow
    {
        public Guid Id { get; set; }
        public string? JournalNumber { get; set; }
        public DateOnly EntryDate { get; set; }
        public int SourceType { get; set; }
        public string SourceRef { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int PostingType { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public bool IsFestgeschrieben { get; set; }
    }

    private sealed class AccountStatementSqlRow
    {
        public Guid PostingId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string? JournalNumber { get; set; }
        public string? CounterAccount { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
    }
}
