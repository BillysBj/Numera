using System.Data;
using System.Text.Json;

using Hangfire;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Numera.Api.Contracts;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Tenant dunning configuration endpoints.</summary>
public static class DunningEndpoints
{
    /// <summary>Maps GET/PUT <c>/api/dunning/config</c>.</summary>
    public static IEndpointRouteBuilder MapDunningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dunning").RequireAuthorization();

        group.MapGet("/config", async (
            DunningConfigService service,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var levels = await service.GetConfigAsync(ct).ConfigureAwait(false);
            return Results.Ok(new DunningConfigResponse(levels));
        });

        group.MapPut("/config", async (
            UpdateDunningConfigRequest request,
            DunningConfigService service,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var result = await service.UpsertConfigAsync(request.Levels, ct).ConfigureAwait(false);
            return result.IsValid
                ? Results.Ok(new DunningConfigResponse(result.Levels!))
                : Results.ValidationProblem(result.Errors);
        });

        group.MapPost("/run", RunAsync);
        group.MapGet("/notices", async (
            NumeraDbContext db, IEntitlementService entitlements, CancellationToken ct,
            int page = 1, int pageSize = 25) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            page = Math.Clamp(page, 1, 1_000_000);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = from notice in db.Set<DunningNotice>().AsNoTracking()
                        join document in db.Set<SalesDocument>().AsNoTracking()
                            on notice.DocumentId equals document.Id
                        select new { Notice = notice, Document = document };
            var total = await query.CountAsync(ct).ConfigureAwait(false);
            // Project only list fields: never load the stored PDF blobs into a list response.
            var rows = await query.OrderByDescending(x => x.Notice.CreatedAt)
                .ThenByDescending(x => x.Notice.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new
                {
                    x.Notice.Id, x.Document.DocumentNumber, x.Document.RecipientSnapshot,
                    x.Notice.Level, x.Notice.IssuedOn, x.Notice.Fee, x.Notice.TotalToPay,
                    x.Document.Currency, x.Notice.Status,
                }).ToListAsync(ct).ConfigureAwait(false);
            var items = rows.Select(x => new DunningNoticeListItem(
                x.Id, x.DocumentNumber ?? x.Id.ToString(),
                SnapshotReader.FromDocument(new SalesDocument
                {
                    RecipientSnapshot = x.RecipientSnapshot,
                }).Recipient.Name,
                x.Level, x.IssuedOn, x.Fee, x.TotalToPay, x.Currency, x.Status)).ToList();
            return Results.Ok(new DunningNoticePage(items, total));
        });
        group.MapGet("/notices/{id:guid}/pdf", async (
            Guid id, DunningNoticePdfService pdf, IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            var bytes = await pdf.GetOrRenderAsync(id, "de", ct).ConfigureAwait(false);
            return bytes is null ? Results.NotFound()
                : Results.File(bytes, "application/pdf", $"Mahnung-{id}.pdf");
        });
        return app;
    }

    internal static async Task<IResult> RunAsync(
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IEntitlementService entitlements,
        IAuditWriter audit,
        IBackgroundJobClient jobs,
        CancellationToken ct)
    {
        if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
        {
            return UpgradeRequired();
        }

        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to run dunning.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var openItems = await db.Set<OpenItem>()
            .Where(x => x.DueDate < today &&
                        (x.Status == OpenItemStatus.Open || x.Status == OpenItemStatus.PartiallyPaid))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var configs = await db.Set<DunningLevelConfig>()
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var states = await ReadStatesAsync(db, openItems.Select(x => x.Id), ct).ConfigureAwait(false);
        var candidates = DunningService.SelectCandidates(openItems, configs, states, today);
        var notices = new List<DunningNotice>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var amounts = DunningService.CalculateAmounts(
                candidate.OpenItem.OpenAmount,
                candidate.NextLevel,
                candidate.DaysOverdue);
            var notice = new DunningNotice
            {
                TenantId = tenantId,
                OpenItemId = candidate.OpenItem.Id,
                DocumentId = candidate.OpenItem.DocumentId,
                Level = candidate.NextLevel.Level,
                IssuedOn = today,
                NewDueDate = DunningService.CalculateNewDueDate(today),
                OverdueAmount = candidate.OpenItem.OpenAmount,
                Fee = amounts.Fee,
                Interest = amounts.Interest,
                InterestRatePercent = candidate.NextLevel.InterestRatePercent,
                TotalToPay = amounts.TotalToPay,
                Status = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            // Freeze the configured letter now, before it can change ahead of email delivery.
            notice.RenderedPdf = await new DunningNoticePdfService(db)
                .RenderAsync(notice, "de", ct).ConfigureAwait(false);
            db.Add(notice);
            notices.Add(notice);

            await audit.RecordAsync(
                new DunningNoticeAuditEvent(
                    "dunning.notice.created",
                    notice.Id,
                    null,
                    JsonSerializer.Serialize(new
                    {
                        notice.OpenItemId,
                        notice.Level,
                        notice.OverdueAmount,
                        notice.Fee,
                        notice.Interest,
                        notice.TotalToPay,
                    })),
                ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            await AdvanceStateAsync(
                db,
                candidate.OpenItem.Id,
                candidate.NextLevel.Level,
                today,
                ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);

        foreach (var notice in notices)
        {
            jobs.Enqueue<SendDunningNoticeJob>(
                job => job.RunAsync(tenantId, notice.Id, "de", CancellationToken.None));
        }

        return Results.Ok(new DunningRunResponse(notices.Count, openItems.Count - notices.Count));
    }

    private static async Task<Dictionary<Guid, DunningService.OpenItemDunningState>> ReadStatesAsync(
        NumeraDbContext db,
        IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, DunningService.OpenItemDunningState>();
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return result;
        }

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText =
            "SELECT id, current_dunning_level, last_dunned_on FROM open_items WHERE id = ANY(@ids)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "ids";
        parameter.Value = idList.ToArray();
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[reader.GetGuid(0)] = new DunningService.OpenItemDunningState(
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2));
        }

        return result;
    }

    private static async Task AdvanceStateAsync(
        NumeraDbContext db,
        Guid openItemId,
        int level,
        DateOnly today,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText =
            "UPDATE open_items SET current_dunning_level = @level, last_dunned_on = @today WHERE id = @id";
        AddParameter(command, "level", level);
        AddParameter(command, "today", today);
        AddParameter(command, "id", openItemId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    // The server-authoritative Dunning (plan L+) gate predicate — the single testable seam the
    // config + run handlers share (mirrors RecurringInvoiceEndpoints.HasCapabilityAsync).
    internal static Task<bool> HasCapabilityAsync(IEntitlementService entitlements, CancellationToken ct)
        => entitlements.HasCapabilityAsync(Capability.Dunning, ct);

    // The server-authoritative Dunning (plan L+) gate result: a 403 upgrade hint (locked 09-CONTEXT §3).
    internal static IResult UpgradeRequired()
        => Results.Problem(
            title: "Upgrade required",
            detail: "Mahnwesen (dunning) requires the Dunning capability (plan L or XL).",
            statusCode: StatusCodes.Status403Forbidden);
}

internal sealed record DunningNoticeAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(DunningNotice);
}
