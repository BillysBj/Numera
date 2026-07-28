using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Append-only Kundenakte upload, metadata listing, and download endpoints.</summary>
public static class CustomerFileEndpoints
{
    private const long MaxFileBytes = 20 * 1024 * 1024;

    private static readonly string[] AllowedFileTypes =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
    ];

    /// <summary>Maps <c>/api/partners/{partnerId}/files</c> without edit/delete routes.</summary>
    public static IEndpointRouteBuilder MapCustomerFileEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/partners/{partnerId:guid}/files").RequireAuthorization();

        g.MapPost("/", async (
            Guid partnerId,
            IFormFile file,
            NumeraDbContext db,
            ICurrentTenant tenant,
            ICurrentUser user,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            if (file.Length <= 0 || file.Length > MaxFileBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [$"The file must be between 1 byte and {MaxFileBytes} bytes."],
                });
            }

            var contentType = file.ContentType?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(contentType) ||
                Array.IndexOf(AllowedFileTypes, contentType) < 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["The file type is not supported."],
                });
            }

            if (!await PartnerExistsAsync(db, partnerId, ct).ConfigureAwait(false))
            {
                return Results.NotFound();
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);
            var bytes = stream.ToArray();
            var customerFile = new CustomerFile
            {
                TenantId = tenant.TenantId!.Value,
                PartnerId = partnerId,
                Bytes = bytes,
                FileName = file.FileName,
                ContentType = contentType,
                ByteSize = bytes.Length,
                UploadedByUserId = user.UserId,
            };

            db.Add(customerFile);
            await audit.RecordAsync(new CustomerFileUploadedAuditEvent(
                customerFile.Id,
                JsonSerializer.Serialize(new
                {
                    customerFile.PartnerId,
                    customerFile.FileName,
                    customerFile.ContentType,
                    customerFile.ByteSize,
                })), ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created(
                $"/api/partners/{partnerId}/files/{customerFile.Id}",
                new { customerFile.Id, customerFile.FileName, customerFile.ByteSize });
        }).DisableAntiforgery();

        g.MapGet("/", async (Guid partnerId, NumeraDbContext db, CancellationToken ct) =>
        {
            if (!await PartnerExistsAsync(db, partnerId, ct).ConfigureAwait(false))
            {
                return Results.NotFound();
            }

            var files = await db.Set<CustomerFile>()
                .AsNoTracking()
                .Where(f => f.PartnerId == partnerId)
                .OrderByDescending(f => f.UploadedAt)
                .Select(f => new
                {
                    f.Id,
                    f.FileName,
                    f.ContentType,
                    f.ByteSize,
                    f.UploadedByUserId,
                    f.UploadedAt,
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(files);
        });

        g.MapGet("/{fileId:guid}", async (
            Guid partnerId,
            Guid fileId,
            NumeraDbContext db,
            CancellationToken ct) =>
        {
            var file = await db.Set<CustomerFile>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    f => f.Id == fileId && f.PartnerId == partnerId,
                    ct)
                .ConfigureAwait(false);

            return file is null
                ? Results.NotFound()
                : Results.File(file.Bytes, file.ContentType, file.FileName);
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
}

internal sealed record CustomerFileUploadedAuditEvent(Guid? EntityId, string? After) : IAuditEvent
{
    public string Action => "customer_file.uploaded";
    public string EntityType => nameof(CustomerFile);
    public string? Before => null;
}
