using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Db;

namespace Numera.Api.Services;

/// <summary>Shared, tenant-scoped PDF artifact used by downloads and email delivery.</summary>
public sealed class DunningNoticePdfService(NumeraDbContext db)
{
    public async Task<byte[]?> GetOrRenderAsync(Guid id, string language, CancellationToken ct)
    {
        var notice = await db.Set<DunningNotice>().FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
        if (notice is null)
        {
            return null;
        }

        if (notice.RenderedPdf is not null)
        {
            return notice.RenderedPdf;
        }

        var bytes = await RenderAsync(notice, language, ct).ConfigureAwait(false);
        // Concurrent email/download requests must both use the winning stored artifact.
        await db.Set<DunningNotice>().Where(x => x.Id == id && x.RenderedPdf == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RenderedPdf, bytes), ct)
            .ConfigureAwait(false);
        await db.Entry(notice).ReloadAsync(ct).ConfigureAwait(false);
        return notice.RenderedPdf;
    }

    /// <summary>Freezes the artifact at issue time; also supports legacy notices without a PDF.</summary>
    public async Task<byte[]> RenderAsync(DunningNotice notice, string language, CancellationToken ct)
    {
        var document = await db.Set<SalesDocument>().AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.TaxBreakdown)
            .FirstAsync(x => x.Id == notice.DocumentId, ct).ConfigureAwait(false);
        var configs = await db.Set<DunningLevelConfig>().AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        var config = configs.Single(x => x.Level == notice.Level);
        var logo = await db.Set<CompanyProfile>().AsNoTracking().Select(x => x.LogoBytes)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return DunningNoticeDocument.Render(BuildModel(
            notice, document, config, configs.Max(x => x.Level), logo, language));
    }

    public static DunningNoticeModel BuildModel(
        DunningNotice notice, SalesDocument document, DunningLevelConfig config,
        int maxLevel, byte[]? logo = null, string language = "de")
    {
        var frozen = SnapshotReader.FromDocument(document, logo, null, language);
        return new DunningNoticeModel
        {
            Invoice = frozen,
            Language = language,
            LogoBytes = logo,
            Issuer = frozen.Issuer,
            Recipient = frozen.Recipient,
            InvoiceNumber = document.DocumentNumber ?? document.Id.ToString(),
            InvoiceDate = document.DocumentDate,
            Currency = document.Currency,
            OverdueAmount = notice.OverdueAmount,
            Fee = notice.Fee,
            Interest = notice.Interest,
            InterestRatePercent = notice.InterestRatePercent,
            DaysOverdue = notice.IssuedOn.DayNumber - document.DueDate!.Value.DayNumber,
            TotalToPay = notice.TotalToPay,
            NewDueDate = notice.NewDueDate,
            LevelName = config.Name,
            // Configured legal prose stays German, matching the existing email artifact.
            TemplateText = config.TemplateTextDe,
            IsFinalNotice = notice.Level == maxLevel,
        };
    }
}
