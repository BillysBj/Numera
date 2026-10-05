using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>One-time chart-of-accounts setup and owner-controlled ledger settings.</summary>
public static class LedgerSetupEndpoints
{
    /// <summary>Maps owner-gated setup/settings writes and authenticated reads under <c>/api/ledger</c>.</summary>
    public static IEndpointRouteBuilder MapLedgerSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger").RequireAuthorization();

        group.MapPost("/setup", SetupAsync)
            .RequireAuthorization("RequireOwner");
        group.MapPut("/settings", UpdateSettingsAsync)
            .RequireAuthorization("RequireOwner");

        group.MapGet("/settings", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var settings = await db.Set<LedgerSettings>()
                .AsNoTracking()
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);

            return settings is null
                ? Results.NotFound()
                : Results.Ok(ToDto(settings));
        });

        return app;
    }

    internal static async Task<IResult> UpdateSettingsAsync(LedgerSettingsUpdateRequest request,
        NumeraDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var settings = await db.Set<LedgerSettings>().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (settings is null)
        {
            return Results.NotFound();
        }

        var before = JsonSerializer.Serialize(ToDto(settings));
        settings.UgRuecklagepflichtAktiv = request.UgRuecklagepflichtAktiv;
        await audit.RecordAsync(new LedgerSetupAuditEvent("ledger.settings_updated", settings.Id, before,
            JsonSerializer.Serialize(ToDto(settings))), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(settings));
    }

    internal static async Task<IResult> SetupAsync(
        LedgerSetupRequest request,
        NumeraDbContext db,
        ChartSeeder chartSeeder,
        IAuditWriter audit,
        ICurrentTenant currentTenant,
        CancellationToken ct)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to set up the ledger.");

        if (await db.Set<LedgerSettings>().AnyAsync(ct).ConfigureAwait(false))
        {
            return Results.Conflict(new { message = "The ledger is already set up for this tenant." });
        }

        var settings = new LedgerSettings
        {
            TenantId = tenantId,
            ChartVariant = request.ChartVariant,
            Besteuerungsart = request.Besteuerungsart,
            Gewinnermittlungsart = request.Gewinnermittlungsart,
            FiscalYearStartMonth = request.FiscalYearStartMonth ?? 1,
            UgRuecklagepflichtAktiv = request.UgRuecklagepflichtAktiv,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Add(settings);
        await audit.RecordAsync(
            new LedgerSetupAuditEvent(
                "ledger.setup",
                settings.Id,
                null,
                JsonSerializer.Serialize(ToDto(settings))),
            ct).ConfigureAwait(false);

        try
        {
            if (!await chartSeeder.SeedAsync(request.ChartVariant, tenantId, ct).ConfigureAwait(false))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return Results.Conflict(new { message = "A chart of accounts already exists for this tenant." });
            }

            var accountCount = await db.Set<Account>().CountAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);

            return Results.Created(
                "/api/ledger/settings",
                new LedgerSetupResponse(ToDto(settings), accountCount));
        }
        catch (DbUpdateException ex) when (IsConcurrentSetup(ex))
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return Results.Conflict(new { message = "The ledger is already set up for this tenant." });
        }
    }

    private static Dictionary<string, string[]> Validate(LedgerSetupRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.ChartVariant))
        {
            errors["chartVariant"] = ["Chart variant must be Skr03 or Skr04."];
        }

        if (!Enum.IsDefined(request.Besteuerungsart))
        {
            errors["besteuerungsart"] = ["Besteuerungsart must be Soll or Ist."];
        }

        if (!Enum.IsDefined(request.Gewinnermittlungsart))
        {
            errors["gewinnermittlungsart"] = ["Gewinnermittlungsart must be Euer or Bilanz."];
        }

        if (request.FiscalYearStartMonth is < 1 or > 12)
        {
            errors["fiscalYearStartMonth"] = ["Fiscal year start month must be between 1 and 12."];
        }

        return errors;
    }

    private static bool IsConcurrentSetup(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_ledger_settings_tenant_id",
        };

    private static LedgerSettingsDto ToDto(LedgerSettings settings) => new(
        settings.Id,
        settings.ChartVariant,
        settings.Besteuerungsart,
        settings.Gewinnermittlungsart,
        settings.FiscalYearStartMonth,
        settings.UgRuecklagepflichtAktiv);
}

internal sealed record LedgerSetupAuditEvent(
    string Action,
    Guid? EntityId,
    string? Before,
    string? After) : IAuditEvent
{
    public string EntityType => nameof(LedgerSettings);
}
