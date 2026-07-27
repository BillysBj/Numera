using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Records and reverses append-only incoming payments atomically with receivable state.</summary>
public sealed class PaymentService
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAuditWriter _audit;

    /// <summary>Creates the service over the request-scoped DbContext + tenant + audit writer.</summary>
    public PaymentService(NumeraDbContext db, ICurrentTenant currentTenant, IAuditWriter audit)
    {
        _db = db;
        _currentTenant = currentTenant;
        _audit = audit;
    }

    /// <summary>Records a payment, its allocations, open-item changes and audit event in one transaction.</summary>
    public async Task<PaymentOperationResult> RecordAsync(
        RecordPaymentRequest request,
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to record a payment.");

        if (request.Amount is null or <= 0m)
        {
            return PaymentOperationResult.Invalid("amount", "Der Zahlungsbetrag muss größer als 0 sein.");
        }

        if (request.Allocations.Count == 0)
        {
            return PaymentOperationResult.Invalid("allocations", "Mindestens eine Zuordnung ist erforderlich.");
        }

        if (!Enum.IsDefined(request.Method))
        {
            return PaymentOperationResult.Invalid("method", "Die Zahlungsart ist ungültig.");
        }

        if (request.Allocations.Any(a => a.Amount <= 0m))
        {
            return PaymentOperationResult.Invalid("allocations", "Jeder Zuordnungsbetrag muss größer als 0 sein.");
        }

        var allocatedTotal = request.Allocations.Sum(a => a.Amount);
        if (allocatedTotal != request.Amount.Value)
        {
            return PaymentOperationResult.Invalid("amount", "Zahlungsbetrag und Summe der Zuordnungen müssen übereinstimmen.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var grouped = request.Allocations
            .GroupBy(a => a.OpenItemId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var openItemIds = grouped.Keys.ToArray();
        var openItems = await _db.Set<OpenItem>()
            .Where(o => openItemIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct)
            .ConfigureAwait(false);

        foreach (var (openItemId, amount) in grouped)
        {
            if (!openItems.TryGetValue(openItemId, out var openItem))
            {
                return PaymentOperationResult.Invalid("allocations", $"Der offene Posten {openItemId} wurde nicht gefunden.");
            }

            if (openItem.Status is not (OpenItemStatus.Open or OpenItemStatus.PartiallyPaid))
            {
                return PaymentOperationResult.Invalid("allocations", $"Der offene Posten {openItemId} ist nicht zahlbar.");
            }

            if (amount > openItem.OpenAmount)
            {
                return PaymentOperationResult.Invalid("allocations", $"Die Zuordnung für {openItemId} übersteigt den offenen Betrag.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var payment = new Payment
        {
            TenantId = tenantId,
            Amount = request.Amount.Value,
            ValueDate = request.ValueDate,
            Method = request.Method,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RecordedAt = now,
        };
        _db.Add(payment);

        foreach (var allocation in request.Allocations)
        {
            _db.Add(new PaymentAllocation
            {
                TenantId = tenantId,
                PaymentId = payment.Id,
                OpenItemId = allocation.OpenItemId,
                AllocatedAmount = allocation.Amount,
            });
        }

        foreach (var (openItemId, amount) in grouped)
        {
            var openItem = openItems[openItemId];
            openItem.OpenAmount -= amount;
            openItem.Status = openItem.OpenAmount <= 0m
                ? OpenItemStatus.Paid
                : OpenItemStatus.PartiallyPaid;

            var document = await _db.Set<SalesDocument>()
                .FirstAsync(d => d.Id == openItem.DocumentId, ct)
                .ConfigureAwait(false);
            document.AmountDue = openItem.OpenAmount;
            if (openItem.Status == OpenItemStatus.Paid)
            {
                document.Status = DocumentStatus.Paid;
            }
        }

        await _audit.RecordAsync(
            new PaymentAuditEvent(
                "payment.recorded", payment.Id, Before: null,
                After: Snapshot(payment, request.Allocations)),
            ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(payment.Id);
    }

    /// <summary>Creates a negative payment that reverses an original booked payment.</summary>
    public async Task<PaymentOperationResult> ReverseAsync(
        Guid paymentId,
        CancellationToken ct = default)
    {
        var tenantId = _currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to reverse a payment.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var original = await _db.Set<Payment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            .ConfigureAwait(false);
        if (original is null)
        {
            return PaymentOperationResult.NotFound();
        }

        if (original.ReversesPaymentId is not null ||
            await _db.Set<Payment>().AnyAsync(p => p.ReversesPaymentId == original.Id, ct).ConfigureAwait(false))
        {
            return PaymentOperationResult.Conflict("Die Zahlung wurde bereits storniert.");
        }

        var originalAllocations = await _db.Set<PaymentAllocation>()
            .AsNoTracking()
            .Where(a => a.PaymentId == original.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var grouped = originalAllocations
            .GroupBy(a => a.OpenItemId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedAmount));
        var openItemIds = grouped.Keys.ToArray();
        var openItems = await _db.Set<OpenItem>()
            .Where(o => openItemIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct)
            .ConfigureAwait(false);
        if (openItems.Count != grouped.Count)
        {
            return PaymentOperationResult.Conflict("Mindestens ein zugehöriger offener Posten ist nicht mehr verfügbar.");
        }

        var reversal = new Payment
        {
            TenantId = tenantId,
            Amount = -original.Amount,
            ValueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Method = original.Method,
            Reference = $"Storno: {original.Reference}".TrimEnd(),
            ReversesPaymentId = original.Id,
            RecordedAt = DateTimeOffset.UtcNow,
        };
        _db.Add(reversal);

        foreach (var allocation in originalAllocations)
        {
            _db.Add(new PaymentAllocation
            {
                TenantId = tenantId,
                PaymentId = reversal.Id,
                OpenItemId = allocation.OpenItemId,
                AllocatedAmount = -allocation.AllocatedAmount,
            });
        }

        foreach (var (openItemId, amount) in grouped)
        {
            var openItem = openItems[openItemId];
            openItem.OpenAmount += amount;
            openItem.Status = openItem.OpenAmount >= openItem.OriginalAmount
                ? OpenItemStatus.Open
                : OpenItemStatus.PartiallyPaid;

            var document = await _db.Set<SalesDocument>()
                .FirstAsync(d => d.Id == openItem.DocumentId, ct)
                .ConfigureAwait(false);
            document.AmountDue = openItem.OpenAmount;
            if (document.Status == DocumentStatus.Paid)
            {
                document.Status = DocumentStatus.Finalized;
            }
        }

        await _audit.RecordAsync(
            new PaymentAuditEvent(
                "payment.reversed", reversal.Id, Before: Snapshot(original, originalAllocations),
                After: Snapshot(reversal, originalAllocations.Select(a =>
                    new PaymentAllocationInput(a.OpenItemId, -a.AllocatedAmount)))),
            ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(reversal.Id);
    }

    private static string Snapshot(Payment payment, IEnumerable<PaymentAllocationInput> allocations)
        => JsonSerializer.Serialize(new
        {
            payment.Id,
            payment.Amount,
            payment.ValueDate,
            Method = (int)payment.Method,
            payment.Reference,
            payment.ReversesPaymentId,
            payment.RecordedAt,
            Allocations = allocations.Select(a => new { a.OpenItemId, a.Amount }),
        }, AuditJson);

    private static string Snapshot(Payment payment, IEnumerable<PaymentAllocation> allocations)
        => Snapshot(payment, allocations.Select(a =>
            new PaymentAllocationInput(a.OpenItemId, a.AllocatedAmount)));
}

/// <summary>Typed service outcome mapped by the HTTP endpoints.</summary>
public sealed record PaymentOperationResult(
    PaymentOperationStatus Status,
    Guid? PaymentId = null,
    string? ErrorKey = null,
    string? Error = null)
{
    public static PaymentOperationResult Ok(Guid id) => new(PaymentOperationStatus.Success, id);
    public static PaymentOperationResult Invalid(string key, string error) =>
        new(PaymentOperationStatus.Invalid, ErrorKey: key, Error: error);
    public static PaymentOperationResult NotFound() => new(PaymentOperationStatus.NotFound);
    public static PaymentOperationResult Conflict(string error) =>
        new(PaymentOperationStatus.Conflict, Error: error);
}

/// <summary>The finite outcomes of a payment command.</summary>
public enum PaymentOperationStatus
{
    Success,
    Invalid,
    NotFound,
    Conflict,
}

/// <summary>A payment audit mutation; tenant and actor are stamped by the writer.</summary>
internal sealed record PaymentAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(Payment);
}
