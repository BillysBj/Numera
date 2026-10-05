using Numera.Platform.Money;

namespace Numera.Modules.Ledger;

public sealed record AfaResult(decimal Betrag, decimal Restbuchwert);

/// <summary>Pure calendar-year AfA calculation, including the in-service and disposal months.</summary>
public sealed class AfaCalculator
{
    public AfaResult Compute(FixedAsset asset, int jahr, IEnumerable<AfaBuchung> booked)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(booked);
        ArgumentOutOfRangeException.ThrowIfLessThan(jahr, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jahr, 9999);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(asset.NutzungsdauerJahre);
        ArgumentOutOfRangeException.ThrowIfNegative(asset.Anschaffungswert);
        if (!Enum.IsDefined(asset.Methode))
        {
            throw new ArgumentOutOfRangeException(nameof(asset), "Unsupported depreciation method.");
        }

        var entries = booked.Where(entry => entry.FixedAssetId == asset.Id && entry.TenantId == asset.TenantId).ToList();
        var remaining = Math.Max(0m, asset.Anschaffungswert - entries.Sum(entry => entry.Betrag));
        var start = asset.InbetriebnahmeDatum;
        if (entries.Any(entry => entry.Jahr == jahr) || jahr < start.Year
            || asset.AbgangsDatum is { } disposal && disposal.Year < jahr)
        {
            return new(0m, RoundingPolicy.RoundAmount(remaining));
        }

        decimal amount;
        if (asset.Methode == AfaMethode.GwgSofort)
        {
            amount = jahr == start.Year ? remaining : 0m;
        }
        else
        {
            var firstMonth = jahr == start.Year ? start.Month : 1;
            var lastMonth = asset.AbgangsDatum is { } end && end.Year == jahr ? end.Month : 12;
            // Month arithmetic avoids DateOnly overflow for long useful lives.
            var finalMonth = (long)start.Year * 12 + start.Month - 1 + (long)asset.NutzungsdauerJahre * 12 - 1;
            var finalYear = finalMonth / 12;
            if (jahr > finalYear)
            {
                amount = 0m;
            }
            else if (jahr == finalYear && (long)jahr * 12 + lastMonth - 1 >= finalMonth)
            {
                amount = remaining;
            }
            else
            {
                amount = asset.Anschaffungswert / asset.NutzungsdauerJahre
                    * Math.Max(0, lastMonth - firstMonth + 1) / 12m;
            }
        }

        // Cap AFTER rounding too: four-decimal acquisition values must never be over-depreciated.
        // Preserve the exact final remainder at the ledger's numeric(19,4) precision.
        amount = amount == remaining ? remaining : Math.Min(remaining, RoundingPolicy.RoundAmount(amount));

        // TODO(Steuerberater): asset removal / residual-book-value write-off on disposal is out of scope.
        return new(amount, RoundingPolicy.RoundAmount(Math.Max(0m, remaining - amount)));
    }
}
