using System.Globalization;

namespace Numera.Api.Pdf;

/// <summary>German presentation labels and formatting culture shared by report PDFs.</summary>
public sealed record ReportPdfLabels(
    CultureInfo Culture,
    string UstVaTitle,
    string EuerTitle,
    string Year,
    string Period,
    string TaxationType,
    string Preliminary,
    string Kz,
    string Line,
    string Description,
    string TaxableBase,
    string Tax,
    string Amount,
    string BusinessIncome,
    string BusinessExpenses,
    string TotalIncome,
    string TotalExpenses,
    string ProfitOrLoss,
    string Note)
{
    /// <summary>The fixed German label set. Its culture is independent of the worker culture.</summary>
    public static ReportPdfLabels German { get; } = new(
        new CultureInfo("de-DE"),
        "Umsatzsteuer-Voranmeldung",
        "Einnahmenüberschussrechnung (Anlage EÜR)",
        "Jahr",
        "Zeitraum",
        "Besteuerungsart",
        "vorläufig (Zeitraum noch nicht festgeschrieben)",
        "Kz",
        "Zeile",
        "Bezeichnung",
        "Bemessungsgrundlage",
        "Steuer",
        "Betrag",
        "Betriebseinnahmen",
        "Betriebsausgaben",
        "Summe Betriebseinnahmen",
        "Summe Betriebsausgaben",
        "Gewinn / Verlust",
        "Hinweis");
}
