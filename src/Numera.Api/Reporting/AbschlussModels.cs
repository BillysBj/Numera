namespace Numera.Api.Reporting;

/// <summary>One amount in the grouped balance sheet.</summary>
public record BilanzPosition(string Gruppe, string Bezeichnung, decimal Betrag);

/// <summary>A balance sheet at the fiscal year end, including the current year's result.</summary>
public record BilanzReport(
    int Jahr,
    DateOnly Stichtag,
    IReadOnlyList<BilanzPosition> Aktiva,
    decimal SummeAktiva,
    IReadOnlyList<BilanzPosition> Passiva,
    decimal SummePassiva,
    decimal BilanzDifferenz,
    string? Hinweis);

/// <summary>One amount in the grouped profit and loss statement.</summary>
public record GuvPosition(string Bezeichnung, decimal Betrag);

/// <summary>A profit and loss statement for one inclusive fiscal year.</summary>
public record GuvReport(
    int Jahr,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GuvPosition> Ertraege,
    IReadOnlyList<GuvPosition> Aufwendungen,
    decimal Jahresueberschuss,
    string? Hinweis);
