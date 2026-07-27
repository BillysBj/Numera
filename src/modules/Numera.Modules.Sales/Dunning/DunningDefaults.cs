namespace Numera.Modules.Sales.Dunning;

/// <summary>Sensible German defaults materialized for a tenant on first read.</summary>
public static class DunningDefaults
{
    /// <summary>Default annual §288 BGB B2B rate (1.27% base rate plus 9 percentage points).</summary>
    public const decimal InterestRatePercent = 10.27m;

    /// <summary>The four-level default ladder.</summary>
    public static IReadOnlyList<DunningLevelDefault> Levels { get; } =
    [
        new(0, "Zahlungserinnerung", 7, 0m, false,
            "Bitte begleichen Sie den offenen Betrag. Möglicherweise hat sich Ihre Zahlung mit diesem Schreiben überschnitten.",
            "Please settle the outstanding amount. Your payment may have crossed with this reminder."),
        new(1, "1. Mahnung", 14, 5m, false,
            "Trotz unserer Zahlungserinnerung ist der Betrag weiterhin offen. Bitte zahlen Sie bis zum genannten Termin.",
            "Despite our payment reminder, the amount remains outstanding. Please pay by the stated date."),
        new(2, "2. Mahnung", 28, 10m, true,
            "Wir mahnen den offenen Betrag erneut an und berechnen Verzugszinsen gemäß § 288 BGB.",
            "We again request payment and charge default interest in accordance with section 288 BGB."),
        new(3, "3./letzte Mahnung", 42, 15m, true,
            "Letzte Mahnung: Zahlen Sie bis zum genannten Termin. Andernfalls behalten wir uns rechtliche Schritte vor.",
            "Final notice: Pay by the stated date. Otherwise, we reserve the right to take legal action."),
    ];
}

/// <summary>Immutable template used to seed one dunning level.</summary>
public sealed record DunningLevelDefault(
    int Level,
    string Name,
    int DaysAfterDue,
    decimal Fee,
    bool ChargeInterest,
    string TemplateTextDe,
    string TemplateTextEn);
