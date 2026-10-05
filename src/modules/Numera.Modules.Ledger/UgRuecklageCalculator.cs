using Numera.Platform.Money;

namespace Numera.Modules.Ledger;

/// <summary>§5a GmbHG allocation within equity; never an expense or a deduction from taxable profit.</summary>
public sealed class UgRuecklageCalculator
{
    public UgRuecklageResult Compute(decimal jahresueberschuss, decimal verlustvortragVorjahr, bool pflichtAktiv)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(verlustvortragVorjahr);
        var massgeblicherBetrag = Math.Max(0m, jahresueberschuss - verlustvortragVorjahr);
        return new(massgeblicherBetrag, pflichtAktiv ? RoundingPolicy.RoundAmount(0.25m * massgeblicherBetrag) : 0m);
    }
}

public sealed record UgRuecklageResult(decimal MassgeblicherBetrag, decimal Ruecklage);
