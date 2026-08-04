using Numera.Modules.Sales;

namespace Numera.Api.Reporting.Elster;

/// <summary>Converts German Landes-format Steuernummern to ELSTER's 13-digit format.</summary>
public static class SteuernummerConverter
{
    /// <summary>
    /// Converts a local Steuernummer to the nationwide
    /// <c>FFFF0BBBUUUUP</c>-style layout used by ELSTER.
    /// </summary>
    /// <remarks>
    /// The first four output digits are the Bundesfinanzamtsnummer: a state prefix
    /// plus the local Finanzamt digits. Nordrhein-Westfalen retains its four-digit
    /// Bezirks-/Unterscheidungs block; Hessen commonly prints a leading zero before
    /// its two Finanzamt digits, which is accepted and removed.
    /// </remarks>
    public static string Convert(string? taxNumber, Bundesland? bundesland)
    {
        if (string.IsNullOrWhiteSpace(taxNumber))
        {
            throw new ArgumentException(
                "A local Steuernummer is required; a USt-IdNr/VatId cannot be used instead.",
                nameof(taxNumber));
        }

        var trimmed = taxNumber.Trim();
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && char.IsLetter(trimmed[1]))
        {
            throw new ArgumentException(
                "The value looks like a USt-IdNr/VatId. ELSTER requires the local Steuernummer.",
                nameof(taxNumber));
        }

        if (bundesland is null || !Enum.IsDefined(bundesland.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bundesland),
                bundesland,
                "A supported German Bundesland is required to convert the local Steuernummer.");
        }

        if (trimmed.Any(character => !IsAsciiDigit(character) && !IsSeparator(character)))
        {
            throw new ArgumentException(
                "The Steuernummer may contain only digits, whitespace, '/', '-' or '.'.",
                nameof(taxNumber));
        }

        var digits = string.Concat(trimmed.Where(IsAsciiDigit));
        var format = FormatFor(bundesland.Value);

        if (format.AcceptLeadingOfficeZero && digits.Length == format.LocalDigitCount + 1 && digits[0] == '0')
        {
            digits = digits[1..];
        }

        if (digits.Length != format.LocalDigitCount)
        {
            throw new ArgumentException(
                $"The Steuernummer has an unsupported shape for {bundesland.Value}; " +
                $"expected {format.LocalDigitCount} digits in the Landes-format.",
                nameof(taxNumber));
        }

        var offset = 0;
        var office = digits.Substring(offset, format.OfficeDigits);
        offset += format.OfficeDigits;
        var district = digits.Substring(offset, format.DistrictDigits);
        offset += format.DistrictDigits;
        var identifierAndCheckDigit = digits[offset..];

        var converted = string.Concat(
            format.FederalPrefix,
            office,
            "0",
            district,
            identifierAndCheckDigit);

        if (converted.Length != 13)
        {
            throw new InvalidOperationException("The configured Steuernummer format did not produce 13 digits.");
        }

        return converted;
    }

    private static StateFormat FormatFor(Bundesland bundesland) => bundesland switch
    {
        Bundesland.BadenWuerttemberg => TwoDigitOffice("28"),
        Bundesland.Bayern => ThreeDigitOffice("9"),
        Bundesland.Berlin => TwoDigitOffice("11"),
        Bundesland.Brandenburg => ThreeDigitOffice("3"),
        Bundesland.Bremen => TwoDigitOffice("24"),
        Bundesland.Hamburg => TwoDigitOffice("22"),
        Bundesland.Hessen => TwoDigitOffice("26", acceptLeadingOfficeZero: true),
        Bundesland.MecklenburgVorpommern => ThreeDigitOffice("4"),
        Bundesland.Niedersachsen => TwoDigitOffice("23"),
        Bundesland.NordrheinWestfalen => new("5", 3, 4, 4, AcceptLeadingOfficeZero: false),
        Bundesland.RheinlandPfalz => TwoDigitOffice("27"),
        Bundesland.Saarland => ThreeDigitOffice("1"),
        Bundesland.Sachsen => ThreeDigitOffice("3"),
        Bundesland.SachsenAnhalt => ThreeDigitOffice("3"),
        Bundesland.SchleswigHolstein => TwoDigitOffice("21"),
        Bundesland.Thueringen => ThreeDigitOffice("4"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(bundesland),
            bundesland,
            "The Bundesland is not supported for Steuernummer conversion."),
    };

    private static StateFormat TwoDigitOffice(string prefix, bool acceptLeadingOfficeZero = false) =>
        new(prefix, 2, 3, 5, acceptLeadingOfficeZero);

    private static StateFormat ThreeDigitOffice(string prefix) =>
        new(prefix, 3, 3, 5, AcceptLeadingOfficeZero: false);

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';

    private static bool IsSeparator(char character) =>
        char.IsWhiteSpace(character) || character is '/' or '-' or '.';

    private sealed record StateFormat(
        string FederalPrefix,
        int OfficeDigits,
        int DistrictDigits,
        int IdentifierAndCheckDigitDigits,
        bool AcceptLeadingOfficeZero)
    {
        public int LocalDigitCount => OfficeDigits + DistrictDigits + IdentifierAndCheckDigitDigits;
    }
}
