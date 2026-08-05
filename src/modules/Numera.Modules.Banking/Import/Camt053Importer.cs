using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Numera.Modules.Banking.Import;

/// <summary>Parses namespace-version-independent ISO 20022 camt.053 statements.</summary>
public sealed class Camt053Importer : IBankStatementImporter
{
    public bool CanImport(string fileName, string contentType) =>
        string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase)
        || HasContentType(contentType, "application/xml")
        || HasContentType(contentType, "text/xml");

    public async IAsyncEnumerable<BankTransactionDraft> ParseAsync(
        Stream content,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = await XDocument.LoadAsync(content, LoadOptions.None, ct).ConfigureAwait(false);
        if (document.Root is null
            || !HasLocalName(document.Root, "Document")
            || !document.Descendants().Any(element => HasLocalName(element, "BkToCstmrStmt")))
        {
            throw new FormatException("Die XML-Datei ist kein CAMT.053-Kontoauszug.");
        }

        var statements = document.Descendants().Where(element => HasLocalName(element, "Stmt"));
        var entries = statements.SelectMany(statement =>
            statement.Descendants().Where(element => HasLocalName(element, "Ntry")));

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return ParseEntry(entry);
        }
    }

    private static BankTransactionDraft ParseEntry(XElement entry)
    {
        var indicator = ChildValue(entry, "CdtDbtInd")?.ToUpperInvariant();
        var amountElement = Child(entry, "Amt")
            ?? throw new FormatException("Ein CAMT.053-Eintrag enthält keinen Betrag.");
        if (!decimal.TryParse(
                amountElement.Value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var unsignedAmount))
        {
            throw new FormatException($"Ungültiger CAMT.053-Betrag: '{amountElement.Value}'.");
        }

        var amount = indicator switch
        {
            "CRDT" => decimal.Abs(unsignedAmount),
            "DBIT" => -decimal.Abs(unsignedAmount),
            _ => throw new FormatException("Ein CAMT.053-Eintrag enthält keine gültige CdtDbtInd-Angabe."),
        };

        var valueDate = ReadDate(entry, "ValDt")
            ?? throw new FormatException("Ein CAMT.053-Eintrag enthält kein gültiges ValDt/Dt.");
        var bookingDate = ReadDate(entry, "BookgDt");
        var relatedParties = entry.Descendants().FirstOrDefault(element => HasLocalName(element, "RltdPties"));
        var isCredit = indicator == "CRDT";
        var counterpartyName = ReadCounterpartyName(relatedParties, isCredit ? "Dbtr" : "Cdtr");
        var counterpartyIban = ReadCounterpartyIban(relatedParties, isCredit ? "DbtrAcct" : "CdtrAcct");

        var purposes = entry.Descendants()
            .Where(element => HasLocalName(element, "Ustrd"))
            .Select(element => NullIfWhiteSpace(element.Value))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var purpose = purposes.Length > 0
            ? string.Join(" ", purposes)
            : DescendantValue(entry, "AddtlNtryInf");

        return new BankTransactionDraft(
            DescendantValue(entry, "AcctSvcrRef") ?? ChildValue(entry, "NtryRef"),
            amount,
            valueDate,
            bookingDate,
            purpose,
            counterpartyName,
            counterpartyIban,
            DescendantValue(entry, "EndToEndId"),
            BankTransactionSource.Camt);
    }

    private static string? ReadCounterpartyName(XElement? relatedParties, string partyElementName)
    {
        if (relatedParties is null)
        {
            return null;
        }

        var party = Child(relatedParties, partyElementName);
        return party is null
            ? DescendantValue(relatedParties, "Nm")
            : DescendantValue(party, "Nm");
    }

    private static string? ReadCounterpartyIban(XElement? relatedParties, string accountElementName)
    {
        if (relatedParties is null)
        {
            return null;
        }

        var account = Child(relatedParties, accountElementName);
        var iban = account is null
            ? DescendantValue(relatedParties, "IBAN")
            : DescendantValue(account, "IBAN");
        return iban is null
            ? null
            : string.Concat(iban.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
    }

    private static DateOnly? ReadDate(XElement entry, string wrapperName)
    {
        var wrapper = Child(entry, wrapperName);
        var text = wrapper is null
            ? null
            : DescendantValue(wrapper, "Dt") ?? DescendantValue(wrapper, "DtTm");
        if (text is null)
        {
            return null;
        }

        var dateText = text.Length >= 10 ? text[..10] : text;
        return DateOnly.TryParseExact(
            dateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => HasLocalName(element, localName));

    private static string? ChildValue(XElement parent, string localName) =>
        NullIfWhiteSpace(Child(parent, localName)?.Value);

    private static string? DescendantValue(XElement parent, string localName) =>
        NullIfWhiteSpace(parent.Descendants().FirstOrDefault(element => HasLocalName(element, localName))?.Value);

    private static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static bool HasContentType(string contentType, string expected) =>
        contentType.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
        && (contentType.Length == expected.Length || contentType[expected.Length] is ';' or ' ');

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
