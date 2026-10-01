using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using UglyToad.PdfPig;

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
    public async Task Full_invoice_pages_precede_the_dunning_notice()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var document = new SalesDocument
        {
            TenantId = tenant.TenantId!.Value, DocumentType = DocumentType.Rechnung,
            DocumentNumber = "RE-002", DocumentDate = new(2026, 8, 1), DueDate = new(2026, 8, 15),
            IssuerSnapshot = "{\"legalName\":\"Numera Test GmbH\"}",
            RecipientSnapshot = "{\"name\":\"Frozen customer\"}",
            TotalNet = 600m, TotalTax = 114m, TotalGross = 714m, AmountDue = 714m,
        };
        document.Lines = Enumerable.Range(1, 60).Select(i => new SalesDocumentLine
        {
            TenantId = tenant.TenantId.Value, DocumentId = document.Id, LineNumber = i,
            Name = $"Frozen item {i:D2}", UnitCode = "C62", Quantity = 1m,
            NetUnitPrice = 10m, LineNetAmount = 10m, TaxCategory = TaxCategory.S, VatRatePercent = 19m,
        }).ToList();
        document.TaxBreakdown.Add(new SalesDocumentTaxBreakdown
        {
            TenantId = tenant.TenantId.Value, DocumentId = document.Id,
            TaxCategory = TaxCategory.S, VatRatePercent = 19m, TaxableBase = 600m, TaxAmount = 114m,
        });
        var config = new DunningLevelConfig
        {
            TenantId = tenant.TenantId.Value, Level = 1, Name = "Mahnung",
            TemplateTextDe = "Bitte zahlen.", TemplateTextEn = "Please pay.",
        };
        db.AddRange(document, config);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var notice = new DunningNotice
        {
            DocumentId = document.Id, Level = 1, IssuedOn = new(2026, 9, 15),
            NewDueDate = new(2026, 9, 29), OverdueAmount = 714m, TotalToPay = 714m,
        };

        var service = new DunningNoticePdfService(db);
        foreach (var language in new[] { "de", "en" })
        {
            using var pdf = PdfDocument.Open(await service.RenderAsync(notice, language, CancellationToken.None));
            Assert.True(pdf.NumberOfPages > 2); // Multi-page invoice plus a separate notice.
            var invoiceText = string.Join(" ", pdf.GetPages().Take(pdf.NumberOfPages - 1).Select(p => p.Text));
            foreach (var line in document.Lines)
            {
                Assert.Contains(line.Name, invoiceText, StringComparison.Ordinal);
            }
            Assert.Contains(language == "de" ? "600,00" : "600.00", invoiceText, StringComparison.Ordinal);
            Assert.Contains(language == "de" ? "114,00" : "114.00", invoiceText, StringComparison.Ordinal);
            Assert.Contains(language == "de" ? "714,00" : "714.00", invoiceText, StringComparison.Ordinal);
            Assert.Contains(PdfLabels.For(language).VatBreakdown.ToUpperInvariant(), invoiceText, StringComparison.Ordinal);
            Assert.DoesNotContain("Bitte zahlen.", invoiceText, StringComparison.Ordinal);
            Assert.Contains("Bitte zahlen.", pdf.GetPage(pdf.NumberOfPages).Text, StringComparison.Ordinal);
            foreach (var page in pdf.GetPages())
            {
                Assert.Contains($"{page.Number} / {pdf.NumberOfPages}", page.Text, StringComparison.Ordinal);
            }
        }
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
