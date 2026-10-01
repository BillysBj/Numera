using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Ledger;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.Belege.Payments;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Records append-only supplier payments atomically with payable state and ledger postings.</summary>
public sealed class SupplierPaymentService(
    NumeraDbContext db,
    ICurrentTenant currentTenant,
    IAuditWriter audit,
    PostingEngine postingEngine,
    AccountResolver accountResolver)
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<PaymentOperationResult> RecordAsync(
        Guid receiptId,
        RecordSupplierPaymentRequest request,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to record a supplier payment.");
        if (request.Amount is null or <= 0m)
        {
            return PaymentOperationResult.Invalid("amount", "Der Zahlungsbetrag muss größer als 0 sein.");
        }

        if (!Enum.IsDefined(request.Method))
        {
            return PaymentOperationResult.Invalid("method", "Die Zahlungsart ist ungültig.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var receipt = await LockReceiptAsync(receiptId, ct).ConfigureAwait(false);
        if (receipt is null)
        {
            return PaymentOperationResult.NotFound();
        }

        if (receipt.Status != ReceiptStatus.Booked || receipt.OpenAmount is null or <= 0m)
        {
            return PaymentOperationResult.Invalid("receipt", "Der Beleg ist nicht zahlbar.");
        }

        if (request.Amount.Value > receipt.OpenAmount.Value)
        {
            return PaymentOperationResult.Invalid("amount", "Die Zahlung übersteigt den offenen Betrag.");
        }

        // Clear the creditor actually booked, independent of later supplier-master edits.
        var creditorAccount = await (
            from posting in db.Set<Posting>()
            join account in db.Set<Account>() on posting.AccountId equals account.Id
            where posting.JournalEntryId == receipt.JournalEntryId
                && posting.Direction == PostingDirection.Credit
            select account.Number).SingleAsync(ct).ConfigureAwait(false);
        var payment = new SupplierPayment
        {
            TenantId = tenantId,
            Amount = request.Amount.Value,
            ValueDate = request.ValueDate,
            Method = request.Method,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RecordedAt = DateTimeOffset.UtcNow,
        };
        var allocation = new SupplierPaymentAllocation
        {
            TenantId = tenantId,
            PaymentId = payment.Id,
            ReceiptId = receipt.Id,
            AllocatedAmount = payment.Amount,
        };
        db.Add(payment);
        db.Add(allocation);
        receipt.OpenAmount -= payment.Amount;
        receipt.PaymentStatus = receipt.OpenAmount == 0m
            ? ReceiptPaymentStatus.Paid
            : ReceiptPaymentStatus.PartiallyPaid;

        // Like incoming payments, all methods currently use the standard Bank account.
        await PostAsync(payment, new SupplierPaymentPostingInput(
            null, payment.ValueDate, [new(creditorAccount, payment.Amount)], IsReversal: false),
            originalEntry: null, ct).ConfigureAwait(false);
        await audit.RecordAsync(new SupplierPaymentAuditEvent(
            "supplier_payment.recorded", payment.Id, null, Snapshot(payment, [allocation])), ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(payment.Id);
    }

    public async Task<PaymentOperationResult> ReverseAsync(
        Guid receiptId,
        Guid paymentId,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to reverse a supplier payment.");
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var receipt = await LockReceiptAsync(receiptId, ct).ConfigureAwait(false);
        var original = await db.Set<SupplierPayment>().AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.Id == paymentId, ct).ConfigureAwait(false);
        var allocations = await db.Set<SupplierPaymentAllocation>().AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId).ToListAsync(ct).ConfigureAwait(false);
        if (receipt is null || original is null || allocations.Count == 0
            || allocations.Any(allocation => allocation.ReceiptId != receiptId))
        {
            return PaymentOperationResult.NotFound();
        }

        if (original.ReversesPaymentId is not null
            || await db.Set<SupplierPayment>().AnyAsync(
                payment => payment.ReversesPaymentId == paymentId, ct).ConfigureAwait(false))
        {
            return PaymentOperationResult.Conflict("Die Zahlung wurde bereits storniert.");
        }

        if (receipt.Status != ReceiptStatus.Booked || receipt.OpenAmount is null)
        {
            return PaymentOperationResult.Conflict("Der zugehörige gebuchte Beleg ist nicht mehr verfügbar.");
        }

        var sourceRef = original.Id.ToString();
        var originalEntry = await db.Set<JournalEntry>().AsNoTracking().Include(entry => entry.Postings)
            .SingleAsync(entry => entry.SourceType == LedgerSourceType.Payment
                && entry.SourceRef == sourceRef, ct).ConfigureAwait(false);
        var accountIds = originalEntry.Postings.Select(posting => posting.AccountId).ToArray();
        var numbers = await db.Set<Account>().AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Number, ct).ConfigureAwait(false);
        var bankPosting = originalEntry.Postings.Single(posting => posting.Direction == PostingDirection.Credit);
        var reversal = new SupplierPayment
        {
            TenantId = tenantId,
            Amount = -original.Amount,
            ValueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Method = original.Method,
            Reference = $"Storno: {original.Reference}".TrimEnd(),
            ReversesPaymentId = original.Id,
            RecordedAt = DateTimeOffset.UtcNow,
        };
        db.Add(reversal);
        var reversingAllocations = allocations.Select(allocation => new SupplierPaymentAllocation
        {
            TenantId = tenantId,
            PaymentId = reversal.Id,
            ReceiptId = allocation.ReceiptId,
            AllocatedAmount = -allocation.AllocatedAmount,
        }).ToList();
        db.AddRange(reversingAllocations);
        receipt.OpenAmount += allocations.Sum(allocation => allocation.AllocatedAmount);
        receipt.PaymentStatus = receipt.OpenAmount >= receipt.GrossAmount
            ? ReceiptPaymentStatus.Unpaid
            : ReceiptPaymentStatus.PartiallyPaid;

        await PostAsync(reversal, new SupplierPaymentPostingInput(
            numbers[bankPosting.AccountId], reversal.ValueDate,
            originalEntry.Postings.Where(posting => posting.Direction == PostingDirection.Debit)
                .Select(posting => new SupplierPaymentPostingAllocation(numbers[posting.AccountId], posting.Amount))
                .ToList(), IsReversal: true), originalEntry, ct).ConfigureAwait(false);
        await audit.RecordAsync(new SupplierPaymentAuditEvent(
            "supplier_payment.reversed", reversal.Id, Snapshot(original, allocations),
            Snapshot(reversal, reversingAllocations)), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(reversal.Id);
    }

    private Task<Receipt?> LockReceiptAsync(Guid id, CancellationToken ct) =>
        // Serialize settlement and reversal of this payable. RLS and the EF tenant filter both apply.
        db.Set<Receipt>().FromSqlInterpolated($"SELECT * FROM receipt WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task PostAsync(
        SupplierPayment payment,
        SupplierPaymentPostingInput input,
        JournalEntry? originalEntry,
        CancellationToken ct)
    {
        var chartVariant = await db.Set<LedgerSettings>().AsNoTracking()
            .Select(settings => settings.ChartVariant).SingleAsync(ct).ConfigureAwait(false);
        var header = new JournalEntry
        {
            TenantId = payment.TenantId,
            EntryDate = payment.ValueDate,
            SourceRef = payment.Id.ToString(),
            SourceType = LedgerSourceType.Payment,
            Description = $"Lieferantenzahlung {payment.Reference ?? payment.Id.ToString()}",
            PostingType = input.IsReversal ? PostingType.Storno : PostingType.Normal,
            ReversesEntryId = originalEntry?.Id,
        };
        await postingEngine.PostAsync(
            new SupplierPaymentPostingSource(payment.TenantId, chartVariant, input, accountResolver),
            header, ct).ConfigureAwait(false);
    }

    private static string Snapshot(SupplierPayment payment, IEnumerable<SupplierPaymentAllocation> allocations) =>
        JsonSerializer.Serialize(new
        {
            payment.Id,
            payment.Amount,
            payment.ValueDate,
            Method = (int)payment.Method,
            payment.Reference,
            payment.ReversesPaymentId,
            payment.RecordedAt,
            Allocations = allocations.Select(allocation => new { allocation.ReceiptId, allocation.AllocatedAmount }),
        }, AuditJson);
}

internal sealed record SupplierPaymentAuditEvent(
    string Action, Guid? EntityId, string? Before, string? After) : IAuditEvent
{
    public string EntityType => nameof(SupplierPayment);
}
