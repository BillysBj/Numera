using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Numera.Modules.Banking.Import;

/// <summary>Parses the common MT940 :61: transaction and best-effort :86: detail fields.</summary>
public sealed partial class Mt940Importer : IBankStatementImporter
{
    public bool CanImport(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".sta", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mt940", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || HasContentType(contentType, "application/mt940")
            || HasContentType(contentType, "application/x-mt940");
    }

    public async IAsyncEnumerable<BankTransactionDraft> ParseAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        var text = await ReadTextAsync(content, ct).ConfigureAwait(false);
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        PendingTransaction? pending = null;
        var foundTransaction = false;

        while (reader.ReadLine() is { } rawLine)
        {
            ct.ThrowIfCancellationRequested();
            var line = rawLine.TrimEnd();
            if (line.StartsWith(":61:", StringComparison.Ordinal))
            {
                if (pending is not null)
                {
                    yield return CreateDraft(pending);
                }

                pending = ParseTransactionLine(line[4..]);
                foundTransaction = true;
            }
            else if (pending is not null && line.StartsWith(":86:", StringComparison.Ordinal))
            {
                pending.Details.Append(line.AsSpan(4));
                pending.ReadingDetails = true;
            }
            else if (pending is not null && pending.ReadingDetails && !line.StartsWith(':'))
            {
                pending.Details.Append(' ').Append(line.Trim());
            }
            else if (pending is not null && line.StartsWith(':'))
            {
                pending.ReadingDetails = false;
            }
        }

        if (pending is not null)
        {
            yield return CreateDraft(pending);
        }

        if (!foundTransaction)
        {
            throw new FormatException("Die Datei enthält keine MT940-Transaktion (:61:).");
        }
    }

    private static PendingTransaction ParseTransactionLine(string line)
    {
        var match = TransactionLineRegex().Match(line);
        if (!match.Success)
        {
            throw new FormatException($"Ungültige MT940-Transaktionszeile: ':61:{line}'.");
        }

        var dateText = match.Groups["valueDate"].Value;
        var twoDigitYear = int.Parse(dateText.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var year = twoDigitYear >= 70 ? 1900 + twoDigitYear : 2000 + twoDigitYear;
        if (!DateOnly.TryParseExact(
                $"{year:D4}{dateText[2..]}",
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var valueDate))
        {
            throw new FormatException($"Ungültiges MT940-Valutadatum: '{dateText}'.");
        }

        var amountText = match.Groups["amount"].Value.Replace(',', '.');
        if (!decimal.TryParse(
                amountText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var unsignedAmount))
        {
            throw new FormatException($"Ungültiger MT940-Betrag: '{match.Groups["amount"].Value}'.");
        }

        var mark = match.Groups["mark"].Value;
        var amount = mark is "C" or "RC"
            ? decimal.Abs(unsignedAmount)
            : -decimal.Abs(unsignedAmount);
        return new PendingTransaction(valueDate, amount);
    }

    private static BankTransactionDraft CreateDraft(PendingTransaction transaction)
    {
        var details = ParseDetails(transaction.Details.ToString());
        return new BankTransactionDraft(
            null,
            transaction.Amount,
            transaction.ValueDate,
            null,
            details.Purpose,
            details.CounterpartyName,
            details.CounterpartyIban,
            null,
            BankTransactionSource.Mt940);
    }

    private static TransactionDetails ParseDetails(string rawDetails)
    {
        var details = rawDetails.Trim();
        if (details.Length == 0)
        {
            return new TransactionDetails(null, null, null);
        }

        var fields = DetailFieldRegex().Matches(details)
            .Select(match => new DetailField(match.Groups["code"].Value, match.Groups["value"].Value.Trim()))
            .ToArray();
        var purposeValues = fields
            .Where(field => int.TryParse(field.Code, CultureInfo.InvariantCulture, out var code)
                && code is >= 20 and <= 29)
            .Select(field => RemoveLabel(field.Value, "SVWZ+"))
            .Where(value => value.Length > 0)
            .ToArray();
        var purpose = purposeValues.Length > 0
            ? string.Join(" ", purposeValues)
            : NullIfWhiteSpace(fields.Length == 0 ? details : details[..details.IndexOf('?', StringComparison.Ordinal)]);

        var counterpartyNameValues = fields
            .Where(field => field.Code is "32" or "33")
            .Select(field => field.Value)
            .Where(value => value.Length > 0)
            .ToArray();
        var counterpartyName = counterpartyNameValues.Length > 0
            ? string.Join(" ", counterpartyNameValues)
            : ReadLabeledValue(details, "NAME+");

        var counterpartyIban = fields
            .Select(field => NormalizeIban(field.Value))
            .FirstOrDefault(iban => iban is not null)
            ?? NormalizeIban(IbanRegex().Match(details).Value);

        return new TransactionDetails(purpose, counterpartyName, counterpartyIban);
    }

    private static string? NormalizeIban(string value)
    {
        var candidate = RemoveLabel(value.Trim(), "IBAN+");
        candidate = string.Concat(candidate.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return candidate.Length is >= 15 and <= 34
            && char.IsLetter(candidate[0])
            && char.IsLetter(candidate[1])
            && char.IsDigit(candidate[2])
            && char.IsDigit(candidate[3])
            ? candidate
            : null;
    }

    private static string? ReadLabeledValue(string details, string label)
    {
        var start = details.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += label.Length;
        var end = details.IndexOf('?', start);
        return NullIfWhiteSpace(end < 0 ? details[start..] : details[start..end]);
    }

    private static string RemoveLabel(string value, string label) =>
        value.StartsWith(label, StringComparison.OrdinalIgnoreCase) ? value[label.Length..].Trim() : value;

    private static async Task<string> ReadTextAsync(Stream content, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static bool HasContentType(string contentType, string expected) =>
        contentType.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
        && (contentType.Length == expected.Length || contentType[expected.Length] is ';' or ' ');

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^(?<valueDate>\d{6})(?:\d{4})?(?<mark>RC|RD|C|D)(?:[A-Z])?(?<amount>\d+(?:[,.]\d*)?)", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionLineRegex();

    [GeneratedRegex(@"\?(?<code>\d{2})(?<value>.*?)(?=\?\d{2}|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DetailFieldRegex();

    [GeneratedRegex(@"(?<![A-Z0-9])[A-Z]{2}\d{2}(?:\s?[A-Z0-9]){11,30}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    private sealed class PendingTransaction(DateOnly valueDate, decimal amount)
    {
        public DateOnly ValueDate { get; } = valueDate;

        public decimal Amount { get; } = amount;

        public StringBuilder Details { get; } = new();

        public bool ReadingDetails { get; set; }
    }

    private sealed record DetailField(string Code, string Value);

    private sealed record TransactionDetails(string? Purpose, string? CounterpartyName, string? CounterpartyIban);
}
