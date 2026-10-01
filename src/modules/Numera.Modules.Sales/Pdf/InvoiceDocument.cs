using System.Globalization;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Numera.Modules.Sales.Pdf;

/// <summary>
/// The single, resource-driven QuestPDF §14-UStG invoice layout. Given an
/// <see cref="InvoicePdfModel"/> (built by <see cref="SnapshotReader"/> from the frozen
/// snapshot) it renders a professional A4 PDF on the tenant letterhead in a German
/// (default) or English label set — logo, issuer imprint, recipient block, document meta,
/// line table, totals, the BG-23 VAT breakdown, the verbatim frozen Pflichttexte, and the
/// bank/payment block.
/// </summary>
/// <remarks>
/// <para>
/// One layout serves both languages via <see cref="PdfLabels"/> (no layout drift —
/// RESEARCH.md Pitfall 6). All money/dates format under the label set's explicit
/// <see cref="CultureInfo"/> (de-DE default), never the worker's ambient culture
/// (Pitfall 4). The legally-frozen Pflichttexte from
/// <see cref="InvoicePdfModel.BreakdownRow.ExemptionReasonText"/> are printed verbatim and
/// stay German even under the English labels (LOCKED).
/// </para>
/// <para>
/// Phase-5 ZUGFeRD seam (05-04): the SAME §14 layout renders as PDF/A-3b via
/// <see cref="RenderPdfA"/> — the ONLY difference is <see cref="GetSettings"/> returns
/// <see cref="PDFA_Conformance.PDFA_3B"/> (the level ZUGFeRD/Factur-X requires to embed the
/// CII). PDF/A-3b is produced by CORE QuestPDF 2026.7.1 (no extra package, no iText/AGPL);
/// <c>ZugferdGenerator</c> then attaches the <c>factur-x.xml</c> CII via
/// <c>DocumentOperation</c>. The non-PDF/A <see cref="Render"/> path (Phase-4 §14 PDF) is
/// byte-layout unchanged — it returns <see cref="DocumentSettings.Default"/> exactly as before.
/// </para>
/// </remarks>
public sealed class InvoiceDocument : IDocument
{
    private readonly InvoicePdfModel _model;
    private readonly PdfLabels _labels;
    private readonly CultureInfo _culture;
    private readonly bool _pdfA;

    // Design palette. Accent is the single brand tone — change this one value to
    // re-skin the whole invoice in a company colour. Everything else is neutral.
    private const string Accent = "#28374D";      // deep ink-navy: title, table head, total
    private const string AccentSoft = "#5B6B82";  // muted accent: section labels
    private const string Tint = "#EEF1F6";        // panel / total-band background
    private const string Hairline = "#D7DDE6";    // light rules and borders

