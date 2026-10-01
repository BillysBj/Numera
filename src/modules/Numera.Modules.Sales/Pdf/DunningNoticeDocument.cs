using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Numera.Modules.Sales.Pdf;

/// <summary>Single culture-driven A4 layout for German and English dunning notices.</summary>
public sealed class DunningNoticeDocument : IDocument
{
    private readonly DunningNoticeModel _model;
    private readonly Labels _labels;

    public DunningNoticeDocument(DunningNoticeModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _labels = Labels.For(model.Language);
    }

    public static byte[] Render(DunningNoticeModel model) => new DunningNoticeDocument(model).GeneratePdf();

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{_model.LevelName} {_model.InvoiceNumber}",
        Author = _model.Issuer.LegalName ?? string.Empty,
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        var invoice = new InvoiceDocument(_model.Invoice);
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));
            page.Header().Element(invoice.ComposeHeader);
            page.Content().Element(invoice.ComposeContent);
            page.Footer().Column(col =>
            {
                col.Item().Element(invoice.ComposeFooter);
                col.Item().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));
            page.Header().Element(Header);
            page.Content().PaddingVertical(20).Column(Content);
            page.Footer().AlignCenter().Text(x =>
            {
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        });
    }

    private void Header(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (_model.LogoBytes is { Length: > 0 } logo)
                {
                    col.Item().Height(48).Image(logo).FitHeight();
                }
                else
                {
                    col.Item().Text(_model.Issuer.LegalName ?? string.Empty).FontSize(15).Bold();
                }
            });
            row.ConstantItem(230).Column(col =>
            {
                col.Item().Text(_model.Issuer.LegalName ?? string.Empty).SemiBold();
                foreach (var line in AddressLines(_model.Issuer.Address))
                {
                    col.Item().Text(line);
                }
            });
        });
    }

    private void Content(ColumnDescriptor col)
    {
        col.Spacing(14);
        col.Item().Column(recipient =>
        {
            recipient.Item().Text(_model.Recipient.Name ?? string.Empty).SemiBold();
            foreach (var line in AddressLines(_model.Recipient.BillingAddress))
            {
                recipient.Item().Text(line);
            }
        });
        col.Item().PaddingTop(15).Text(_model.LevelName).FontSize(18).Bold().FontColor(Colors.Black);
        if (_model.IsFinalNotice)
        {
            col.Item().Text(_labels.FinalNotice).Bold().FontColor(Colors.Red.Darken2);
        }

        col.Item().Text(
            $"{_labels.Invoice} {_model.InvoiceNumber} {_labels.From} {Date(_model.InvoiceDate)}");
        col.Item().Text(_model.TemplateText);

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(150);
            });
            Row(table, _labels.OverdueAmount, Money(_model.OverdueAmount));
            Row(table, _labels.Fee, Money(_model.Fee));
            Row(table,
                $"{_labels.Interest} ({Number(_model.InterestRatePercent)} %, {_model.DaysOverdue} {_labels.Days})",
                Money(_model.Interest));
            table.Cell().BorderTop(1).PaddingTop(6).Text(_labels.Total).Bold();
            table.Cell().BorderTop(1).PaddingTop(6).AlignRight().Text(Money(_model.TotalToPay)).Bold();
        });

        col.Item().Text($"{_labels.NewDueDate}: {Date(_model.NewDueDate)}").Bold();
    }

    private static void Row(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingVertical(4).Text(label);
        table.Cell().PaddingVertical(4).AlignRight().Text(value);
    }

    private string Money(decimal value) => $"{Number(value)} {_model.Currency}";
    private string Number(decimal value) => value.ToString("N2", _labels.Culture);
    private string Date(DateOnly value) => value.ToString("d", _labels.Culture);

    private static IEnumerable<string> AddressLines(InvoicePdfModel.AddressBlock address)
    {
        if (!string.IsNullOrWhiteSpace(address.Street)) yield return address.Street;
        if (!string.IsNullOrWhiteSpace(address.Line2)) yield return address.Line2;
        var city = string.Join(" ", new[] { address.PostalCode, address.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (city.Length > 0) yield return city;
        if (!string.IsNullOrWhiteSpace(address.CountryCode)) yield return address.CountryCode;
    }

    private sealed record Labels(
        CultureInfo Culture,
        string Invoice,
        string From,
        string OverdueAmount,
        string Fee,
        string Interest,
        string Days,
        string Total,
        string NewDueDate,
        string FinalNotice)
    {
        public static Labels For(string language) =>
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                ? new(CultureInfo.GetCultureInfo("en-GB"), "Invoice", "dated", "Outstanding amount",
                    "Dunning fee", "Default interest", "days", "Total to pay", "New payment deadline",
                    "Final notice")
                : new(CultureInfo.GetCultureInfo("de-DE"), "Rechnung", "vom", "Offener Betrag",
                    "Mahngebühr", "Verzugszinsen", "Tage", "Gesamt zu zahlen", "Neue Zahlungsfrist",
                    "Letzte Mahnung");
    }
}
