namespace Numera.Api.Reporting;

/// <summary>One amount in the Anlage-EÜR structure.</summary>
public record EuerLine(string Zeile, string Bezeichnung, decimal Betrag);

/// <summary>A cash-basis Einnahmenüberschussrechnung for one reporting range.</summary>
public record EuerReport(
    int Jahr,
    DateOnly From,
    DateOnly To,
    bool IsKleinunternehmer,
    IReadOnlyList<EuerLine> Betriebseinnahmen,
    decimal SummeEinnahmen,
    IReadOnlyList<EuerLine> Betriebsausgaben,
    decimal SummeAusgaben,
    decimal Gewinn,
    bool IsExpenseDataIncomplete,
    string? Hinweis);
