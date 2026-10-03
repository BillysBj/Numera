using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger.Tax;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Records EÜR-only cash settlements. Ledger/DATEV posting awaits the B5 clearing accounts.</summary>
public sealed class VatPaymentService(NumeraDbContext db, ICurrentTenant currentTenant, IAuditWriter audit)
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<PaymentOperationResult> RecordAsync(
        decimal? amount, VatPaymentKind kind, DateOnly valueDate, string? reference,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to record a VAT payment.");
        if (amount is null or <= 0m)
        {
            return PaymentOperationResult.Invalid("amount", "Der Zahlungsbetrag muss größer als 0 sein.");
        }

        if (!Enum.IsDefined(kind))
        {
            return PaymentOperationResult.Invalid("kind", "Die Zahlungsart ist ungültig.");
        }

        if (valueDate == default)
        {
            return PaymentOperationResult.Invalid("valueDate", "Das Wertstellungsdatum ist erforderlich.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var payment = new VatPayment
        {
            TenantId = tenantId,
            Amount = amount.Value,
            Kind = kind,
            ValueDate = valueDate,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            RecordedAt = DateTimeOffset.UtcNow,
        };
        db.Add(payment);
        await audit.RecordAsync(new VatPaymentAuditEvent(
            "vat_payment.recorded", payment.Id, null, Snapshot(payment)), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(payment.Id);
    }

    public async Task<PaymentOperationResult> ReverseAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to reverse a VAT payment.");
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Serialize reversals without UPDATE privileges on this append-only table.
        // RLS and the EF tenant filter apply to the subsequent lookup.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString() + ":" + id.ToString()}, 0))", ct)
            .ConfigureAwait(false);
        var original = await db.Set<VatPayment>().AsNoTracking()
            .SingleOrDefaultAsync(payment => payment.Id == id, ct).ConfigureAwait(false);
        if (original is null)
        {
            return PaymentOperationResult.NotFound();
        }

        if (original.ReversesPaymentId is not null
            || await db.Set<VatPayment>().AnyAsync(payment => payment.ReversesPaymentId == id, ct)
                .ConfigureAwait(false))
        {
            return PaymentOperationResult.Conflict("Die Zahlung wurde bereits storniert.");
        }

        var reversal = new VatPayment
        {
            TenantId = tenantId,
            Amount = original.Amount,
            Kind = original.Kind == VatPaymentKind.Payment ? VatPaymentKind.Refund : VatPaymentKind.Payment,
            ValueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Reference = $"Storno: {original.Reference}".TrimEnd(),
            ReversesPaymentId = original.Id,
            RecordedAt = DateTimeOffset.UtcNow,
        };
        db.Add(reversal);
        await audit.RecordAsync(new VatPaymentAuditEvent(
            "vat_payment.reversed", reversal.Id, Snapshot(original), Snapshot(reversal)), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return PaymentOperationResult.Ok(reversal.Id);
    }

    private static string Snapshot(VatPayment payment) => JsonSerializer.Serialize(new
    {
        payment.Id, payment.Amount, Kind = (int)payment.Kind, payment.ValueDate,
        payment.Reference, payment.ReversesPaymentId, payment.RecordedAt,
    }, AuditJson);
}

internal sealed record VatPaymentAuditEvent(
    string Action, Guid? EntityId, string? Before, string? After) : IAuditEvent
{
    public string EntityType => nameof(VatPayment);
}
