using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Contracts;
using Numera.Modules.Crm;
using Numera.Modules.Ledger;
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
    private readonly PostingEngine _postingEngine;
    private readonly AccountResolver _accountResolver;
    private readonly ILogger<PaymentService> _logger;

    /// <summary>Creates the service over the request-scoped DbContext + tenant + audit writer.</summary>
    public PaymentService(NumeraDbContext db, ICurrentTenant currentTenant, IAuditWriter audit)
        : this(
            db,
            currentTenant,
            audit,
            new PostingEngine(db),
            new AccountResolver(db),
            NullLogger<PaymentService>.Instance)
    {
    }

    /// <summary>Creates the service with the request-scoped ledger posting collaborators.</summary>
    public PaymentService(
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IAuditWriter audit,
        PostingEngine postingEngine,
        AccountResolver accountResolver,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _audit = audit;
        _postingEngine = postingEngine;
        _accountResolver = accountResolver;
        _logger = logger;
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

        await PostRecordedPaymentAsync(
            payment, request.Allocations, openItems, tenantId, ct).ConfigureAwait(false);

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

        await PostPaymentReversalAsync(
            original, reversal, tenantId, ct)
            .ConfigureAwait(false);

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

    private async Task PostRecordedPaymentAsync(
        Payment payment,
        IReadOnlyList<PaymentAllocationInput> allocations,
        IReadOnlyDictionary<Guid, OpenItem> openItems,
        Guid tenantId,
        CancellationToken ct)
    {
        var sourceRef = payment.Id.ToString();
        if (await _db.Set<JournalEntry>()
                .AnyAsync(
                    entry => entry.SourceType == LedgerSourceType.Payment
                        && entry.SourceRef == sourceRef,
                    ct)
                .ConfigureAwait(false))
        {
            return;
        }

        var chartVariant = await TryResolveChartVariantAsync(tenantId, payment.Id, ct)
            .ConfigureAwait(false);
        if (chartVariant is null)
        {
            return;
        }

        var debtorOverrides = await ResolveDebtorOverridesAsync(openItems.Values, ct)
            .ConfigureAwait(false);
        var input = new PaymentPostingInput(
            // MVP: every PaymentMethod books to the chart's standard Bank account.
            // A dedicated Kasse/payment-method mapping is a later refinement.
            BankAccount: null,
            payment.ValueDate,
            allocations
                .Select(allocation => new PaymentPostingAllocation(
                    debtorOverrides.GetValueOrDefault(allocation.OpenItemId),
                    allocation.Amount))
                .ToList(),
            IsReversal: false);
        var header = new JournalEntry
        {
            TenantId = tenantId,
            EntryDate = payment.ValueDate,
            SourceRef = sourceRef,
            SourceType = LedgerSourceType.Payment,
            Description = $"Zahlung {payment.Reference ?? payment.Id.ToString()}",
            PostingType = PostingType.Normal,
        };

        await _postingEngine.PostAsync(
            new PaymentPostingSource(tenantId, chartVariant.Value, input, _accountResolver),
            header,
            ct).ConfigureAwait(false);
    }

    private async Task PostPaymentReversalAsync(
        Payment original,
        Payment reversal,
        Guid tenantId,
        CancellationToken ct)
    {
        var reversalSourceRef = reversal.Id.ToString();
        if (await _db.Set<JournalEntry>()
                .AnyAsync(
                    entry => entry.SourceType == LedgerSourceType.Payment
                        && entry.SourceRef == reversalSourceRef,
                    ct)
                .ConfigureAwait(false))
        {
            return;
        }

        var originalSourceRef = original.Id.ToString();
        var originalEntry = await _db.Set<JournalEntry>()
            .AsNoTracking()
            .Include(entry => entry.Postings)
            .SingleOrDefaultAsync(
                entry => entry.SourceType == LedgerSourceType.Payment
                    && entry.SourceRef == originalSourceRef,
                ct)
            .ConfigureAwait(false);
        if (originalEntry is null)
        {
            _logger.LogWarning(
                "Skipping ledger reversal for payment {ReversalPaymentId}: original payment "
                + "{OriginalPaymentId} has no journal entry.",
                reversal.Id,
                original.Id);
            return;
        }

        var chartVariant = await TryResolveChartVariantAsync(tenantId, reversal.Id, ct)
            .ConfigureAwait(false);
        if (chartVariant is null)
        {
            return;
        }

        var accountIds = originalEntry.Postings
            .Select(posting => posting.AccountId)
            .Distinct()
            .ToArray();
        var accountNumbers = await _db.Set<Account>()
            .AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Number, ct)
            .ConfigureAwait(false);
        var bankPosting = originalEntry.Postings.Single(
            posting => posting.Direction == PostingDirection.Debit);
        var receivablePostings = originalEntry.Postings
            .Where(posting => posting.Direction == PostingDirection.Credit)
            .ToList();
        var input = new PaymentPostingInput(
            accountNumbers[bankPosting.AccountId],
            reversal.ValueDate,
            receivablePostings
                .Select(posting => new PaymentPostingAllocation(
                    accountNumbers[posting.AccountId],
                    posting.Amount))
                .ToList(),
            IsReversal: true);
        var header = new JournalEntry
        {
            TenantId = tenantId,
            EntryDate = reversal.ValueDate,
            SourceRef = reversalSourceRef,
            SourceType = LedgerSourceType.Payment,
            Description = $"Zahlungsstorno {original.Reference ?? original.Id.ToString()}",
            PostingType = PostingType.Storno,
            ReversesEntryId = originalEntry.Id,
        };

        await _postingEngine.PostAsync(
            new PaymentPostingSource(tenantId, chartVariant.Value, input, _accountResolver),
            header,
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<Guid, string?>> ResolveDebtorOverridesAsync(
        IEnumerable<OpenItem> openItems,
        CancellationToken ct)
    {
        var items = openItems.ToList();
        var documentIds = items.Select(item => item.DocumentId).Distinct().ToArray();
        var documentPartners = await _db.Set<SalesDocument>()
            .AsNoTracking()
            .Where(document => documentIds.Contains(document.Id))
            .Select(document => new { document.Id, document.PartnerId })
            .ToDictionaryAsync(document => document.Id, document => document.PartnerId, ct)
            .ConfigureAwait(false);
        var partnerIds = documentPartners.Values
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        var debtorAccounts = await _db.Set<BusinessPartner>()
            .AsNoTracking()
            .Where(partner => partnerIds.Contains(partner.Id))
            .ToDictionaryAsync(partner => partner.Id, partner => partner.DebtorAccount, ct)
            .ConfigureAwait(false);

        return items.ToDictionary(
            item => item.Id,
            item => documentPartners.TryGetValue(item.DocumentId, out var partnerId)
                && partnerId is { } id
                && debtorAccounts.TryGetValue(id, out var account)
                    ? account
                    : null);
    }

    private async Task<ChartVariant?> TryResolveChartVariantAsync(
        Guid tenantId,
        Guid paymentId,
        CancellationToken ct)
    {
        var chartVariant = await _db.Set<LedgerSettings>()
            .AsNoTracking()
            .Where(settings => settings.TenantId == tenantId)
            .Select(settings => (ChartVariant?)settings.ChartVariant)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (chartVariant is null)
        {
            _logger.LogWarning(
                "Skipping ledger booking for payment {PaymentId}: tenant {TenantId} has no ledger settings.",
                paymentId,
                tenantId);
        }

        return chartVariant;
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
