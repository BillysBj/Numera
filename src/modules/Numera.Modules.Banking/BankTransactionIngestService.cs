using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Modules.Banking;

/// <summary>Persists normalized bank transactions idempotently inside one tenant scope.</summary>
public sealed class BankTransactionIngestService
{
    private const string DedupeIndexName =
        "ix_bank_transaction_tenant_id_bank_account_id_dedupe_key";

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    /// <summary>Creates the shared provider/file-import ingest path.</summary>
    public BankTransactionIngestService(NumeraDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <summary>Ingests an asynchronous stream and returns the number of new rows.</summary>
    public async Task<int> IngestAsync(
        Guid bankAccountId,
        IAsyncEnumerable<BankTransactionDraft> drafts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (bankAccountId == Guid.Empty)
        {
            throw new ArgumentException("Bank account id must not be empty.", nameof(bankAccountId));
        }

        var tenantId = _currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to ingest bank transactions.");
        var candidates = new List<BankTransaction>();
        var keysInBatch = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var draft in drafts.WithCancellation(ct).ConfigureAwait(false))
        {
            var dedupeKey = ComputeDedupeKey(bankAccountId, draft);
            if (!keysInBatch.Add(dedupeKey)
                || await _db.Set<BankTransaction>()
                    .AsNoTracking()
                    .AnyAsync(
                        transaction => transaction.BankAccountId == bankAccountId
                            && transaction.DedupeKey == dedupeKey,
                        ct)
                    .ConfigureAwait(false))
            {
                continue;
            }

            candidates.Add(new BankTransaction
            {
                TenantId = tenantId,
                BankAccountId = bankAccountId,
                DedupeKey = dedupeKey,
                Source = draft.Source,
                Amount = draft.Amount,
                ValueDate = draft.ValueDate,
                BookingDate = draft.BookingDate,
                Purpose = draft.Purpose,
                CounterpartyName = draft.CounterpartyName,
                CounterpartyIban = draft.CounterpartyIban,
                EndToEndId = draft.EndToEndId,
                MatchStatus = MatchStatus.Unmatched,
            });
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        _db.AddRange(candidates);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return candidates.Count;
        }
        catch (DbUpdateException exception) when (IsDedupeRace(exception))
        {
            // SaveChanges is atomic, so the failed batch inserted nothing. Detach it and
            // retry row-by-row: a concurrent winner is skipped while unrelated rows survive.
            foreach (var candidate in candidates)
            {
                _db.Entry(candidate).State = EntityState.Detached;
            }

            return await RetryAfterDedupeRaceAsync(candidates, bankAccountId, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Ingests file-import drafts through the same dedupe path.</summary>
    public Task<int> IngestAsync(
        Guid bankAccountId,
        IEnumerable<BankTransactionDraft> drafts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        return IngestAsync(bankAccountId, EnumerateAsync(drafts, ct), ct);
    }

    private async Task<int> RetryAfterDedupeRaceAsync(
        IEnumerable<BankTransaction> candidates,
        Guid bankAccountId,
        CancellationToken ct)
    {
        var inserted = 0;
        foreach (var candidate in candidates)
        {
            if (await _db.Set<BankTransaction>()
                    .AsNoTracking()
                    .AnyAsync(
                        transaction => transaction.BankAccountId == bankAccountId
                            && transaction.DedupeKey == candidate.DedupeKey,
                        ct)
                    .ConfigureAwait(false))
            {
                continue;
            }

            _db.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                inserted++;
            }
            catch (DbUpdateException exception) when (IsDedupeRace(exception))
            {
                _db.Entry(candidate).State = EntityState.Detached;
            }
        }

        return inserted;
    }

    private static string ComputeDedupeKey(Guid bankAccountId, BankTransactionDraft draft)
    {
        if (!string.IsNullOrWhiteSpace(draft.ProviderId))
        {
            return draft.ProviderId.Trim();
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, bankAccountId.ToString("N"));
        AppendField(hash, draft.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendField(
            hash,
            draft.BookingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
        AppendField(hash, draft.Amount.ToString("0.############################", CultureInfo.InvariantCulture));
        AppendField(hash, NormalizePurpose(draft.Purpose));
        AppendField(hash, NormalizeIban(draft.CounterpartyIban));
        AppendField(hash, draft.Source.ToString());
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string NormalizePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(purpose.Length);
        var whitespacePending = false;
        foreach (var character in purpose.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                whitespacePending = normalized.Length > 0;
                continue;
            }

            if (whitespacePending)
            {
                normalized.Append(' ');
                whitespacePending = false;
            }

            normalized.Append(char.ToUpperInvariant(character));
        }

        return normalized.ToString();
    }

    private static string NormalizeIban(string? iban) => string.IsNullOrWhiteSpace(iban)
        ? string.Empty
        : string.Concat(iban.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private static bool IsDedupeRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: DedupeIndexName,
        };

    private static async IAsyncEnumerable<BankTransactionDraft> EnumerateAsync(
        IEnumerable<BankTransactionDraft> drafts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            ct.ThrowIfCancellationRequested();
            yield return draft;
        }

        await Task.CompletedTask;
    }
}
