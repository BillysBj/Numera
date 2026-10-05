using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

public static class FixedAssetEndpoints
{
    public static IEndpointRouteBuilder MapFixedAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fixed-assets").RequireAuthorization();
        group.MapGet("/", async (NumeraDbContext db, CancellationToken ct) => Results.Ok(
            await db.Set<FixedAsset>().AsNoTracking().OrderBy(asset => asset.Bezeichnung).ThenBy(asset => asset.Id)
                .ToListAsync(ct).ConfigureAwait(false)));
        group.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var asset = await db.Set<FixedAsset>().AsNoTracking().SingleOrDefaultAsync(asset => asset.Id == id, ct)
                .ConfigureAwait(false);
            return asset is null ? Results.NotFound() : Results.Ok(asset);
        });
        group.MapPost("/", CreateAsync).RequireAuthorization("RequireOwner");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("RequireOwner");
        group.MapPost("/{jahr:int}/afa-run", RunAsync).RequireAuthorization("RequireOwner");
        group.MapGet("/anlagenspiegel", async (int jahr, AnlagenspiegelCalculator calculator, CancellationToken ct) =>
            jahr is < 1 or > 9999 ? InvalidYear()
                : Results.Ok(await calculator.ComputeAsync(jahr, ct).ConfigureAwait(false)));
        return app;
    }

    internal static Task<IResult> CreateAsync(FixedAssetRequest request, NumeraDbContext db,
        IAuditWriter audit, ICurrentTenant currentTenant, CancellationToken ct) =>
        SaveAsync(null, request, db, audit, currentTenant, ct);

    internal static Task<IResult> UpdateAsync(Guid id, FixedAssetRequest request, NumeraDbContext db,
        IAuditWriter audit, ICurrentTenant currentTenant, CancellationToken ct) =>
        SaveAsync(id, request, db, audit, currentTenant, ct);

    private static async Task<IResult> SaveAsync(Guid? id, FixedAssetRequest request, NumeraDbContext db,
        IAuditWriter audit, ICurrentTenant currentTenant, CancellationToken ct)
    {
        var tenant = currentTenant.TenantId ?? throw new InvalidOperationException("A tenant is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAssetsAsync(db, tenant, ct).ConfigureAwait(false);
        var existing = id is { } assetId
            ? await db.Set<FixedAsset>().SingleOrDefaultAsync(asset => asset.Id == assetId, ct).ConfigureAwait(false)
            : null;
        if (id is not null && existing is null)
        {
            return Results.NotFound();
        }

        var chart = await db.Set<LedgerSettings>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (chart is null)
        {
            return Results.Conflict(new { message = "The ledger must be set up first." });
        }

        var errors = Validate(request);
        var expenseNumber = string.IsNullOrWhiteSpace(request.AbschreibungskontoNumber)
            ? SkrMapping.DepreciationExpenseAccount(chart.ChartVariant) : request.AbschreibungskontoNumber.Trim();
        var assetNumber = request.AnlagekontoNumber?.Trim() ?? string.Empty;
        var accounts = await db.Set<Account>().AsNoTracking()
            .Where(account => account.ChartVariant == chart.ChartVariant && account.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!accounts.Any(account => account.Number == assetNumber && account.Type == AccountType.Asset))
        {
            errors["anlagekontoNumber"] = ["An existing active Asset account in the tenant chart is required."];
        }

        if (!accounts.Any(account => account.Number == expenseNumber && account.Type == AccountType.Expense))
        {
            errors["abschreibungskontoNumber"] = ["An existing active Expense account in the tenant chart is required."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (existing is not null)
        {
            var bookings = await db.Set<AfaBuchung>().Where(entry => entry.FixedAssetId == existing.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            if (bookings.Count > 0 && (existing.AnschaffungsDatum != request.AnschaffungsDatum
                || existing.InbetriebnahmeDatum != request.InbetriebnahmeDatum
                || existing.AnschaffungskostenNetto != request.AnschaffungskostenNetto
                || existing.AnschaffungsnebenkostenNetto != request.AnschaffungsnebenkostenNetto
                || existing.NutzungsdauerJahre != request.NutzungsdauerJahre || existing.Methode != request.Methode
                || existing.AnlagekontoNumber != assetNumber || existing.AbschreibungskontoNumber != expenseNumber
                || existing.AbgangsDatum != request.AbgangsDatum
                    && (existing.AbgangsDatum is { } oldDate && oldDate.Year <= bookings.Max(entry => entry.Jahr)
                        || request.AbgangsDatum is { } newDate && newDate.Year <= bookings.Max(entry => entry.Jahr))))
            {
                return Results.Conflict(new { message = "Booked depreciation inputs cannot be changed retroactively." });
            }
        }

        var before = existing is null ? null : JsonSerializer.Serialize(existing);
        var asset = existing ?? new FixedAsset
        {
            TenantId = tenant, Bezeichnung = request.Bezeichnung.Trim(),
            AnlagekontoNumber = assetNumber, AbschreibungskontoNumber = expenseNumber,
        };
        asset.Bezeichnung = request.Bezeichnung.Trim();
        asset.Lieferant = request.Lieferant?.Trim();
        asset.BelegRef = request.BelegRef?.Trim();
        asset.Rechnungsdatum = request.Rechnungsdatum;
        asset.AnschaffungsDatum = request.AnschaffungsDatum;
        asset.InbetriebnahmeDatum = request.InbetriebnahmeDatum;
        asset.AnschaffungskostenNetto = request.AnschaffungskostenNetto;
        asset.AnschaffungsnebenkostenNetto = request.AnschaffungsnebenkostenNetto;
        asset.AnlagekontoNumber = assetNumber;
        asset.AbschreibungskontoNumber = expenseNumber;
        asset.NutzungsdauerJahre = request.NutzungsdauerJahre;
        asset.Methode = request.Methode;
        asset.AbgangsDatum = request.AbgangsDatum;
        asset.AbgangsArt = request.AbgangsArt;
        if (existing is null)
        {
            db.Add(asset);
        }

        await audit.RecordAsync(new FixedAssetAuditEvent(existing is null ? "fixed_asset.created" : "fixed_asset.updated",
            asset.Id, before, JsonSerializer.Serialize(asset)), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return existing is null ? Results.Created($"/api/fixed-assets/{asset.Id}", asset) : Results.Ok(asset);
    }

    internal static async Task<IResult> RunAsync(int jahr, NumeraDbContext db, AfaCalculator calculator,
        PostingEngine engine, IAuditWriter audit, ICurrentTenant currentTenant, CancellationToken ct)
    {
        if (jahr is < 1 or > 9999)
        {
            return InvalidYear();
        }

        var tenant = currentTenant.TenantId ?? throw new InvalidOperationException("A tenant is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Serialize all years and master-data edits, then share Festschreibung's period lock.
        await LockAssetsAsync(db, tenant, ct).ConfigureAwait(false);
        var periodLock = $"ledger-journal:{tenant:D}:{jahr}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({periodLock}, 0))", ct).ConfigureAwait(false);
        var settings = await db.Set<LedgerSettings>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (settings is null)
        {
            return Results.Conflict(new { message = "The ledger must be set up first." });
        }

        var assets = await db.Set<FixedAsset>().AsNoTracking().OrderBy(asset => asset.Id).ToListAsync(ct).ConfigureAwait(false);
        var booked = await db.Set<AfaBuchung>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var byAsset = booked.ToLookup(entry => entry.FixedAssetId);
        var accounts = await db.Set<Account>().AsNoTracking()
            .Where(account => account.ChartVariant == settings.ChartVariant && account.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);
        var periods = await db.Set<FiscalPeriod>().Where(period => period.Year == jahr).ToListAsync(ct).ConfigureAwait(false);
        var count = 0;
        var skipped = 0;
        var total = 0m;
        foreach (var asset in assets)
        {
            var history = byAsset[asset.Id].ToList();
            // Do not rewrite history after a later year's AfA has already been posted.
            var result = calculator.Compute(asset, jahr, history);
            if (result.Betrag <= 0m || history.Any(entry => entry.Jahr >= jahr))
            {
                skipped++;
                continue;
            }

            var entryDate = asset.AbgangsDatum is { } date && date.Year == jahr ? date : new DateOnly(jahr, 12, 31);
            var period = periods.SingleOrDefault(candidate => candidate.Month == entryDate.Month);
            var assetAccount = accounts.SingleOrDefault(account => account.Number == asset.AnlagekontoNumber && account.Type == AccountType.Asset);
            var expenseAccount = accounts.SingleOrDefault(account => account.Number == asset.AbschreibungskontoNumber && account.Type == AccountType.Expense);
            if (period is { Status: FiscalPeriodStatus.Locked } || assetAccount is null || expenseAccount is null)
            {
                skipped++;
                continue;
            }

            if (period is null)
            {
                period = new FiscalPeriod { TenantId = tenant, Year = jahr, Month = entryDate.Month };
                db.Add(period);
                periods.Add(period);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            var entry = await engine.PostAsync(new AfaPostingSource(tenant, expenseAccount.Id, assetAccount.Id, result.Betrag),
                new JournalEntry
                {
                    TenantId = tenant, EntryDate = entryDate, PeriodId = period.Id,
                    SourceRef = asset.Id.ToString(), SourceType = LedgerSourceType.Manual,
                    Description = $"AfA {jahr} – {asset.Bezeichnung}", PostingType = PostingType.Normal,
                }, ct).ConfigureAwait(false);
            db.Add(new AfaBuchung
            {
                TenantId = tenant, FixedAssetId = asset.Id, Jahr = jahr, Betrag = result.Betrag, JournalEntryId = entry.Id,
            });
            count++;
            total += result.Betrag;
        }

        var summary = new AfaRunSummary(count, skipped, RoundingPolicy.RoundAmount(total));
        await audit.RecordAsync(new FixedAssetAuditEvent("fixed_asset.afa_run", null, null,
            JsonSerializer.Serialize(new { Jahr = jahr, Summary = summary })), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Results.Ok(summary);
    }

    private static Task<int> LockAssetsAsync(NumeraDbContext db, Guid tenant, CancellationToken ct)
    {
        var key = $"fixed-assets:{tenant:D}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    private static IResult InvalidYear() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["jahr"] = ["Year must be between 1 and 9999."] });

    private static Dictionary<string, string[]> Validate(FixedAssetRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Bezeichnung))
        {
            errors["bezeichnung"] = ["A description is required."];
        }

        if (request.NutzungsdauerJahre <= 0)
        {
            errors["nutzungsdauerJahre"] = ["Useful life must be positive."];
        }

        if (request.AnschaffungsDatum == default || request.InbetriebnahmeDatum < request.AnschaffungsDatum)
        {
            errors["inbetriebnahmeDatum"] = ["In-service date must be on or after a valid acquisition date."];
        }

        const decimal maximum = 999999999999999.9999m;
        if (request.AnschaffungskostenNetto is < 0m or > maximum
            || request.AnschaffungsnebenkostenNetto is < 0m or > maximum
            || request.AnschaffungskostenNetto + request.AnschaffungsnebenkostenNetto > maximum
            || decimal.Round(request.AnschaffungskostenNetto, 4) != request.AnschaffungskostenNetto
            || decimal.Round(request.AnschaffungsnebenkostenNetto, 4) != request.AnschaffungsnebenkostenNetto)
        {
            errors["anschaffungskostenNetto"] = ["Costs must be non-negative numeric(19,4) amounts within the combined limit."];
        }

        if (!Enum.IsDefined(request.Methode) || !Enum.IsDefined(request.AbgangsArt))
        {
            errors["methode"] = ["Unsupported depreciation method or disposal type."];
        }

        if (request.AbgangsDatum < request.InbetriebnahmeDatum
            || (request.AbgangsDatum is null) != (request.AbgangsArt == AssetDisposal.None))
        {
            errors["abgangsDatum"] = ["Disposal requires a type and a date on or after the in-service date."];
        }

        return errors;
    }
}

internal sealed record FixedAssetAuditEvent(string Action, Guid? EntityId, string? Before, string? After) : IAuditEvent
{
    public string EntityType => nameof(FixedAsset);
}
