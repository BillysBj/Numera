using System.Globalization;

using Numera.Api.Reporting;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Numera.Api.Pdf;

/// <summary>QuestPDF print view for one Einnahmenüberschussrechnung report.</summary>
public sealed class EuerDocument : IDocument
{
    private readonly EuerReport _report;
    private readonly ReportPdfLabels _labels = ReportPdfLabels.German;
    private readonly CultureInfo _culture;

    /// <summary>Creates the print view from the already-calculated report model.</summary>
    public EuerDocument(EuerReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _culture = _labels.Culture;
    }

    /// <summary>Renders the report to a PDF byte array.</summary>
    public static byte[] Render(EuerReport report) => new EuerDocument(report).GeneratePdf();

    /// <inheritdoc />
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{_labels.EuerTitle} {_report.Jahr.ToString(_culture)}",
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
            column.Item().Text(_labels.EuerTitle)
                .FontSize(16).Bold().FontColor(Colors.Black);
            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"{_labels.Year}: {_report.Jahr.ToString(_culture)}");
                row.RelativeItem().AlignRight().Text(
                    $"{_labels.Period}: {Date(_report.From)} - {Date(_report.To)}");
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(18);
            column.Item().Element(content => ComposeSection(
                content,
                _labels.BusinessIncome,
                _report.Betriebseinnahmen,
                _labels.TotalIncome,
                _report.SummeEinnahmen));

            column.Item().Element(content => ComposeSection(
                content,
                _labels.BusinessExpenses,
                _report.Betriebsausgaben,
                _labels.TotalExpenses,
                _report.SummeAusgaben));

            if (_report.IsExpenseDataIncomplete && !string.IsNullOrWhiteSpace(_report.Hinweis))
            {
                column.Item().Element(ComposeIncompleteExpenseWarning);
            }

            column.Item().Element(ComposeProfit);
        });
    }

    private void ComposeSection(
        IContainer container,
        string heading,
        IReadOnlyList<EuerLine> lines,
        string totalLabel,
        decimal total)
    {
        container.Column(column =>
        {
            column.Item().Text(heading).FontSize(13).Bold().FontColor(Colors.Black);
            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(65);
                    columns.RelativeColumn();
                    columns.ConstantColumn(105);
                });

                table.Header(header =>
                {
                    HeaderCell(header, _labels.Line);
                    HeaderCell(header, _labels.Description);
                    HeaderCell(header, _labels.Amount, right: true);
                });

                // NO-FABRICATION: the document does not synthesize missing Anlage-EÜR lines.
                for (var index = 0; index < lines.Count; index++)
                {
                    var line = lines[index];
                    var row = (uint)index + 1;
                    BodyCell(table, row, 1).Text($"{_labels.Line} {line.Zeile}");
                    BodyCell(table, row, 2).Text(line.Bezeichnung);
                    BodyCell(table, row, 3).AlignRight().Text(Money(line.Betrag));
                }
            });

            column.Item().Background(Colors.Grey.Lighten4).Padding(6).Row(row =>
            {
                row.RelativeItem().Text(totalLabel).SemiBold();
                row.ConstantItem(105).AlignRight().Text(Money(total)).Bold();
            });
        });
    }

    private void ComposeIncompleteExpenseWarning(IContainer container)
    {
        container.Background(Colors.Orange.Lighten4)
            .Border(1)
            .BorderColor(Colors.Orange.Darken1)
            .Padding(9)
            .Column(column =>
            {
                column.Item().Text(_labels.Note).Bold().FontColor(Colors.Orange.Darken4);
                column.Item().PaddingTop(2).Text(_report.Hinweis!)
                    .SemiBold().FontColor(Colors.Orange.Darken4);
            });
    }

    private void ComposeProfit(IContainer container)
    {
        container.Background(Colors.Blue.Lighten4)
            .Border(1)
            .BorderColor(Colors.Blue.Darken1)
            .Padding(10)
            .Row(row =>
            {
                row.RelativeItem().Text(_labels.ProfitOrLoss).FontSize(12).Bold();
                row.ConstantItem(125).AlignRight().Text(Money(_report.Gewinn)).FontSize(12).Bold();
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

    private static IContainer BodyCell(TableDescriptor table, uint row, uint column) =>
        table.Cell().Row(row).Column(column)
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(5);

    private string Money(decimal value) => $"{value.ToString("N2", _culture)} €";

    private string Date(DateOnly value) => value.ToString("d", _culture);
}
