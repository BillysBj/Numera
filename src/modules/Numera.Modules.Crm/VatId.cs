using System.Text.RegularExpressions;

namespace Numera.Modules.Crm;

/// <summary>
/// Offline validation of European VAT identification numbers (USt-IdNr). This is a
/// <b>pure</b> function set — it performs format and (for Germany) checksum checks
/// only. It NEVER contacts VIES or any network service: online confirmation is a
/// best-effort, asynchronous concern and must never block a save (RESEARCH.md
/// anti-pattern: "Blocking save on VIES").
/// </summary>
public static partial class VatId
{
    // DE + 9 digits, first digit non-zero (no leading zero).
    private static readonly Regex GermanFormat = BuildGermanFormat();

    // Two-letter country prefix followed by 2..12 alphanumerics (upper-cased).
    private static readonly Regex EuFormat = BuildEuFormat();

    /// <summary>
    /// Expected length of the numeric/alphanumeric part (excluding the 2-letter
    /// country prefix) for each EU member state, used by <see cref="IsPlausibleEu"/>.
    /// A country may permit several lengths.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int[]> EuPartLengths =
        new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["AT"] = [9],            // U + 8 chars
            ["BE"] = [10],
            ["BG"] = [9, 10],
            ["CY"] = [9],
            ["CZ"] = [8, 9, 10],
            ["DE"] = [9],
            ["DK"] = [8],
            ["EE"] = [9],
            ["EL"] = [9],           // Greece uses the EL prefix for VAT
            ["ES"] = [9],
            ["FI"] = [8],
            ["FR"] = [11],
            ["HR"] = [11],
            ["HU"] = [8],
            ["IE"] = [8, 9],
            ["IT"] = [11],
            ["LT"] = [9, 12],
            ["LU"] = [8],
            ["LV"] = [11],
            ["MT"] = [8],
            ["NL"] = [12],
            ["PL"] = [10],
            ["PT"] = [9],
            ["RO"] = [2, 3, 4, 5, 6, 7, 8, 9, 10],
            ["SE"] = [12],
            ["SI"] = [8],
            ["SK"] = [10],
        };

    /// <summary>
    /// Normalises raw user input: trims, removes all internal whitespace, and
    /// upper-cases. E.g. <c>" de 811 907 980 "</c> → <c>"DE811907980"</c>.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        Span<char> buffer = raw.Length <= 64 ? stackalloc char[raw.Length] : new char[raw.Length];
        var n = 0;
        foreach (var c in raw)
        {
            if (!char.IsWhiteSpace(c))
            {
                buffer[n++] = char.ToUpperInvariant(c);
            }
        }

        return new string(buffer[..n]);
    }

    /// <summary>
    /// True if <paramref name="raw"/> is a valid German USt-IdNr: <c>DE</c> + 9
    /// digits (no leading zero) with a correct ISO 7064 MOD 11,10 check digit.
    /// </summary>
    public static bool IsValidDe(string? raw)
    {
        var value = Normalize(raw);
        if (!GermanFormat.IsMatch(value))
        {
            return false;
        }

        // The 9 digits follow the "DE" prefix; the 9th is the check digit.
        ReadOnlySpan<char> digits = value.AsSpan(2);
        return HasValidDeCheckDigit(digits);
    }

    /// <summary>
    /// True if <paramref name="raw"/> is <i>plausibly</i> an EU VAT ID: a known
    /// two-letter member-state prefix followed by a body of the expected length.
    /// This is a shape check only — no per-country checksum (except that
    /// <see cref="IsValidDe"/> exists for Germany). Use for non-DE partners where a
    /// full checksum is out of scope.
    /// </summary>
    public static bool IsPlausibleEu(string? raw)
    {
        var value = Normalize(raw);
        if (!EuFormat.IsMatch(value))
        {
            return false;
        }

        var country = value[..2];
        if (!EuPartLengths.TryGetValue(country, out var lengths))
        {
            return false;
        }

        var bodyLength = value.Length - 2;
        return Array.IndexOf(lengths, bodyLength) >= 0;
    }

    /// <summary>
    /// ISO 7064 MOD 11,10 check over the first 8 digits, verifying the 9th is the
    /// correct check digit — the German USt-IdNr Prüfziffer algorithm.
    /// </summary>
    private static bool HasValidDeCheckDigit(ReadOnlySpan<char> nineDigits)
    {
        var product = 10;
        for (var i = 0; i < 8; i++)
        {
            var digit = nineDigits[i] - '0';
            var sum = (digit + product) % 10;
            if (sum == 0)
            {
                sum = 10;
            }

            product = (2 * sum) % 11;
        }

        var check = 11 - product;
        if (check == 10)
        {
            check = 0;
        }

        return check == nineDigits[8] - '0';
    }

    [GeneratedRegex(@"^DE[1-9]\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildGermanFormat();

    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{2,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildEuFormat();
}
