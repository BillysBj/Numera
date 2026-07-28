using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Modules.Crm;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>CRUD and due-date queries for tasks attached to partners.</summary>
public static class PartnerTaskEndpoints
{
    /// <summary>Maps <c>/api/partners/{partnerId}/tasks</c>.</summary>
    public static IEndpointRouteBuilder MapPartnerTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/partners/{partnerId:guid}/tasks").RequireAuthorization();

        g.MapGet("/", async (
            Guid partnerId,
            NumeraDbContext db,
            CancellationToken ct,
            bool openOnly = false,
            bool overdueOnly = false) =>
        {
            if (!await PartnerExistsAsync(db, partnerId, ct).ConfigureAwait(false))
            {
                return Results.NotFound();
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = db.Set<PartnerTask>()
                .AsNoTracking()
                .Where(t => t.PartnerId == partnerId);

            if (openOnly)
            {
                query = query.Where(t => t.Status == PartnerTaskStatus.Open);
            }

            if (overdueOnly)
            {
                query = query.Where(t =>
                    t.DueDate != null &&
                    t.DueDate < today &&
                    t.Status == PartnerTaskStatus.Open);
            }

            var tasks = await query
                .OrderBy(t => t.DueDate)
                .ThenBy(t => t.Id)
                .Select(t => new PartnerTaskListItem(
                    t.Id,
                    t.Title,
                    t.Description,
                    t.DueDate,
                    t.Status,
                    t.AssignedUserId,
                    t.DueDate != null &&
                    t.DueDate < today &&
                    t.Status == PartnerTaskStatus.Open))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(tasks);
        });

        g.MapPost("/", async (
            Guid partnerId,
            CreatePartnerTaskRequest request,
            IValidator<CreatePartnerTaskRequest> validator,
            NumeraDbContext db,
            ICurrentTenant tenant,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            if (!await PartnerExistsAsync(db, partnerId, ct).ConfigureAwait(false))
            {
                return Results.NotFound();
            }

            var task = new PartnerTask
            {
                TenantId = tenant.TenantId!.Value,
                PartnerId = partnerId,
                Title = request.Title,
                Description = request.Description,
                DueDate = request.DueDate,
                AssignedUserId = request.AssignedUserId,
            };
            db.Add(task);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created(
                $"/api/partners/{partnerId}/tasks/{task.Id}",
                ToDetail(task));
        });

        g.MapPut("/{taskId:guid}", async (
            Guid partnerId,
            Guid taskId,
            UpdatePartnerTaskRequest request,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Title)] = ["Title is required and must not exceed 200 characters."],
                });
            }

            var task = await db.Set<PartnerTask>()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.PartnerId == partnerId, ct)
                .ConfigureAwait(false);
            if (task is null)
            {
                return Results.NotFound();
            }

            task.Title = request.Title;
            task.Description = request.Description;
            task.DueDate = request.DueDate;
            task.AssignedUserId = request.AssignedUserId;
            if (task.Status != request.Status)
            {
                task.CompletedAt = request.Status == PartnerTaskStatus.Done
                    ? DateTimeOffset.UtcNow
                    : null;
            }

            task.Status = request.Status;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        g.MapPost("/{taskId:guid}/complete", async (
            Guid partnerId,
            Guid taskId,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var task = await db.Set<PartnerTask>()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.PartnerId == partnerId, ct)
                .ConfigureAwait(false);
            if (task is null)
            {
                return Results.NotFound();
            }

            task.Status = PartnerTaskStatus.Done;
            task.CompletedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        g.MapDelete("/{taskId:guid}", async (
            Guid partnerId,
            Guid taskId,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var task = await db.Set<PartnerTask>()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.PartnerId == partnerId, ct)
                .ConfigureAwait(false);
            if (task is null)
            {
                return Results.NotFound();
            }

            db.Remove(task);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        return app;
    }

    private static Task<bool> PartnerExistsAsync(
        NumeraDbContext db,
        Guid partnerId,
        CancellationToken ct) =>
        db.Set<BusinessPartner>()
            .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
            .AnyAsync(p => p.Id == partnerId, ct);

    private static PartnerTaskDetail ToDetail(PartnerTask task) => new(
        task.Id,
        task.PartnerId,
        task.Title,
        task.Description,
        task.DueDate,
        task.Status,
        task.AssignedUserId,
        task.CreatedAt,
        task.CompletedAt);
}
