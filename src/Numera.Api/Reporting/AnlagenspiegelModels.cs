namespace Numera.Api.Reporting;

public sealed record AnlagenspiegelLine(
    string Bezeichnung,
    decimal BuchwertJahresanfang,
    decimal Zugaenge,
    decimal Abgaenge,
    decimal AfaJahr,
    decimal BuchwertJahresende);

/// <summary>Book-value movements from actual booked AfA; no projected depreciation.</summary>
public sealed record AnlagenspiegelReport(int Jahr, IReadOnlyList<AnlagenspiegelLine> Anlagen, AnlagenspiegelLine Summe);
// TODO: E-Bilanz/XBRL is outside this data-basis report.
