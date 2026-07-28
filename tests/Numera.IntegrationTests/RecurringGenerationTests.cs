using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Recurring;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class RecurringGenerationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Catch_up_auto_finalizes_is_idempotent_and_ends_after_count()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOccurrence = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        await SalesTestData.SeedProfileAsync(fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(
            fixture, tenant, DocumentType.Rechnung, "RE-", padding: 5, includeYear: true);
        var template = await SeedTemplateAsync(
            tenant,
            partner.Id,
            firstOccurrence,
            autoFinalize: true,
            maxOccurrences: 3);

        await RunJobAsync(tenant, template.Id);

        await using (var read = fixture.CreateAppContext(tenant))
        {
            var documents = await read.Set<SalesDocument>()
                .AsNoTracking()
                .Where(x => x.RecurringTemplateId == template.Id)
                .OrderBy(x => x.DocumentDate)
                .ToListAsync();

            Assert.Equal(3, documents.Count);
            Assert.All(documents, document =>
            {
                Assert.Equal(DocumentStatus.Finalized, document.Status);
                Assert.StartsWith("RE-", document.DocumentNumber, StringComparison.Ordinal);
            });
            Assert.Equal(3, documents.Select(x => x.RecurringPeriodKey).Distinct().Count());
            Assert.Equal(
                3,
                await read.Set<OpenItem>()
                    .CountAsync(x => documents.Select(d => d.Id).Contains(x.DocumentId)));

            var persisted = await read.Set<RecurringInvoiceTemplate>()
                .AsNoTracking()
                .SingleAsync(x => x.Id == template.Id);
            Assert.Equal(3, persisted.GeneratedCount);
            Assert.Equal(firstOccurrence.AddMonths(3), persisted.NextRunOn);
            Assert.Equal(RecurringStatus.Ended, persisted.Status);
        }

        await RunJobAsync(tenant, template.Id);

        await using (var read = fixture.CreateAppContext(tenant))
        {
            Assert.Equal(
                3,
                await read.Set<SalesDocument>()
                    .CountAsync(x => x.RecurringTemplateId == template.Id));
        }

        await using var other = fixture.CreateAppContext(otherTenant);
        Assert.Equal(
            0,
            await other.Set<SalesDocument>()
                .IgnoreQueryFilters()
                .CountAsync(x => x.RecurringTemplateId == template.Id));
    }

    [Fact]
    public async Task Auto_finalize_opt_out_leaves_an_unnumbered_draft_without_open_item()
    {
        var tenant = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var template = await SeedTemplateAsync(
            tenant,
            partner.Id,
            today,
            autoFinalize: false,
            maxOccurrences: 1);

        await RunJobAsync(tenant, template.Id);

        await using var read = fixture.CreateAppContext(tenant);
        var document = await read.Set<SalesDocument>()
            .AsNoTracking()
            .SingleAsync(x => x.RecurringTemplateId == template.Id);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Null(document.DocumentNumber);
        Assert.Equal(0, await read.Set<OpenItem>().CountAsync(x => x.DocumentId == document.Id));
    }

    [Fact]
    public async Task Foreign_currency_template_freezes_total_tax_in_eur()
    {
        var tenant = Guid.CreateVersion7();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SalesTestData.SeedProfileAsync(fixture, tenant);
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant);
        var template = await SeedTemplateAsync(
            tenant,
            partner.Id,
            today,
            autoFinalize: true,
            maxOccurrences: 1,
            currency: "USD",
            exchangeRate: 1.25m);

        await RunJobAsync(tenant, template.Id);

        await using var read = fixture.CreateAppContext(tenant);
        var document = await read.Set<SalesDocument>()
            .AsNoTracking()
            .SingleAsync(x => x.RecurringTemplateId == template.Id);
        Assert.Equal(DocumentStatus.Finalized, document.Status);
        Assert.Equal(15.20m, document.TotalTaxEur);
        Assert.Equal(1.25m, document.ExchangeRate);
        Assert.Equal(today, document.ExchangeRateDate);
    }

    private async Task<RecurringInvoiceTemplate> SeedTemplateAsync(
        Guid tenant,
        Guid partnerId,
        DateOnly firstOccurrence,
        bool autoFinalize,
        int maxOccurrences,
        string currency = "EUR",
        decimal? exchangeRate = null)
    {
        var template = new RecurringInvoiceTemplate
        {
            TenantId = tenant,
            Name = "Recurring consulting",
            PartnerId = partnerId,
            Currency = currency,
            ExchangeRate = exchangeRate,
            ExchangeRateDate = exchangeRate is null ? null : firstOccurrence,
            IntervalUnit = RecurringIntervalUnit.Monthly,
            IntervalCount = 1,
            StartOn = firstOccurrence,
            EndMode = RecurringEndMode.AfterCount,
            MaxOccurrences = maxOccurrences,
            NextRunOn = firstOccurrence,
            Status = RecurringStatus.Active,
            AutoFinalize = autoFinalize,
        };
        template.Lines.Add(new RecurringInvoiceTemplateLine
        {
            TenantId = tenant,
            TemplateId = template.Id,
            LineNumber = 1,
            Name = "Consulting",
            Quantity = 1m,
            UnitCode = "C62",
            NetUnitPrice = 100m,
            TaxCategory = TaxCategory.S,
            VatRatePercent = 19m,
        });

        await using var db = fixture.CreateAppContext(tenant);
        db.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private async Task RunJobAsync(Guid tenant, Guid templateId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(
            options => options.UseNpgsql(fixture.AppConnectionString));
        services.AddScoped<NumberingService>();
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        services.AddScoped<IEInvoiceValidator, AcceptingValidator>();
        services.AddScoped<EInvoiceService>();
        services.AddScoped<IDomainEventPublisher, NoOpPublisher>();

        await using var provider = services.BuildServiceProvider();
        var job = new GenerateRecurringInvoiceJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GenerateRecurringInvoiceJob>.Instance);
        await job.RunAsync(tenant, templateId, CancellationToken.None);
    }

    private sealed class AcceptingValidator : IEInvoiceValidator
    {
        public Task<EInvoiceValidationResult> ValidateAsync(
            byte[] xml,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EInvoiceValidationResult(
                    EInvoiceValidationStatus.Accepted,
                    [],
                    RawReport: null));
    }

    private sealed class NoOpPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(object domainEvent, CancellationToken ct) => Task.CompletedTask;
    }
}
