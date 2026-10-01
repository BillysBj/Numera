using System.Globalization;
using System.Text;

namespace Numera.Modules.Ledger;

/// <summary>One DATEV booking with resolved accounts, gross amount and source-document number.</summary>
public sealed record DatevPostingLine(
    decimal Amount, PostingDirection Direction, string Account, string CounterAccount,
    Steuerschluessel? TaxKey, DateOnly EntryDate, string DocumentNumber, string Description);

/// <summary>DATEV EXTF 700 / Buchungsstapel format version 7 (116 data fields).</summary>
public static class DatevExport
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    // Public DATEV field order. Optional fields remain empty, including Festschreibung:
    // downloading a file never permanently locks a journal entry.
    public static IReadOnlyList<string> Columns { get; } = BuildColumns().AsReadOnly();

    public static DateOnly FiscalYearStart(DateOnly date, int startMonth) =>
        new(date.Month < startMonth ? date.Year - 1 : date.Year, startMonth, 1);

    public static byte[] Render(
        LedgerSettings settings, DateOnly from, DateOnly to,
        IEnumerable<DatevPostingLine> lines, DateTimeOffset createdAt)
    {
        if (settings.ChartVariant is not (ChartVariant.Skr03 or ChartVariant.Skr04)
            || settings.FiscalYearStartMonth is < 1 or > 12)
        {
            throw new ArgumentException("Kontenrahmen ist nicht eingerichtet", nameof(settings));
        }

        var fiscalStart = FiscalYearStart(from, settings.FiscalYearStartMonth);
        if (from > to || FiscalYearStart(to, settings.FiscalYearStartMonth) != fiscalStart)
        {
            throw new ArgumentException("Der Zeitraum muss innerhalb eines Wirtschaftsjahres liegen.", nameof(to));
        }

        // Neither advisor/client identifiers exist in LedgerSettings. 0 is an explicit
        // placeholder; the receiving tax advisor must assign their own client on import.
        // Both seeded SKR03/SKR04 charts use four-digit general-ledger account numbers.
        var header = new string[31];
        header[0] = Quote("EXTF");
        header[1] = "700";
        header[2] = "21";
        header[3] = Quote("Buchungsstapel");
        header[4] = "7";
        header[5] = createdAt.UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        header[7] = Quote("RE");
        header[8] = Quote("Numera");
        header[10] = "0";
        header[11] = "0";
        header[12] = fiscalStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        header[13] = "4";
        header[14] = from.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        header[15] = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        header[16] = Quote($"Numera SKR{(int)settings.ChartVariant:00}");
        header[18] = "1";
        header[19] = "0";
        header[20] = "0";
        header[21] = Quote("EUR");

        var csv = new StringBuilder();
        AppendRow(csv, header);
        AppendRow(csv, Columns.Select(Quote));
        foreach (var line in lines)
        {
            if (line.Amount < 0m || line.Direction is not (PostingDirection.Debit or PostingDirection.Credit))
            {
                throw new ArgumentException("Ungültige Buchungszeile.", nameof(lines));
            }

            var row = new string[Columns.Count];
            // Decimal throughout; DATEV amounts have two fractional digits and no grouping.
            row[0] = decimal.Round(line.Amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", German);
            row[1] = Quote(line.Direction == PostingDirection.Debit ? "S" : "H");
            row[2] = Quote("EUR");
            row[6] = line.Account;
            row[7] = line.CounterAccount;
            row[8] = line.TaxKey is null or Steuerschluessel.None
                ? string.Empty : ((int)line.TaxKey.Value).ToString(CultureInfo.InvariantCulture);
            row[9] = line.EntryDate.ToString("ddMM", CultureInfo.InvariantCulture);
            row[10] = Quote(line.DocumentNumber);
            row[13] = Quote(line.Description);
            AppendRow(csv, row);
        }

        return Windows1252.GetBytes(csv.ToString());
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string> fields) =>
        csv.AppendJoin(';', fields).Append("\r\n");

    private static string Quote(string value) =>
        $"\"{value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static List<string> BuildColumns()
    {
        var columns = new List<string>
        {
            "Umsatz (ohne Soll/Haben-Kz)", "Soll/Haben-Kennzeichen", "WKZ Umsatz", "Kurs",
            "Basis-Umsatz", "WKZ Basis-Umsatz", "Konto", "Gegenkonto (ohne BU-Schlüssel)",
            "BU-Schlüssel", "Belegdatum", "Belegfeld 1", "Belegfeld 2", "Skonto", "Buchungstext",
            "Postensperre", "Diverse Adressnummer", "Geschäftspartnerbank", "Sachverhalt",
            "Zinssperre", "Beleglink",
        };
        for (var i = 1; i <= 8; i++)
        {
            columns.Add($"Beleginfo - Art {i}");
            columns.Add($"Beleginfo - Inhalt {i}");
        }

        columns.AddRange([
            "KOST1 - Kostenstelle", "KOST2 - Kostenstelle", "Kost-Menge", "EU-Land u. UStID",
            "EU-Steuersatz", "Abw. Versteuerungsart", "Sachverhalt L+L", "Funktionsergänzung L+L",
            "BU 49 Hauptfunktionstyp", "BU 49 Hauptfunktionsnummer", "BU 49 Funktionsergänzung",
        ]);
        for (var i = 1; i <= 20; i++)
        {
            columns.Add($"Zusatzinformation - Art {i}");
            columns.Add($"Zusatzinformation - Inhalt {i}");
        }

        columns.AddRange([
            "Stück", "Gewicht", "Zahlweise", "Forderungsart", "Veranlagungsjahr",
            "Zugeordnete Fälligkeit", "Skontotyp", "Auftragsnummer", "Buchungstyp",
            "USt-Schlüssel (Anzahlungen)", "EU-Land (Anzahlungen)", "Sachverhalt L+L (Anzahlungen)",
            "EU-Steuersatz (Anzahlungen)", "Erlöskonto (Anzahlungen)", "Herkunft-Kz", "Leerfeld",
            "KOST-Datum", "SEPA-Mandatsreferenz", "Skontosperre", "Gesellschaftername",
            "Beteiligtennummer", "Identifikationsnummer", "Zeichnernummer", "Postensperre bis",
            "Bezeichnung SoBil-Sachverhalt", "Kennzeichen SoBil-Buchung", "Festschreibung",
            "Leistungsdatum", "Datum Zuord. Steuerperiode",
        ]);
        return columns;
    }
}
