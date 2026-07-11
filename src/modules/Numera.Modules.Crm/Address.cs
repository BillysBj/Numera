using Microsoft.EntityFrameworkCore;

namespace Numera.Modules.Crm;

/// <summary>
/// A postal address embedded (EF <b>owned type</b>) in a <see cref="BusinessPartner"/>
/// row. Covers the EN 16931 buyer postal-address group (BG-8: BT-50 street, BT-51
/// additional line, BT-52 city, BT-53 post code, BT-55 country code).
/// </summary>
/// <remarks>
/// Owned means the columns live in the <c>partners</c> row (no separate table, no
/// extra RLS policy, no join). A partner can carry a billing address (required) and
/// an optional deviating shipping address (Lieferanschrift) — two owned instances of
/// this same type, distinguished by EF via the navigation-name column prefix.
/// </remarks>
[Owned]
public sealed class Address
{
    /// <summary>Street and house number (BT-50).</summary>
    public required string Street { get; set; }

    /// <summary>Additional address line (BT-51), e.g. c/o or building.</summary>
    public string? Line2 { get; set; }

    /// <summary>Postal / ZIP code (BT-53).</summary>
    public required string PostalCode { get; set; }

    /// <summary>City / town (BT-52).</summary>
    public required string City { get; set; }

    /// <summary>
    /// Country as an ISO 3166-1 alpha-2 <b>code</b> (BT-55), e.g. <c>DE</c> — never a
    /// country name. Defaults to Germany.
    /// </summary>
    public string CountryCode { get; set; } = "DE";

    /// <summary>Optional PO box (Postfach) when post is not delivered to the street address.</summary>
    public string? PoBox { get; set; }
}
