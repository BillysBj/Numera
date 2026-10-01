using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class DunningNoticePdfTests
{
    [Fact]
    public void Shared_model_uses_frozen_recipient_and_notice_claims_and_renders_a_pdf()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var document = new SalesDocument
        {
            DocumentNumber = "RE-001", DocumentDate = new(2026, 8, 1), DueDate = new(2026, 8, 15),
            RecipientSnapshot = "{\"name\":\"Kunde zur Ausstellung\",\"email\":\"frozen@example.com\"}",
            IssuerSnapshot = "{\"legalName\":\"Numera Test GmbH\"}",
        };
        var notice = new DunningNotice
        {
            Level = 2, IssuedOn = new(2026, 9, 15), NewDueDate = new(2026, 9, 29),
            OverdueAmount = 100m, Fee = 5m, Interest = 1m, InterestRatePercent = 10m, TotalToPay = 106m,
        };
        var config = new DunningLevelConfig
        {
            Level = 2, Name = "Letzte Mahnung", TemplateTextDe = "Bitte zahlen.", TemplateTextEn = "Please pay.",
        };
        var model = DunningNoticePdfService.BuildModel(notice, document, config, 2);
        Assert.Equal("Kunde zur Ausstellung", model.Recipient.Name);
        Assert.Equal("frozen@example.com", model.Recipient.Email);
        Assert.Equal(100m, model.OverdueAmount);
        Assert.Equal(106m, model.TotalToPay);
        Assert.Equal(31, model.DaysOverdue);
        Assert.True(model.IsFinalNotice);
        Assert.Equal("Bitte zahlen.", model.TemplateText);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(DunningNoticeDocument.Render(model), 0, 4));
    }

    [Fact]
    public async Task Stored_artifact_is_returned_unchanged_without_live_config_and_other_tenants_are_hidden()
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var own = new DunningNotice { TenantId = tenant.TenantId!.Value, RenderedPdf = [1, 2, 3], Status = 2 };
        var other = new DunningNotice { TenantId = Guid.CreateVersion7(), RenderedPdf = [4, 5] };
        db.AddRange(own, other);
        await db.SaveChangesAsync();
        var service = new DunningNoticePdfService(db);
        Assert.Equal(own.RenderedPdf, await service.GetOrRenderAsync(own.Id, "de", CancellationToken.None));
        Assert.Equal(2, own.Status);
        Assert.Null(await service.GetOrRenderAsync(other.Id, "de", CancellationToken.None));
        Assert.Null(await service.GetOrRenderAsync(Guid.CreateVersion7(), "de", CancellationToken.None));
    }
}
