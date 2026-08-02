using System.Text;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;

namespace Numera.Api.Endpoints;

/// <summary>
/// DSGVO Art. 20 tenant-data export (PLAT-08): a single Owner-only endpoint that streams the
/// caller's ENTIRE tenant dataset as a ZIP of per-entity JSON + the real binary blobs.
/// </summary>
/// <remarks>
/// The group requires the <c>RequireOwner</c> policy (any non-Owner member → 403). A TaxAdvisor is
/// additionally default-denied by the global read-only write-guard, because <c>/api/export</c> is
/// deliberately NOT in <c>ReadOnlyAccessPolicy.ReadPrefixes</c>. The export itself is gated on
/// <see cref="Capability.DataExport"/> (plan S+) and is non-mutating: it appends exactly one
/// <c>data.exported</c> audit row (saved before streaming) and opens no write transaction.
/// </remarks>
public static class ExportEndpoints
{
    /// <summary>Maps <c>GET /api/export</c> — Owner-only, DataExport-gated, audited, streamed.</summary>
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/export").RequireAuthorization("RequireOwner");

        group.MapGet("/", async (
            HttpContext ctx,
            NumeraDbContext db,
            IEntitlementService entitlements,
            IAuditWriter audit,
            TenantExportService exportService,
            CancellationToken ct) =>
        {
            // Tarif gate (plan S+): a plan without DataExport gets a 403 upgrade hint.
            if (!await HasCapabilityAsync(entitlements, ct).ConfigureAwait(false))
            {
                return UpgradeRequired();
            }

            // Read the tenant (RLS-scoped) for the download filename.
            var meta = await db.Tenants.AsNoTracking()
                .Select(t => new { t.Name })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            // Append exactly one data.exported audit row and persist it BEFORE streaming — the
            // export is otherwise non-mutating (no business rows change).
            await audit.RecordAsync(new ExportAuditEvent(), ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var fileName = $"numera-export-{Slug(meta?.Name)}-{DateTime.UtcNow:yyyyMMdd}.zip";
            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            // Stream the archive straight onto the response body (bounded memory; nothing buffered).
            await exportService.WriteArchiveAsync(ctx.Response.Body, ct).ConfigureAwait(false);
            return Results.Empty;
        });

        return app;
    }

    // The DataExport (plan S+) gate predicate — the testable seam the handler shares.
    internal static Task<bool> HasCapabilityAsync(IEntitlementService entitlements, CancellationToken ct)
        => entitlements.HasCapabilityAsync(Capability.DataExport, ct);

    // The DataExport (plan S+) gate result: a 403 upgrade hint (mirrors the other endpoint gates).
    internal static IResult UpgradeRequired()
        => Results.Problem(
            title: "Upgrade required",
            detail: "Der Datenexport (DSGVO Art. 20) erfordert die DataExport-Berechtigung (Tarif S oder höher).",
            statusCode: StatusCodes.Status403Forbidden);

    // Reduce the tenant name to a short, filename-safe slug (ASCII letters/digits/dash).
    private static string Slug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "tenant";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if ((c == ' ' || c == '-' || c == '_') && sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "tenant" : slug;
    }
}

/// <summary>The single audit row appended by a tenant-data export (who/when; no business change).</summary>
internal sealed record ExportAuditEvent : IAuditEvent
{
    public string Action => "data.exported";

    public string EntityType => "TenantExport";

    public Guid? EntityId => null;

    public string? Before => null;

    public string? After => null;
}
