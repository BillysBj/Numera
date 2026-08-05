using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Numera.Modules.Banking.Import;

/// <summary>
/// Parses UTF-8 CSV with the defined header
/// <c>date,amount,purpose,counterparty_name,counterparty_iban</c>. Fields follow RFC 4180 quoting;
/// German decimal-comma amounts therefore need quotes in comma-delimited files. Semicolon-delimited
/// German exports and ISO (<c>yyyy-MM-dd</c>) or German (<c>dd.MM.yyyy</c>) dates are also accepted.
/// </summary>
public sealed class CsvImporter : IBankStatementImporter
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yy"];

    public bool CanImport(string fileName, string contentType) =>
        string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase)
        || HasContentType(contentType, "text/csv")
        || HasContentType(contentType, "application/csv");

    public async IAsyncEnumerable<BankTransactionDraft> ParseAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var reader = new StreamReader(content, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var delimiter = DetectDelimiter(text);
        var rowNumber = 0;

        foreach (var row in ParseRows(text, delimiter))
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (rowNumber == 1 && IsHeader(row))
            {
                continue;
            }

            if (row.Length != 5)
            {
                throw new FormatException(
                    $"CSV-Zeile {rowNumber} hat {row.Length} statt der erwarteten 5 Spalten.");
            }

            if (!TryParseDate(row[0], out var valueDate))
            {
                throw new FormatException($"Ungültiges CSV-Datum in Zeile {rowNumber}: '{row[0]}'.");
            }

            if (!TryParseAmount(row[1], out var amount))
            {
                throw new FormatException($"Ungültiger CSV-Betrag in Zeile {rowNumber}: '{row[1]}'.");
            }

            yield return new BankTransactionDraft(
                null,
                amount,
                valueDate,
                null,
                NullIfWhiteSpace(row[2]),
                NullIfWhiteSpace(row[3]),
                NormalizeIban(row[4]),
                null,
                BankTransactionSource.Csv);
        }
    }

    private static IEnumerable<string[]> ParseRows(string text, char delimiter)
    {
        var row = new List<string>(5);
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == delimiter && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !inQuotes)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString());
                field.Clear();
                yield return row.ToArray();
                row.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new FormatException("Die CSV-Datei enthält ein nicht geschlossenes Anführungszeichen.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row.ToArray();
        }
    }

    private static char DetectDelimiter(string text)
    {
        var commaCount = 0;
        var semicolonCount = 0;
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == ',')
            {
                commaCount++;
            }
            else if (!inQuotes && character == ';')
            {
                semicolonCount++;
            }
            else if (!inQuotes && character is '\r' or '\n')
            {
                break;
            }
        }

        return semicolonCount > commaCount ? ';' : ',';
    }

    private static bool IsHeader(string[] row) =>
        row.Length == 5
        && NormalizeHeader(row[0]) == "date"
        && NormalizeHeader(row[1]) == "amount"
        && NormalizeHeader(row[2]) == "purpose"
        && NormalizeHeader(row[3]) == "counterpartyname"
        && NormalizeHeader(row[4]) == "counterpartyiban";

    private static string NormalizeHeader(string value) =>
        string.Concat(value.Trim().TrimStart('\uFEFF').Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = value.Trim().Replace("\u00A0", string.Empty, StringComparison.Ordinal);
        var commaIndex = normalized.LastIndexOf(',');
        var dotIndex = normalized.LastIndexOf('.');
        var culture = commaIndex > dotIndex ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.InvariantCulture;
        return decimal.TryParse(normalized, NumberStyles.Number, culture, out amount);
    }

    private static string? NormalizeIban(string value)
    {
        var iban = string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
        return iban.Length == 0 ? null : iban;
    }

    private static bool HasContentType(string contentType, string expected) =>
        contentType.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
        && (contentType.Length == expected.Length || contentType[expected.Length] is ';' or ' ');

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
