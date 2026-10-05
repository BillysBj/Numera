namespace Numera.Api.Reporting;

/// <summary>Annual statutory reserve allocation within equity.</summary>
public record UgRuecklageReport(
    int Jahr,
    decimal Jahresueberschuss,
    decimal VerlustvortragVorjahr,
    decimal MassgeblicherBetrag,
    decimal Ruecklage,
    string? Hinweis);
