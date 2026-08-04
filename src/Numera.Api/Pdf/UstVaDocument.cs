using System.Globalization;

using Numera.Api.Reporting;
using Numera.Modules.Ledger;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Numera.Api.Pdf;

/// <summary>QuestPDF print view for one Umsatzsteuer-Voranmeldung report.</summary>
public sealed class UstVaDocument : IDocument
{
    private readonly UstVaReport _report;
    private readonly ReportPdfLabels _labels = ReportPdfLabels.German;
    private readonly CultureInfo _culture;

    /// <summary>Creates the print view from the already-calculated report model.</summary>
    public UstVaDocument(UstVaReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _culture = _labels.Culture;
    }

    /// <summary>Renders the report to a PDF byte array.</summary>
    public static byte[] Render(UstVaReport report) => new UstVaDocument(report).GeneratePdf();

    /// <inheritdoc />
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{_labels.UstVaTitle} {_report.Jahr.ToString(_culture)} {_report.Zeitraum}",
    };

    /// <inheritdoc />
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    /// <inheritdoc />
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.PageColor(Colors.White);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Grey.Darken4));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(16).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text(_labels.UstVaTitle)
                .FontSize(20).Bold().FontColor(Colors.Black);

            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"{_labels.Year}: {_report.Jahr.ToString(_culture)}");
                row.RelativeItem().Text($"{_labels.Period}: {_report.Zeitraum}");
                row.RelativeItem().AlignRight().Text(
                    $"{_labels.TaxationType}: {FormatTaxationType(_report.Besteuerungsart)}");
            });

            if (!_report.IsFestgeschrieben)
            {
                column.Item()
                    .PaddingTop(10)
                    .Background(Colors.Orange.Lighten4)
                    .Border(1)
                    .BorderColor(Colors.Orange.Darken1)
                    .Padding(7)
                    .Text(_labels.Preliminary)
                    .SemiBold()
                    .FontColor(Colors.Orange.Darken4);
            }
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(16);
            column.Item().Element(ComposeLinesTable);

            if (!string.IsNullOrWhiteSpace(_report.Hinweis))
            {
                column.Item()
                    .BorderTop(0.75f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .PaddingTop(8)
                    .Text(text =>
                    {
                        text.Span($"{_labels.Note}: ").SemiBold().FontSize(8.5f);
                        text.Span(_report.Hinweis!).FontSize(8.5f);
                    });
            }
        });
    }

    private void ComposeLinesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(40);
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
            });

            table.Header(header =>
            {
                HeaderCell(header, _labels.Kz);
                HeaderCell(header, _labels.Description);
                HeaderCell(header, _labels.TaxableBase, right: true);
                HeaderCell(header, _labels.Tax, right: true);
            });

            // NO-FABRICATION: only rows supplied by the report model are printed.
            foreach (var line in _report.Lines)
            {
                var highlight = string.Equals(line.Kz, "83", StringComparison.Ordinal);
                BodyCell(table, highlight).Text(line.Kz).SemiBold();
                BodyCell(table, highlight).Text(line.Bezeichnung);
                BodyCell(table, highlight).AlignRight().Text(NullableMoney(line.Bemessungsgrundlage));
                BodyCell(table, highlight).AlignRight().Text(
                    highlight ? Money(_report.Zahllast) : NullableMoney(line.Steuer));
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(5)
            .DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1))
            .AlignCenter().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
    }

    private static void HeaderCell(TableCellDescriptor header, string value, bool right = false)
    {
        var cell = header.Cell()
            .Background(Colors.Grey.Lighten3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Darken1)
            .Padding(5);
        var content = right ? cell.AlignRight() : cell;
        content.Text(value).SemiBold().FontSize(8.5f);
    }

    private static IContainer BodyCell(TableDescriptor table, bool highlight)
    {
        var cell = table.Cell()
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(5);
        return highlight ? cell.Background(Colors.Blue.Lighten4) : cell;
    }

    private string Money(decimal value) => $"{value.ToString("N2", _culture)} €";

    private string NullableMoney(decimal? value) => value is { } amount ? Money(amount) : string.Empty;

    private static string FormatTaxationType(Besteuerungsart value) => value switch
    {
        Besteuerungsart.Soll => "Sollversteuerung",
        Besteuerungsart.Ist => "Istversteuerung",
        _ => value.ToString(),
    };
}
