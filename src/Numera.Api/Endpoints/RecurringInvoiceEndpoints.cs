using System.Linq.Expressions;

using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Jobs;
using Numera.Modules.Sales.Recurring;
using Numera.Modules.Sales.Money;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>CRUD and per-template Hangfire scheduling for recurring invoices.</summary>
public static class RecurringInvoiceEndpoints
{
    public static IEndpointRouteBuilder MapRecurringInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recurring-templates").RequireAuthorization();

        group.MapGet("/", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var templates = await db.Set<RecurringInvoiceTemplate>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .OrderBy(x => x.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(templates.Select(ToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var template = await db.Set<RecurringInvoiceTemplate>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            return template is null ? Results.NotFound() : Results.Ok(ToResponse(template));
        });

        group.MapPost("/", async (
            UpsertRecurringInvoiceTemplateRequest request,
            NumeraDbContext db,
            ICurrentTenant currentTenant,
            IEntitlementService entitlements,
            IRecurringJobManager recurringJobs,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var tenantId = currentTenant.TenantId
                ?? throw new InvalidOperationException("A tenant is required.");
            var template = new RecurringInvoiceTemplate
            {
                TenantId = tenantId,
                Name = request.Name.Trim(),
                NextRunOn = request.StartOn,
            };
            Apply(template, request);
            db.Add(template);
            AddLines(db, template.Id, tenantId, request.Lines);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (template.Status == RecurringStatus.Active)
            {
                Register(recurringJobs, template);
            }

            return Results.Created($"/api/recurring-templates/{template.Id}", new { template.Id });
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpsertRecurringInvoiceTemplateRequest request,
            NumeraDbContext db,
            IEntitlementService entitlements,
            IRecurringJobManager recurringJobs,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var template = await db.Set<RecurringInvoiceTemplate>()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (template is null)
            {
                return Results.NotFound();
            }

            foreach (var line in template.Lines)
            {
                db.Remove(line);
            }

            Apply(template, request);
            template.NextRunOn = request.StartOn;
            AddLines(db, template.Id, template.TenantId, request.Lines);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (template.Status == RecurringStatus.Active)
            {
                Register(recurringJobs, template);
            }
            else
            {
                recurringJobs.RemoveIfExists(RecurringJobId(template.TenantId, template.Id));
            }

            return Results.Ok(ToResponse(template, request.Lines));
        });

        group.MapPost("/{id:guid}/activate", async (
            Guid id,
            NumeraDbContext db,
            IEntitlementService entitlements,
            IRecurringJobManager recurringJobs,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var template = await db.Set<RecurringInvoiceTemplate>()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (template is null)
            {
                return Results.NotFound();
            }

            template.Status = RecurringStatus.Active;
            template.NextRunOn = template.NextRunOn < template.StartOn
                ? template.StartOn
                : template.NextRunOn;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            Register(recurringJobs, template);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/pause", async (
            Guid id,
            NumeraDbContext db,
            IEntitlementService entitlements,
            IRecurringJobManager recurringJobs,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var template = await db.Set<RecurringInvoiceTemplate>()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (template is null)
            {
                return Results.NotFound();
            }

            template.Status = RecurringStatus.Paused;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            recurringJobs.RemoveIfExists(RecurringJobId(template.TenantId, template.Id));
            return Results.Ok();
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            NumeraDbContext db,
            IEntitlementService entitlements,
            IRecurringJobManager recurringJobs,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var template = await db.Set<RecurringInvoiceTemplate>()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (template is null)
            {
                return Results.NotFound();
            }

            db.Remove(template);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            recurringJobs.RemoveIfExists(RecurringJobId(template.TenantId, template.Id));
            return Results.NoContent();
        });

        return app;
    }

    internal static string RecurringJobId(Guid tenantId, Guid templateId)
        => $"recurring-invoice:{tenantId}:{templateId}";

    internal static Task<bool> HasCapabilityAsync(IEntitlementService entitlements, CancellationToken ct)
        => entitlements.HasCapabilityAsync(Capability.RecurringInvoices, ct);

    private static void Register(
        IRecurringJobManager recurringJobs,
        RecurringInvoiceTemplate template)
    {
        var tenantId = template.TenantId;
        var templateId = template.Id;
        Expression<Func<GenerateRecurringInvoiceJob, Task>> invocation =
            job => job.RunAsync(tenantId, templateId, CancellationToken.None);
        recurringJobs.AddOrUpdate(
            RecurringJobId(tenantId, templateId),
            invocation,
            CronExpression(template));
    }

    internal static string CronExpression(RecurringInvoiceTemplate template)
    {
        var day = template.StartOn.Day;
        return template.IntervalUnit switch
        {
            RecurringIntervalUnit.Monthly =>
                $"0 0 {day} */{template.IntervalCount} *",
            RecurringIntervalUnit.Quarterly =>
                $"0 0 {day} */{3 * template.IntervalCount} *",
            RecurringIntervalUnit.Yearly =>
                $"0 0 {day} {template.StartOn.Month} *",
            RecurringIntervalUnit.Weekly =>
                $"0 0 * * {(int)template.StartOn.DayOfWeek}",
            _ => throw new ArgumentOutOfRangeException(nameof(template)),
        };
    }

    private static Dictionary<string, string[]> Validate(UpsertRecurringInvoiceTemplateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }

        if (request.IntervalCount < 1)
        {
            errors[nameof(request.IntervalCount)] = ["IntervalCount must be at least 1."];
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "EUR" : request.Currency;
        if (!CurrencyScope.IsSupported(currency))
        {
            errors[nameof(request.Currency)] = [CurrencyScope.UnsupportedReason];
        }
        else if (!string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)
                 && (request.ExchangeRate is not > 0 || request.ExchangeRateDate is null))
        {
            errors[nameof(request.ExchangeRate)] =
                ["A positive exchange rate and exchange-rate date are required for foreign currency."];
        }

        if (request.EndMode == RecurringEndMode.UntilDate
            && (request.EndDate is null || request.EndDate < request.StartOn))
        {
            errors[nameof(request.EndDate)] = ["EndDate must be on or after StartOn."];
        }

        if (request.EndMode == RecurringEndMode.AfterCount
            && request.MaxOccurrences is not > 0)
        {
            errors[nameof(request.MaxOccurrences)] = ["MaxOccurrences must be greater than 0."];
        }

        if (request.Lines.Count == 0)
        {
            errors[nameof(request.Lines)] = ["At least one line is required."];
        }
        else if (request.Lines.Any(x =>
                     string.IsNullOrWhiteSpace(x.Name)
                     || string.IsNullOrWhiteSpace(x.UnitCode)
                     || x.Quantity <= 0
                     || x.NetUnitPrice < 0))
        {
            errors[nameof(request.Lines)] = ["Every line needs a name, unit, positive quantity, and non-negative price."];
        }

        return errors;
    }

