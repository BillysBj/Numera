using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Numbering;

/// <summary>
/// Assigns the legal, race-safe document number at finalize (RESEARCH.md Pattern 3,
/// INV-02). The number is claimed by a SINGLE atomic <c>INSERT … ON CONFLICT … DO UPDATE
/// … RETURNING</c> statement against <see cref="NumberSequence"/> — never a Postgres
/// <c>SEQUENCE</c> (burns numbers on rollback, not per-tenant) and never <c>MAX()+1</c>
/// (read-modify-write race). Because the whole claim is one statement under the ON CONFLICT
/// row lock, concurrent finalizations of the same series serialize per
/// <c>(tenant, doc_type, year)</c> and cannot collide.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AssignAsync"/> MUST be called from inside the finalize transaction: it runs
/// on the <see cref="NumeraDbContext"/>'s connection and enlists the ambient
/// <see cref="IDbContextTransaction"/>, so a rolled-back finalize releases the row lock and
/// simply leaves a skipped number (legal — German law requires <em>einmalig</em>, not
/// <em>lückenlos</em>; there is deliberately no gap-backfill).
/// </para>
/// <para>
/// The rendered format comes from the tenant's <see cref="DocumentNumberFormat"/> row for
/// the type (<c>{prefix}{YYYY-}{seq:0padding}</c> → e.g. <c>RE-2026-00001</c>); when no
/// config row exists sane per-type defaults apply (type-specific prefix, year embedded,
/// 5-digit padding). Never-reset series (<c>includeYear=false</c>) key the counter on
/// <c>year = 0</c>.
/// </para>
/// </remarks>
public sealed class NumberingService
{
    // Reserved partner series outside DocumentType (0–7), sharing number_sequences
    // without a schema change. Year 0 makes these tenant-scoped counters continuous.
    private const int PartnerCustomerSeriesDocType = 1001;
    private const int PartnerSupplierSeriesDocType = 1002;

    private readonly NumeraDbContext _db;

    /// <summary>Creates the service over the request-scoped <see cref="NumeraDbContext"/>.</summary>
    public NumberingService(NumeraDbContext db) => _db = db;

    /// <summary>
    /// Claims and renders the next number for the <paramref name="docType"/> series.
    /// </summary>
    /// <param name="docType">The document type whose series to draw from (its own counter).</param>
    /// <param name="year">
    /// The document's calendar year (usually <c>documentDate.Year</c>). Used as the series
    /// reset boundary only when the format embeds the year; otherwise the counter keys on 0.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document number, e.g. <c>RE-2026-00001</c>.</returns>
    public async Task<string> AssignAsync(DocumentType docType, int year, CancellationToken ct)
    {
        var format = await _db.Set<DocumentNumberFormat>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DocType == (int)docType, ct)
            .ConfigureAwait(false);

        var prefix = format?.Prefix ?? DefaultPrefix(docType);
        var includeYear = format?.IncludeYear ?? true;
        var padding = format is { Padding: > 0 } ? format.Padding : 5;

        // Never-reset series (no embedded year) share a single counter keyed on year 0.
        var seriesYear = includeYear ? year : 0;

        var assigned = await ClaimAsync((int)docType, seriesYear, ct).ConfigureAwait(false);

        var yearPart = includeYear ? $"{seriesYear}-" : string.Empty;
        var seq = assigned.ToString("D" + padding.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return $"{prefix}{yearPart}{seq}";
    }

    /// <summary>
    /// Claims a continuous customer (K-00001) or supplier (L-00001) number.
    /// Without an ambient transaction the claim autocommits; failed partner saves may leave gaps.
    /// </summary>
    public async Task<string> AssignPartnerNumberAsync(bool customer, CancellationToken ct)
    {
        var docType = customer ? PartnerCustomerSeriesDocType : PartnerSupplierSeriesDocType;
        var assigned = await ClaimAsync(docType, 0, ct).ConfigureAwait(false);
        return (customer ? "K-" : "L-") + assigned.ToString("D5", CultureInfo.InvariantCulture);
    }

    // The atomic claim (RESEARCH.md Pattern 3). One statement: creates the counter row on
    // first use (next_value seeded to 2 so this txn owns 1) or increments it, returning the
    // value this transaction owns. Runs on the DbContext connection + ambient transaction so
    // it participates in the finalize unit-of-work and the ON CONFLICT row lock serializes
    // concurrent finalizations of the same series.
    private async Task<long> ClaimAsync(int docType, int year, CancellationToken ct)
    {
        // EF opens the connection through its tenant interceptor, including for partner
        // creation with no preceding query/transaction. Balance EF's open count below.
        await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = _db.Database.GetDbConnection();
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText =
                """
                INSERT INTO number_sequences (id, tenant_id, doc_type, year, next_value)
                VALUES (@id, current_setting('app.current_tenant')::uuid, @docType, @year, 2)
                ON CONFLICT (tenant_id, doc_type, year)
                DO UPDATE SET next_value = number_sequences.next_value + 1
                RETURNING next_value - 1;
                """;
            cmd.Parameters.Add(Param(cmd, "id", Guid.CreateVersion7()));
            cmd.Parameters.Add(Param(cmd, "docType", docType));
            cmd.Parameters.Add(Param(cmd, "year", year));

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Numbering claim returned no value.");
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static DbParameter Param(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    // Sane default prefixes when a tenant has not configured document_number_formats.
    private static string DefaultPrefix(DocumentType docType) => docType switch
    {
        DocumentType.Rechnung => "RE-",
        DocumentType.Storno => "ST-",
        DocumentType.Gutschrift => "GS-",
        DocumentType.Angebot => "AN-",
        DocumentType.Auftragsbestaetigung => "AB-",
        DocumentType.Lieferschein => "LS-",
        DocumentType.Abschlagsrechnung => "AR-",
        DocumentType.Schlussrechnung => "SR-",
        _ => "DOC-",
    };
}
