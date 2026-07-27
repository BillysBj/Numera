using Numera.Platform.Money;

namespace Numera.Modules.Sales.Dunning;

/// <summary>Selects receivables eligible for the next dunning level and calculates ancillary claims.</summary>
public static class DunningService
{
    /// <summary>The persisted dunning state read from the 06-03 shadow columns.</summary>
    public readonly record struct OpenItemDunningState(int CurrentLevel, DateOnly? LastDunnedOn);

    /// <summary>An eligible open item together with its next configured level.</summary>
    public sealed record Candidate(
        OpenItem OpenItem,
        DunningLevelConfig NextLevel,
        int DaysOverdue);

    /// <summary>Fee, default interest and resulting amount stated on the notice.</summary>
    public readonly record struct Amounts(decimal Fee, decimal Interest, decimal TotalToPay);

    /// <summary>
    /// Selects overdue Open/PartiallyPaid items whose next configured threshold has been reached
    /// and which have not already been dunned today.
    /// </summary>
    public static IReadOnlyList<Candidate> SelectCandidates(
        IEnumerable<OpenItem> openItems,
        IEnumerable<DunningLevelConfig> configs,
        IReadOnlyDictionary<Guid, OpenItemDunningState> states,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(openItems);
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(states);

        var levels = configs.ToDictionary(x => x.Level);
        var result = new List<Candidate>();
        foreach (var item in openItems)
        {
            if (item.DueDate >= today ||
                item.Status is not (OpenItemStatus.Open or OpenItemStatus.PartiallyPaid) ||
                !states.TryGetValue(item.Id, out var state) ||
                state.LastDunnedOn == today ||
                !levels.TryGetValue(state.CurrentLevel + 1, out var next))
            {
                continue;
            }

            var daysOverdue = today.DayNumber - item.DueDate.DayNumber;
            if (daysOverdue >= next.DaysAfterDue)
            {
                result.Add(new Candidate(item, next, daysOverdue));
            }
        }

        return result;
    }

    /// <summary>Calculates fee and §288 interest without changing the receivable principal.</summary>
    public static Amounts CalculateAmounts(
        decimal openAmount,
        DunningLevelConfig config,
        int daysOverdue)
    {
        ArgumentNullException.ThrowIfNull(config);
        var interest = config.ChargeInterest
            ? RoundingPolicy.RoundTax(openAmount * daysOverdue / 365m, config.InterestRatePercent)
            : 0m;
        return new Amounts(config.Fee, interest, openAmount + config.Fee + interest);
    }

    /// <summary>Returns the default seven-day grace deadline.</summary>
    public static DateOnly CalculateNewDueDate(DateOnly today, int graceDays = 7) =>
        today.AddDays(graceDays);

}
