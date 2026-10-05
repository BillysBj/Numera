using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Api.Reporting;

public sealed class AnlagenspiegelCalculator(NumeraDbContext db)
{
    public async Task<AnlagenspiegelReport> ComputeAsync(int jahr, CancellationToken ct)
    {
        var start = new DateOnly(jahr, 1, 1);
        var end = new DateOnly(jahr, 12, 31);
        var assets = await db.Set<FixedAsset>().AsNoTracking()
            .Where(asset => asset.AnschaffungsDatum <= end && (asset.AbgangsDatum == null || asset.AbgangsDatum >= start))
            .OrderBy(asset => asset.Bezeichnung).ThenBy(asset => asset.Id).ToListAsync(ct).ConfigureAwait(false);
        var bookings = await db.Set<AfaBuchung>().AsNoTracking()
            .Where(entry => entry.Jahr <= jahr).ToListAsync(ct).ConfigureAwait(false);
        var byAsset = bookings.ToLookup(entry => entry.FixedAssetId);
        var lines = assets.Select(asset =>
        {
            var prior = byAsset[asset.Id].Where(entry => entry.Jahr < jahr).Sum(entry => entry.Betrag);
            var opening = asset.AnschaffungsDatum < start ? Math.Max(0m, asset.Anschaffungswert - prior) : 0m;
            var additions = asset.AnschaffungsDatum >= start ? asset.Anschaffungswert : 0m;
            var afa = byAsset[asset.Id].Where(entry => entry.Jahr == jahr).Sum(entry => entry.Betrag);
            var remaining = Math.Max(0m, opening + additions - afa);
            // TODO(Steuerberater): disposal is a register movement only; no asset-removal/write-off journal booking yet.
            var disposals = asset.AbgangsDatum is { } date && date <= end ? remaining : 0m;
            return new AnlagenspiegelLine(asset.Bezeichnung,
                RoundingPolicy.RoundAmount(opening), RoundingPolicy.RoundAmount(additions),
                RoundingPolicy.RoundAmount(disposals), RoundingPolicy.RoundAmount(afa),
                RoundingPolicy.RoundAmount(remaining - disposals));
        }).ToList();
        return new(jahr, lines, new("Summe",
            RoundingPolicy.RoundAmount(lines.Sum(line => line.BuchwertJahresanfang)),
            RoundingPolicy.RoundAmount(lines.Sum(line => line.Zugaenge)),
            RoundingPolicy.RoundAmount(lines.Sum(line => line.Abgaenge)),
            RoundingPolicy.RoundAmount(lines.Sum(line => line.AfaJahr)),
            RoundingPolicy.RoundAmount(lines.Sum(line => line.BuchwertJahresende))));
    }
}
