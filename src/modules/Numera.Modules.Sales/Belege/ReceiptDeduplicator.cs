using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Belege;

/// <summary>The review verdict produced when receipt intake finds a possible duplicate.</summary>
public enum DuplicateVerdict
{
    None,
    Exact,
    SuspectedBusinessKey,
}

/// <summary>
/// Detects exact-byte and business-key duplicates inside the current tenant's RLS scope. A
/// duplicate is only a review verdict: callers must still retain and surface the received item.
/// </summary>
public sealed class ReceiptDeduplicator
{
    /// <summary>Computes the lowercase SHA-256 hex digest used by receipt integrity and dedup.</summary>
    public string ComputeHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Checks exact content first, then supplier/invoice/gross/date identity.</summary>
    public async Task<DuplicateVerdict> CheckAsync(
        NumeraDbContext db,
        string contentHash,
        Guid? supplierId,
        string? invoiceNumber,
        decimal? gross,
        DateOnly? invoiceDate,
        CancellationToken ct)
    {
        var receipts = db.Set<Receipt>()
            .AsNoTracking()
            .Where(receipt => receipt.Status != ReceiptStatus.Rejected);

        if (await receipts.AnyAsync(receipt => receipt.ContentHash == contentHash, ct))
        {
            return DuplicateVerdict.Exact;
        }

        if (supplierId is null || invoiceNumber is null || gross is null || invoiceDate is null)
        {
            return DuplicateVerdict.None;
        }

        var businessKeyExists = await receipts.AnyAsync(
            receipt => receipt.MatchedPartnerId == supplierId
                && receipt.InvoiceNumber == invoiceNumber
                && receipt.GrossAmount == gross
                && receipt.InvoiceDate == invoiceDate,
            ct);

        return businessKeyExists
            ? DuplicateVerdict.SuspectedBusinessKey
            : DuplicateVerdict.None;
    }
}
