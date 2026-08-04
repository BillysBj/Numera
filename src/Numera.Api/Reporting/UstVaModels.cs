using Numera.Modules.Ledger;

namespace Numera.Api.Reporting;

/// <summary>One producible Kennziffer in the USt-VA review model.</summary>
public record UstVaLine(
    string Kz,
    string Bezeichnung,
    decimal? Bemessungsgrundlage,
    decimal? Steuer,
    bool IsComputed)
{
    /// <summary>
    /// Account-number seam for the Plan-06 journal-entry drill-down. For a computed
    /// line this is the union of the source accounts feeding its component bases.
    /// </summary>
    public IReadOnlyList<string> ContributingAccountNumbers { get; init; } = [];
}

/// <summary>USt-VA review result for one ELSTER reporting period.</summary>
public record UstVaReport(
    int Jahr,
    string Zeitraum,
    Besteuerungsart Besteuerungsart,
    bool IsFestgeschrieben,
    IReadOnlyList<UstVaLine> Lines,
    decimal Zahllast,
    string? Hinweis,
    bool IsKleinunternehmer);