    private static void Apply(
        RecurringInvoiceTemplate template,
        UpsertRecurringInvoiceTemplateRequest request)
    {
        template.Name = request.Name.Trim();
        template.PartnerId = request.PartnerId;
        template.Currency = string.IsNullOrWhiteSpace(request.Currency)
            ? "EUR"
            : request.Currency.Trim().ToUpperInvariant();
        template.ExchangeRate = template.Currency == "EUR" ? null : request.ExchangeRate;
        template.ExchangeRateDate = template.Currency == "EUR" ? null : request.ExchangeRateDate;
        template.IntervalUnit = request.IntervalUnit;
        template.IntervalCount = request.IntervalCount;
        template.StartOn = request.StartOn;
        template.EndMode = request.EndMode;
        template.EndDate = request.EndMode == RecurringEndMode.UntilDate ? request.EndDate : null;
        template.MaxOccurrences = request.EndMode == RecurringEndMode.AfterCount ? request.MaxOccurrences : null;
        template.Status = request.Status;
        template.AutoFinalize = request.AutoFinalize;
        template.AutoSend = request.AutoSend;
    }

    private static void AddLines(
        NumeraDbContext db,
        Guid templateId,
        Guid tenantId,
        IReadOnlyList<RecurringInvoiceTemplateLineRequest> requests)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            var line = requests[i];
            db.Add(new RecurringInvoiceTemplateLine
            {
                TenantId = tenantId,
                TemplateId = templateId,
                LineNumber = i + 1,
                CatalogItemId = line.CatalogItemId,
                Name = line.Name.Trim(),
                Description = line.Description,
                Quantity = line.Quantity,
                UnitCode = line.UnitCode.Trim(),
                NetUnitPrice = line.NetUnitPrice,
                TaxCategory = line.TaxCategory,
                VatRatePercent = line.VatRatePercent,
            });
        }
    }

    private static RecurringInvoiceTemplateResponse ToResponse(RecurringInvoiceTemplate template)
        => new(
            template.Id,
            template.Name,
            template.PartnerId,
            template.Currency,
            template.ExchangeRate,
            template.ExchangeRateDate,
            template.IntervalUnit,
            template.IntervalCount,
            template.StartOn,
            template.EndMode,
            template.EndDate,
            template.MaxOccurrences,
            template.NextRunOn,
            template.LastGeneratedPeriodEnd,
            template.GeneratedCount,
            template.Status,
            template.AutoFinalize,
            template.AutoSend,
            template.Lines.OrderBy(x => x.LineNumber).Select(ToLineResponse).ToList());

    private static RecurringInvoiceTemplateResponse ToResponse(
        RecurringInvoiceTemplate template,
        IReadOnlyList<RecurringInvoiceTemplateLineRequest> lines)
        => new(
            template.Id,
            template.Name,
            template.PartnerId,
            template.Currency,
            template.ExchangeRate,
            template.ExchangeRateDate,
            template.IntervalUnit,
            template.IntervalCount,
            template.StartOn,
            template.EndMode,
            template.EndDate,
            template.MaxOccurrences,
            template.NextRunOn,
            template.LastGeneratedPeriodEnd,
            template.GeneratedCount,
            template.Status,
            template.AutoFinalize,
            template.AutoSend,
            lines.Select((x, i) => new RecurringInvoiceTemplateLineResponse(
                Guid.Empty,
                i + 1,
                x.CatalogItemId,
                x.Name,
                x.Description,
                x.Quantity,
                x.UnitCode,
                x.NetUnitPrice,
                x.TaxCategory,
                x.VatRatePercent)).ToList());

    private static RecurringInvoiceTemplateLineResponse ToLineResponse(
        RecurringInvoiceTemplateLine line)
        => new(
            line.Id,
            line.LineNumber,
            line.CatalogItemId,
            line.Name,
            line.Description,
            line.Quantity,
            line.UnitCode,
            line.NetUnitPrice,
            line.TaxCategory,
            line.VatRatePercent);

    private static IResult UpgradeRequired()
        => Results.Problem(
            title: "Upgrade required",
            detail: "Recurring invoices require the RecurringInvoices capability (plan L or XL).",
            statusCode: StatusCodes.Status403Forbidden);
}
