using System.Text.Json;

using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Asynchronously extracts review proposals from a captured Tier-B receipt.</summary>
[AutomaticRetry(Attempts = 3)]
public sealed class ExtractReceiptJob
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractReceiptJob> _logger;

    /// <summary>Creates the Hangfire job over the root scope factory.</summary>
    public ExtractReceiptJob(IServiceScopeFactory scopeFactory, ILogger<ExtractReceiptJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Re-establishes tenant context, extracts fields and leaves them in Extracted.</summary>
    public async Task RunAsync(
        Guid tenantId,
        Guid receiptId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // Mirror every RLS-safe Hangfire job: set tenant before resolving/using the DbContext.
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NumeraDbContext>();
        var extractor = services.GetRequiredService<IReceiptExtractor>();
        var matcher = services.GetRequiredService<SupplierMatcher>();

        var receipt = await db.Set<Receipt>()
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is null || receipt.Status != ReceiptStatus.Captured)
        {
            _logger.LogWarning(
                "Receipt extraction skipped for {ReceiptId} of tenant {TenantId}: missing or not Captured.",
                receiptId, tenantId);
            return;
        }

        if (receipt.ArchiveId is not { } archiveId)
        {
            _logger.LogWarning(
                "Receipt extraction skipped for {ReceiptId} of tenant {TenantId}: archive link is missing.",
                receiptId, tenantId);
            return;
        }

        var archive = await db.Set<ReceiptArchive>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == archiveId, cancellationToken)
            .ConfigureAwait(false);
        if (archive is null)
        {
            _logger.LogWarning(
                "Receipt extraction skipped for {ReceiptId} of tenant {TenantId}: archive {ArchiveId} not found.",
                receiptId, tenantId, archiveId);
            return;
        }

        var extraction = await extractor
            .ExtractAsync(archive.OriginalBytes, archive.ContentType, cancellationToken)
            .ConfigureAwait(false);

        receipt.SupplierName = extraction.SupplierName?.Value;
        receipt.SupplierVatId = extraction.SupplierVatId?.Value;
        receipt.InvoiceNumber = extraction.InvoiceNumber?.Value;
        receipt.InvoiceDate = extraction.InvoiceDate?.Value;
        receipt.NetAmount = extraction.NetAmount?.Value;
        receipt.VatAmount = extraction.VatAmount?.Value;
        receipt.GrossAmount = extraction.GrossAmount?.Value;
        receipt.VatRatePercent = extraction.VatRatePercent?.Value;
        receipt.Currency = extraction.Currency;
        receipt.FieldConfidence = JsonSerializer.Serialize(
            new
            {
                supplierName = extraction.SupplierName?.Confidence,
                supplierVatId = extraction.SupplierVatId?.Confidence,
                invoiceNumber = extraction.InvoiceNumber?.Confidence,
                invoiceDate = extraction.InvoiceDate?.Confidence,
                netAmount = extraction.NetAmount?.Confidence,
                vatAmount = extraction.VatAmount?.Confidence,
                grossAmount = extraction.GrossAmount?.Confidence,
                vatRatePercent = extraction.VatRatePercent?.Confidence,
            },
            Json);

        receipt.MatchedPartnerId = await matcher.MatchSellerAsync(
            db,
            receipt.SupplierVatId,
            receipt.SupplierName,
            cancellationToken).ConfigureAwait(false);
        receipt.Status = ReceiptStatus.Extracted;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Extracted receipt {ReceiptId} for tenant {TenantId}; awaiting human review.",
            receiptId, tenantId);
    }
}