    /// <summary>Creates the document for a render model.</summary>
    /// <param name="model">The render model built from the frozen snapshot.</param>
    /// <param name="pdfA">When true, <see cref="GetSettings"/> targets PDF/A-3b (the ZUGFeRD base).</param>
    public InvoiceDocument(InvoicePdfModel model, bool pdfA = false)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _labels = PdfLabels.For(model.Language);
        _culture = _labels.Culture;
        _pdfA = pdfA;
    }

    /// <summary>Renders <paramref name="model"/> to a PDF <c>byte[]</c> (the storable artifact).</summary>
    public static byte[] Render(InvoicePdfModel model) => new InvoiceDocument(model).GeneratePdf();

    /// <summary>
    /// Renders <paramref name="model"/> to a <b>PDF/A-3b</b> <c>byte[]</c> — the byte-identical §14
    /// layout, only tagged PDF/A-3b (the conformance level ZUGFeRD/Factur-X requires so the CII can
    /// be embedded). Fonts are embedded by QuestPDF automatically (RESEARCH Pitfall 5). This is the
    /// base <c>ZugferdGenerator</c> wraps with the <c>factur-x.xml</c> attachment + ZUGFeRD XMP.
    /// </summary>
    public static byte[] RenderPdfA(InvoicePdfModel model) => new InvoiceDocument(model, pdfA: true).GeneratePdf();

    /// <inheritdoc />
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{DocumentTitle()} {_model.DocumentNumber}".Trim(),
        Author = _model.Issuer.LegalName ?? string.Empty,
    };

    /// <inheritdoc />
    /// <remarks>
    /// The §14 path returns <see cref="DocumentSettings.Default"/> (identical to the implicit
    /// default the Phase-4 render used). The ZUGFeRD path adds ONLY
    /// <see cref="PDFA_Conformance.PDFA_3B"/> on top of those defaults — same compression, DPI and
    /// image quality — so the printed page is unchanged, just PDF/A-3b-tagged. (The legacy
    /// <c>DocumentSettings.PdfA</c> boolean is deprecated; the conformance enum is the current API.)
    /// </remarks>
    public DocumentSettings GetSettings()
    {
        var settings = DocumentSettings.Default;
        if (_pdfA)
        {
            settings.PDFA_Conformance = PDFA_Conformance.PDFA_3B;
        }

        return settings;
    }

    /// <summary>Formats a decimal as a plain grouped number ("1.234,56" in de-DE). Exposed for tests (Pitfall 4).</summary>
    public static string FormatNumber(decimal value, CultureInfo culture) =>
        value.ToString("N2", culture);

    /// <inheritdoc />
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        var issuer = _model.Issuer;

        container.Column(outer =>
        {
            // Slim brand accent bar across the top of the letterhead.
            outer.Item().PaddingBottom(10).Height(3).Background(Accent);

            outer.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Column(col =>
                {
                    if (_model.LogoBytes is { Length: > 0 } logo)
                    {
                        col.Item().Height(48).AlignLeft().Image(logo).FitHeight();
                    }
                    else
                    {
                        col.Item().Text(issuer.LegalName ?? string.Empty)
                            .FontSize(15).Bold().FontColor(Accent);
                    }
                });

                row.ConstantItem(230).AlignRight().Column(col =>
                {
                    col.Item().Text(issuer.LegalName ?? string.Empty).SemiBold().FontColor(Accent);
                    foreach (var line in AddressLines(issuer.Address))
                    {
                        col.Item().Text(line);
                    }

                    var contact = new[] { issuer.ContactEmail, issuer.ContactPhone }
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!)
                        .ToList();
                    for (var i = 0; i < contact.Count; i++)
                    {
                        var item = i == 0 ? col.Item().PaddingTop(4) : col.Item();
                        item.Text(contact[i]);
                    }
                });
            });

            // Separates the letterhead from the document body.
            outer.Item().PaddingTop(8).LineHorizontal(0.75f).LineColor(Hairline);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(12).Column(col =>
        {
            col.Spacing(12);

            col.Item().Element(ComposeRecipientAndMeta);
            col.Item().Element(ComposeLinesTable);
            col.Item().Element(ComposeTotals);
            col.Item().Element(ComposeBreakdown);
            col.Item().Element(ComposePrepayments);
            col.Item().Element(ComposePflichttexte);
            col.Item().Element(ComposePaymentBlock);

            if (!string.IsNullOrWhiteSpace(_model.Notes))
            {
                col.Item().PaddingTop(6).Text(_model.Notes!);
            }
        });
    }

    private void ComposeRecipientAndMeta(IContainer container)
    {
        var recipient = _model.Recipient;

        container.Row(row =>
        {
            // Recipient (Bill To) block.
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(_labels.BillTo.ToUpperInvariant())
                    .FontSize(7.5f).FontColor(AccentSoft).LetterSpacing(0.08f);

                // Append the legal form only when the name does not already carry it
                // (a partner named "Muster Handels GmbH" + legal form "GmbH" must not read
                // "Muster Handels GmbH GmbH").
                var legalForm = recipient.LegalForm?.Trim();
                var baseName = recipient.Name ?? string.Empty;
                var name = string.IsNullOrWhiteSpace(legalForm)
                    || baseName.TrimEnd().EndsWith(legalForm, StringComparison.OrdinalIgnoreCase)
                        ? baseName
                        : $"{baseName} {legalForm}";
                col.Item().PaddingTop(3).Text(name).FontSize(11).SemiBold();

                foreach (var line in AddressLines(recipient.BillingAddress))
                {
                    col.Item().Text(line);
                }

                if (!string.IsNullOrWhiteSpace(recipient.VatId))
                {
                    col.Item().PaddingTop(4).Text($"{_labels.VatId}: {recipient.VatId}");
                }
                else if (!string.IsNullOrWhiteSpace(recipient.TaxNumber))
                {
                    col.Item().PaddingTop(4).Text($"{_labels.TaxNo}: {recipient.TaxNumber}");
                }
            });

            // Document title + meta card.
            row.ConstantItem(232).Column(col =>
            {
                col.Item().PaddingBottom(8).AlignRight()
                    .Text($"{DocumentTitle()} {_model.DocumentNumber}".Trim())
                    .FontSize(20).Bold().FontColor(Accent);

                col.Item().Background(Tint).Padding(10).Column(meta =>
                {
                    meta.Spacing(1);
                    MetaLine(meta, DocumentNoLabel(), _model.DocumentNumber);
                    MetaLine(meta, _labels.Date, FormatDate(_model.DocumentDate));

                    if (_model.ServicePeriodEnd is { } periodEnd && _model.ServiceDate is { } periodStart)
                    {
                        MetaLine(meta, _labels.ServicePeriod, $"{FormatDate(periodStart)} – {FormatDate(periodEnd)}");
                    }
                    else if (_model.ServiceDate is { } svc)
                    {
                        MetaLine(meta, _labels.ServiceDate, FormatDate(svc));
                    }

                    if (_model.DueDate is { } due)
                    {
                        MetaLine(meta, _labels.Due, FormatDate(due));
                    }

                    if (!string.IsNullOrWhiteSpace(_model.BuyerReference))
                    {
                        MetaLine(meta, _labels.BuyerReference, _model.BuyerReference);
                    }
                });
            });
        });
    }

    private void ComposeLinesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);   // Pos.
                c.RelativeColumn(5);    // Description
                c.RelativeColumn(1.4f); // Qty
                c.RelativeColumn(2);    // Unit price
                c.RelativeColumn(2);    // Amount
            });

            table.Header(header =>
            {
                HeaderCell(header, _labels.Item);
                HeaderCell(header, _labels.Description);
                HeaderCell(header, _labels.Qty, right: true);
                HeaderCell(header, _labels.UnitPrice, right: true);
                HeaderCell(header, _labels.Amount, right: true);
            });

            foreach (var line in _model.Lines)
            {
                BodyCell(table).Text(line.LineNumber.ToString(CultureInfo.InvariantCulture));

                BodyCell(table).Column(col =>
                {
                    col.Item().Text(line.Name).SemiBold();
                    if (!string.IsNullOrWhiteSpace(line.Description))
                    {
                        col.Item().Text(line.Description!).FontSize(8).FontColor(Colors.Grey.Darken1);
                    }
                });

                BodyCell(table).AlignRight().Text(
                    $"{FormatNumber(line.Quantity, _culture)} {UnitDisplay(line.UnitCode)}".Trim());
                BodyCell(table).AlignRight().Text(Money(line.NetUnitPrice));
                BodyCell(table).AlignRight().Text(Money(line.LineNetAmount));
            }
        });
    }

    private void ComposeTotals(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.ConstantItem(240).Column(col =>
            {
                TotalLine(col, _labels.SubtotalNet, Money(_model.TotalNet), bold: false);
                TotalLine(col, _labels.Vat, Money(_model.TotalTax), bold: false);
                col.Item().PaddingVertical(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);

                // The grand total gets an accent-tinted band so it reads at a glance.
                col.Item().PaddingTop(2).Background(Tint).PaddingVertical(6).PaddingHorizontal(8).Row(row =>
                {
                    row.RelativeItem().Text(_labels.Total).Bold().FontSize(12).FontColor(Accent);
                    row.ConstantItem(120).AlignRight().Text(Money(_model.TotalGross))
                        .Bold().FontSize(12).FontColor(Accent);
                });

                if (_model.AmountDue != _model.TotalGross)
                {
                    TotalLine(col, _labels.PayableBy, Money(_model.AmountDue), bold: true);
                }

                if (!string.Equals(_model.Currency, "EUR", StringComparison.OrdinalIgnoreCase)
                    && _model.TotalTaxEur is decimal totalTaxEur)
                {
                    TotalLine(
                        col,
                        _model.Language == "en" ? "VAT in EUR" : "USt in EUR",
                        $"{FormatNumber(totalTaxEur, _culture)} EUR",
                        bold: false);

                    if (_model.ExchangeRate is decimal exchangeRate
                        && _model.ExchangeRateDate is DateOnly exchangeRateDate)
                    {
                        var prefix = _model.Language == "en" ? "Exchange rate" : "Umrechnungskurs";
                        var dateLabel = _model.Language == "en" ? "as of" : "Stand";
                        col.Item()
                            .PaddingTop(2)
                            .AlignRight()
                            .Text(
                                $"{prefix}: 1 EUR = {exchangeRate.ToString("0.######", _culture)} "
                                + $"{_model.Currency}, {dateLabel} {FormatDate(exchangeRateDate)}")
                            .FontSize(7)
                            .FontColor(Colors.Grey.Darken1);
                    }
                }
            });
        });
    }

    private void ComposeBreakdown(IContainer container)
    {
        if (_model.BreakdownRows.Count == 0)
        {
            container.Text(string.Empty);
            return;
        }

        container.Column(outer =>
        {
            outer.Item().Text(_labels.VatBreakdown.ToUpperInvariant())
                .FontSize(7.5f).FontColor(AccentSoft).LetterSpacing(0.08f);
            outer.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);   // Category
                    c.RelativeColumn(1.5f); // Rate
                    c.RelativeColumn(2);   // Base
                    c.RelativeColumn(2);   // Tax
                });

                table.Header(header =>
                {
                    HeaderCell(header, _labels.Vat);
                    HeaderCell(header, _labels.Rate, right: true);
                    HeaderCell(header, _labels.TaxableBase, right: true);
                    HeaderCell(header, _labels.TaxAmount, right: true);
                });

                foreach (var b in _model.BreakdownRows)
                {
                    BodyCell(table).Text(b.TaxCategory.ToString());
                    BodyCell(table).AlignRight().Text(FormatPercent(b.VatRatePercent));
                    BodyCell(table).AlignRight().Text(Money(b.TaxableBase));
                    BodyCell(table).AlignRight().Text(Money(b.TaxAmount));
                }
            });
        });
    }

    private void ComposePrepayments(IContainer container)
    {
        if (_model.Prepayments.Count == 0)
        {
            container.Text(string.Empty);
            return;
        }

        var english = string.Equals(_model.Language, "en", StringComparison.OrdinalIgnoreCase);
        container.Column(outer =>
        {
            outer.Item().Text((english ? "Deduction of advance invoices" : "Abzug geleisteter Abschläge").ToUpperInvariant())
                .FontSize(7.5f).FontColor(AccentSoft).LetterSpacing(0.08f);
            outer.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    HeaderCell(header, english ? "Item" : "Pos.");
                    HeaderCell(header, english ? "No." : "Nr.");
                    HeaderCell(header, english ? "Date" : "Datum");
                    HeaderCell(header, english ? "Net" : "Netto", right: true);
                    HeaderCell(header, english ? "VAT" : "USt", right: true);
                    HeaderCell(header, english ? "Gross" : "Brutto", right: true);
                });

                foreach (var prepayment in _model.Prepayments)
                {
                    BodyCell(table).Text(prepayment.LineNumber.ToString(CultureInfo.InvariantCulture));
                    BodyCell(table).Text(prepayment.AbschlagNumber);
                    BodyCell(table).Text(FormatDate(prepayment.AbschlagDate));
                    BodyCell(table).AlignRight().Text(Money(prepayment.NetAmount));
                    BodyCell(table).AlignRight().Text(Money(prepayment.VatAmount));
                    BodyCell(table).AlignRight().Text(Money(prepayment.GrossAmount));
                }
            });

            outer.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(english ? "Total prepaid" : "Summe Abschläge").SemiBold();
                row.ConstantItem(110).AlignRight().Text(Money(_model.TotalPrepaid)).Bold();
            });
            outer.Item().Row(row =>
            {
                row.RelativeItem().Text(english ? "Amount due" : "Noch zu zahlen").SemiBold();
                row.ConstantItem(110).AlignRight().Text(Money(_model.AmountDue)).Bold();
            });
        });
    }

    private void ComposePflichttexte(IContainer container)
    {
        // The legally-frozen exemption notes (§19 / §13b ...), rendered VERBATIM (LOCKED).
        var texts = _model.BreakdownRows
            .Select(b => b.ExemptionReasonText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (texts.Count == 0)
        {
            container.Text(string.Empty);
            return;
        }

        container.Column(col =>
        {
            col.Spacing(3);
            foreach (var text in texts)
            {
                col.Item().Text(text).FontSize(8.5f);
            }
        });
    }

    private void ComposePaymentBlock(IContainer container)
    {
        var issuer = _model.Issuer;
        var hasBank = !string.IsNullOrWhiteSpace(issuer.Iban);

        container.Column(col =>
        {
            if (_model.DueDate is { } due && !_model.IsKleinunternehmer)
            {
                col.Item().Text($"{_labels.PayableBy}: {FormatDate(due)}").SemiBold().FontColor(Accent);
            }

            if (hasBank)
            {
                col.Item().PaddingTop(6).Text(_labels.BankDetails.ToUpperInvariant())
                    .FontSize(7.5f).FontColor(AccentSoft).LetterSpacing(0.08f);
                col.Item().Text(t =>
                {
                    if (!string.IsNullOrWhiteSpace(issuer.BankName))
                    {
                        t.Span($"{issuer.BankName}  ");
                    }

                    t.Span($"{_labels.Iban}: {issuer.Iban}");
                    if (!string.IsNullOrWhiteSpace(issuer.Bic))
                    {
                        t.Span($"   {_labels.Bic}: {issuer.Bic}");
                    }
                });
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        var issuer = _model.Issuer;
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(issuer.VatId))
        {
            parts.Add($"{_labels.VatId}: {issuer.VatId}");
        }

        if (!string.IsNullOrWhiteSpace(issuer.TaxNumber))
        {
            parts.Add($"{_labels.TaxNo}: {issuer.TaxNumber}");
        }

        if (!string.IsNullOrWhiteSpace(issuer.ManagingDirector))
        {
            parts.Add(issuer.ManagingDirector!);
        }

        if (!string.IsNullOrWhiteSpace(issuer.RegisterCourt) || !string.IsNullOrWhiteSpace(issuer.RegisterNumber))
        {
            parts.Add(string.Join(" ", new[] { issuer.RegisterCourt, issuer.RegisterNumber }
                .Where(s => !string.IsNullOrWhiteSpace(s))));
        }

        container.BorderTop(0.75f).BorderColor(Hairline).PaddingTop(5)
            .Text(string.Join("  •  ", parts))
            .FontSize(7.5f).FontColor(AccentSoft).AlignCenter();
    }

    // --- Small composition helpers -------------------------------------------

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().BorderBottom(1.5f).BorderColor(Accent).PaddingVertical(4);
        var content = right ? cell.AlignRight() : cell;
        content.Text(text).SemiBold().FontSize(8.5f).FontColor(Accent);
    }

    private static IContainer BodyCell(TableDescriptor table) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);

    private static void MetaLine(ColumnDescriptor col, string label, string? value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(92).Text(label).FontSize(8.5f).FontColor(AccentSoft);
            row.RelativeItem().AlignRight().Text(value ?? string.Empty).FontSize(8.5f).SemiBold();
        });
    }

    private static void TotalLine(ColumnDescriptor col, string label, string value, bool bold)
    {
        col.Item().Row(row =>
        {
            var l = row.RelativeItem().Text(label);
            var v = row.ConstantItem(110).AlignRight().Text(value);
            if (bold)
            {
                l.SemiBold();
                v.Bold();
            }
        });
    }

    private IEnumerable<string> AddressLines(InvoicePdfModel.AddressBlock a)
    {
        if (!string.IsNullOrWhiteSpace(a.Street))
        {
            yield return a.Street!;
        }

        if (!string.IsNullOrWhiteSpace(a.Line2))
        {
            yield return a.Line2!;
        }

        var cityLine = string.Join(" ", new[] { a.PostalCode, a.City }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(cityLine))
        {
            yield return cityLine;
        }

        if (!string.IsNullOrWhiteSpace(a.CountryCode))
        {
            yield return a.CountryCode!;
        }
    }

    // --- Culture-correct formatting (Pitfall 4) ------------------------------

    private string Money(decimal value) =>
        $"{FormatNumber(value, _culture)} {CurrencySymbol(_model.Currency)}".TrimEnd();

    private string FormatDate(DateOnly date) => date.ToString("d", _culture);

    private string FormatPercent(decimal rate) =>
        $"{rate.ToString("0.##", _culture)} %";

    private static string CurrencySymbol(string currency) => currency switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        "CHF" => "CHF",
        _ => currency,
    };

    // Human-readable unit label for the PDF only. The stored UN/ECE Rec 20 code (BT-130) is
    // unchanged in the e-invoice XML — this maps the curated catalog units to the labels a
    // customer expects on paper, falling back to the raw code for anything unmapped.
    private string UnitDisplay(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var map = _model.Language == "en" ? UnitLabelsEn : UnitLabelsDe;
        return map.TryGetValue(code, out var label) ? label : code;
    }

    private static readonly IReadOnlyDictionary<string, string> UnitLabelsDe = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C62"] = "Stk.",
        ["H87"] = "Stk.",
        ["HUR"] = "Std.",
        ["DAY"] = "Tage",
        ["MON"] = "Monate",
        ["KGM"] = "kg",
        ["MTR"] = "m",
        ["MTK"] = "m²",
        ["LTR"] = "l",
        ["KWH"] = "kWh",
    };

    private static readonly IReadOnlyDictionary<string, string> UnitLabelsEn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C62"] = "pcs",
        ["H87"] = "pcs",
        ["HUR"] = "hrs",
        ["DAY"] = "days",
        ["MON"] = "months",
        ["KGM"] = "kg",
        ["MTR"] = "m",
        ["MTK"] = "m²",
        ["LTR"] = "l",
        ["KWH"] = "kWh",
    };

    // The printed document title and number label, by document type and language. The
    // PDF renders every document kind (Angebot, Lieferschein, …), not only invoices, so
    // the title must reflect the actual type instead of always reading "Rechnung".
    private string DocumentTitle()
    {
        var map = _model.Language == "en" ? DocTitlesEn : DocTitlesDe;
        return map.TryGetValue(_model.DocumentType, out var title)
            ? title
            : (_model.Language == "en" ? "Document" : "Beleg");
    }

    private string DocumentNoLabel() => _model.Language == "en" ? "Document no." : "Belegnummer";

    private static readonly IReadOnlyDictionary<DocumentType, string> DocTitlesDe = new Dictionary<DocumentType, string>
    {
        [DocumentType.Angebot] = "Angebot",
        [DocumentType.Auftragsbestaetigung] = "Auftragsbestätigung",
        [DocumentType.Lieferschein] = "Lieferschein",
        [DocumentType.Rechnung] = "Rechnung",
        [DocumentType.Storno] = "Stornorechnung",
        [DocumentType.Gutschrift] = "Gutschrift",
        [DocumentType.Abschlagsrechnung] = "Abschlagsrechnung",
        [DocumentType.Schlussrechnung] = "Schlussrechnung",
    };

    private static readonly IReadOnlyDictionary<DocumentType, string> DocTitlesEn = new Dictionary<DocumentType, string>
    {
        [DocumentType.Angebot] = "Quote",
        [DocumentType.Auftragsbestaetigung] = "Order Confirmation",
        [DocumentType.Lieferschein] = "Delivery Note",
        [DocumentType.Rechnung] = "Invoice",
        [DocumentType.Storno] = "Cancellation",
        [DocumentType.Gutschrift] = "Credit Note",
        [DocumentType.Abschlagsrechnung] = "Down Payment Invoice",
        [DocumentType.Schlussrechnung] = "Final Invoice",
    };
}
