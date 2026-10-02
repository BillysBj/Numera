using System.Text;

using Numera.Api.Pdf;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;

using QuestPDF.Infrastructure;

using UglyToad.PdfPig;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Pure QuestPDF smoke tests for the Phase-11 report print views.</summary>
public sealed class ReportPdfTests
{
    private const string ExpenseWarning =
        "Betriebsausgaben unvollständig - Belegerfassung ab Phase 12.";

    static ReportPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Both_report_documents_render_real_pdf_bytes()
    {
        var ustVaPdf = UstVaDocument.Render(BuildUstVaReport());
        var euerPdf = EuerDocument.Render(BuildEuerReport());

        AssertPdf(ustVaPdf);
        AssertPdf(euerPdf);
    }

    [Fact]
    public void Rendered_reports_contain_supplied_kennziffer_and_euer_line()
    {
        var ustVaText = ExtractText(UstVaDocument.Render(BuildUstVaReport()));
        var euerText = ExtractText(EuerDocument.Render(BuildEuerReport()));

        Assert.Contains("81", ustVaText, StringComparison.Ordinal);
        Assert.Contains("Zeile 15", euerText, StringComparison.Ordinal);
        Assert.Contains("Zeile 17", euerText, StringComparison.Ordinal);
        Assert.Contains("Zeile 57", euerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_euer_renders_the_supplied_expense_warning()
    {
        var text = ExtractText(EuerDocument.Render(BuildEuerReport()));

        Assert.Contains(ExpenseWarning, text, StringComparison.Ordinal);
    }

    private static UstVaReport BuildUstVaReport() => new(
        Jahr: 2026,
        Zeitraum: "07",
        Besteuerungsart: Besteuerungsart.Soll,
        IsFestgeschrieben: false,
        Lines:
        [
            new UstVaLine(
                "81",
                "Steuerpflichtige Umsätze zum Steuersatz von 19 %",
                Bemessungsgrundlage: 1_250m,
                Steuer: null,
                IsComputed: false),
            new UstVaLine(
                "83",
                "Verbleibende Umsatzsteuer-Vorauszahlung",
                Bemessungsgrundlage: null,
                Steuer: 237.50m,
                IsComputed: true),
        ],
        Zahllast: 237.50m,
        Hinweis: "Ohne Vorsteuerabzug (Belegerfassung ab Phase 12).",
        IsKleinunternehmer: false);

    private static EuerReport BuildEuerReport() => new(
        Jahr: 2026,
        From: new DateOnly(2026, 1, 1),
        To: new DateOnly(2026, 12, 31),
        IsKleinunternehmer: false,
        Betriebseinnahmen:
        [
            new EuerLine("15", "Umsatzsteuerpflichtige Betriebseinnahmen (netto)", 1_250m),
            new EuerLine("17", "Vereinnahmte Umsatzsteuer", 237.50m),
        ],
        SummeEinnahmen: 1_487.50m,
        Betriebsausgaben:
        [
            new EuerLine("57", "Gezahlte Vorsteuerbeträge", 50m),
        ],
        SummeAusgaben: 50m,
        Gewinn: 1_437.50m,
        IsExpenseDataIncomplete: true,
        Hinweis: ExpenseWarning);

    private static void AssertPdf(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length >= 4);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    private static string ExtractText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(page => page.Text));
    }
}
