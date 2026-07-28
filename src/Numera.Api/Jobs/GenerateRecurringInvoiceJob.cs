using Hangfire;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Modules.Sales.Recurring;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Jobs;

/// <summary>Generates every due occurrence of one tenant-scoped recurring invoice template.</summary>
[AutomaticRetry(Attempts = 3)]
public sealed class GenerateRecurringInvoiceJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenerateRecurringInvoiceJob> _logger;

    public GenerateRecurringInvoiceJob(
        IServiceScopeFactory scopeFactory,
        ILogger<GenerateRecurringInvoiceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(Guid tenantId, Guid templateId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);

        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NumeraDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        while (true)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var template = await db.Set<RecurringInvoiceTemplate>()
                .FromSqlInterpolated(
                    $"SELECT * FROM recurring_invoice_templates WHERE id = {templateId} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (template is null || template.Status != RecurringStatus.Active)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            await db.Entry(template)
                .Collection(x => x.Lines)
                .LoadAsync(ct)
                .ConfigureAwait(false);

            if (IsExhausted(template) || template.NextRunOn > today)
            {
                if (IsExhausted(template))
                {
                    template.Status = RecurringStatus.Ended;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    services.GetService<IRecurringJobManager>()?.RemoveIfExists(
                        RecurringInvoiceEndpoints.RecurringJobId(tenantId, templateId));
                }
                else
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                }

                return;
            }

            var occurrence = template.NextRunOn;
            var nextOccurrence = NextOccurrence(template, occurrence);
            var periodEnd = nextOccurrence.AddDays(-1);
            var periodKey = PeriodKey(template, occurrence, periodEnd);
            SalesDocument? document = null;
            var finalized = false;

            try
            {
                document = BuildDraft(template, tenantId, occurrence, periodEnd, periodKey);
                db.Add(document);

                if (template.AutoFinalize)
                {
                    var profile = await db.Set<CompanyProfile>()
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    var partner = template.PartnerId is null
                        ? null
                        : await db.Set<BusinessPartner>()
                            .FirstOrDefaultAsync(x => x.Id == template.PartnerId, ct)
                            .ConfigureAwait(false);
                    var errors = FinalizeValidation.Check(document, profile, partner);

                    if (errors.Count == 0)
                    {
                        var dryRun = await services.GetRequiredService<EInvoiceService>()
                            .DryRunAsync(document, profile!, partner, ct)
                            .ConfigureAwait(false);
                        if (dryRun.Status != EInvoiceValidationStatus.Rejected)
                        {
                            if (dryRun.Status == EInvoiceValidationStatus.Unavailable)
                            {
                                _logger.LogWarning(
                                    "Recurring invoice {DocumentId}: pre-finalize KoSIT dry-run unavailable; finalizing anyway.",
                                    document.Id);
                            }

                            await SalesDocumentEndpoints.FinalizeCoreAsync(
                                document,
                                profile!,
                                partner,
                                db,
                                services.GetRequiredService<NumberingService>(),
                                services.GetRequiredService<IAuditWriter>(),
                                tenantId,
                                "sales_document.recurring_generated",
                                ct).ConfigureAwait(false);
                            finalized = true;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Recurring invoice {DocumentId} for template {TemplateId} failed the KoSIT dry-run and remains Draft.",
                                document.Id,
                                templateId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Recurring invoice {DocumentId} for template {TemplateId} failed finalize validation and remains Draft.",
                            document.Id,
                            templateId);
                    }
                }

                template.NextRunOn = nextOccurrence;
                template.LastGeneratedPeriodEnd = periodEnd;
                template.GeneratedCount++;
                if (IsExhausted(template))
                {
                    template.Status = RecurringStatus.Ended;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                _logger.LogInformation(
                    "Recurring invoice for template {TemplateId}, period {PeriodKey} already exists; skipping.",
                    templateId,
                    periodKey);
                await AdvancePastExistingAsync(db, templateId, occurrence, nextOccurrence, periodEnd, ct)
                    .ConfigureAwait(false);
                continue;
            }

            if (finalized)
            {
                await services.GetRequiredService<IDomainEventPublisher>()
                    .PublishAsync(
                        new InvoiceFinalized(
                            tenantId,
                            document!.Id,
                            document.DocumentNumber!,
                            document.TotalNet,
                            document.TotalTax,
                            document.TotalGross,
                            document.DocumentDate,
                            document.DocumentType),
                        ct)
                    .ConfigureAwait(false);

                if (template.AutoSend)
                {
                    await QueueEmailAsync(services, db, tenantId, document, ct)
                        .ConfigureAwait(false);
                }
            }

            if (template.Status == RecurringStatus.Ended)
            {
                services.GetService<IRecurringJobManager>()?.RemoveIfExists(
                    RecurringInvoiceEndpoints.RecurringJobId(tenantId, templateId));
                return;
            }

            db.ChangeTracker.Clear();
        }
    }

    private static SalesDocument BuildDraft(
        RecurringInvoiceTemplate template,
        Guid tenantId,
        DateOnly occurrence,
        DateOnly periodEnd,
        string periodKey)
    {
        var document = new SalesDocument
        {
            TenantId = tenantId,
            DocumentType = DocumentType.Rechnung,
            Status = DocumentStatus.Draft,
            PartnerId = template.PartnerId,
            DocumentDate = occurrence,
            ServiceDate = occurrence,
            ServicePeriodEnd = periodEnd,
            Currency = template.Currency,
            ExchangeRate = template.ExchangeRate,
            ExchangeRateDate = template.ExchangeRateDate,
            RecurringTemplateId = template.Id,
            RecurringPeriodKey = periodKey,
        };

        foreach (var line in template.Lines.OrderBy(x => x.LineNumber))
        {
            document.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenantId,
                DocumentId = document.Id,
                LineNumber = line.LineNumber,
                CatalogItemId = line.CatalogItemId,
                Name = line.Name,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitCode = line.UnitCode,
                NetUnitPrice = line.NetUnitPrice,
                LineNetAmount = Math.Round(
                    line.Quantity * line.NetUnitPrice,
                    4,
                    MidpointRounding.AwayFromZero),
                TaxCategory = line.TaxCategory,
                VatRatePercent = line.VatRatePercent,
            });
        }

        document.TotalNet = document.Lines.Sum(x => x.LineNetAmount);
        return document;
    }

    private static DateOnly NextOccurrence(RecurringInvoiceTemplate template, DateOnly occurrence)
        => template.IntervalUnit switch
        {
            RecurringIntervalUnit.Monthly => occurrence.AddMonths(template.IntervalCount),
            RecurringIntervalUnit.Quarterly => occurrence.AddMonths(3 * template.IntervalCount),
            RecurringIntervalUnit.Yearly => occurrence.AddYears(template.IntervalCount),
            RecurringIntervalUnit.Weekly => occurrence.AddDays(7 * template.IntervalCount),
            _ => throw new ArgumentOutOfRangeException(nameof(template)),
        };

    private static string PeriodKey(
        RecurringInvoiceTemplate template,
        DateOnly occurrence,
        DateOnly periodEnd)
        => template.IntervalUnit == RecurringIntervalUnit.Monthly && template.IntervalCount == 1
            ? occurrence.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
            : $"{occurrence:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}";

    private static bool IsExhausted(RecurringInvoiceTemplate template)
        => template.EndMode switch
        {
            RecurringEndMode.UntilDate => template.EndDate is { } endDate
                && template.NextRunOn > endDate,
            RecurringEndMode.AfterCount => template.MaxOccurrences is { } max
                && template.GeneratedCount >= max,
            _ => false,
        };

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_sales_documents_tenant_id_recurring_template_id_recurring_p~",
        };

    private static async Task AdvancePastExistingAsync(
        NumeraDbContext db,
        Guid templateId,
        DateOnly occurrence,
        DateOnly nextOccurrence,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var template = await db.Set<RecurringInvoiceTemplate>()
            .FromSqlInterpolated(
                $"SELECT * FROM recurring_invoice_templates WHERE id = {templateId} FOR UPDATE")
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (template is not null
            && template.Status == RecurringStatus.Active
            && template.NextRunOn == occurrence)
        {
            template.NextRunOn = nextOccurrence;
            template.LastGeneratedPeriodEnd = periodEnd;
            template.GeneratedCount++;
            if (IsExhausted(template))
            {
                template.Status = RecurringStatus.Ended;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    private static async Task QueueEmailAsync(
        IServiceProvider services,
        NumeraDbContext db,
        Guid tenantId,
        SalesDocument document,
        CancellationToken ct)
    {
        var partnerEmail = document.PartnerId is { } partnerId
            ? await db.Set<BusinessPartner>()
                .AsNoTracking()
                .Where(x => x.Id == partnerId)
                .Select(x => x.Email)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : null;
        if (string.IsNullOrWhiteSpace(partnerEmail))
        {
            return;
        }

        var email = new DocumentEmail
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            ToAddress = partnerEmail,
            Subject = DocumentEmailTemplates.Build("de", document.DocumentNumber!).Subject,
            Status = EmailStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(email);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        services.GetRequiredService<IBackgroundJobClient>().Enqueue<SendDocumentEmailJob>(
            job => job.RunAsync(tenantId, email.Id, "de", false, CancellationToken.None));
    }
}
