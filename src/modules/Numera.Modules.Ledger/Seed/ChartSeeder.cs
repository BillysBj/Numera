using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Ledger.Seed;

/// <summary>Materializes an embedded minimal SKR chart for the ambient tenant.</summary>
public sealed class ChartSeeder(NumeraDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Seeds the selected chart with one save. Returns <see langword="false"/> when
    /// the ambient tenant already has accounts. The caller owns tenant/GUC setup.
    /// </summary>
    public async Task<bool> SeedAsync(
        ChartVariant variant,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (await db.Set<Account>().AnyAsync(ct).ConfigureAwait(false))
        {
            return false;
        }

        var resourceName = variant switch
        {
            ChartVariant.Skr03 => "Numera.Modules.Ledger.Seed.skr03.accounts.json",
            ChartVariant.Skr04 => "Numera.Modules.Ledger.Seed.skr04.accounts.json",
            _ => throw new ArgumentOutOfRangeException(
                nameof(variant), variant, "Only SKR03 and SKR04 are supported."),
        };

        await using var stream = typeof(ChartSeeder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded chart resource '{resourceName}' was not found.");
        var seedRows = await JsonSerializer.DeserializeAsync<List<SeedAccount>>(stream, JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Embedded chart resource '{resourceName}' is empty.");

        var accounts = seedRows.Select(row => new Account
        {
            TenantId = tenantId,
            Number = row.Number,
            Name = row.Name,
            Type = row.Type,
            ChartVariant = variant,
            IsAutomatikkonto = row.IsAutomatikkonto,
            Steuerschluessel = row.Steuerschluessel,
            UstvaKennziffer = row.UstvaKennziffer,
            IsActive = true,
        });

        db.Set<Account>().AddRange(accounts);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record SeedAccount(
        string Number,
        string Name,
        AccountType Type,
        bool IsAutomatikkonto,
        Steuerschluessel? Steuerschluessel,
        string? UstvaKennziffer);
}
