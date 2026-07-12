using Microsoft.EntityFrameworkCore;

namespace Numera.Modules.Sales;

/// <summary>
/// A postal address embedded (EF <b>owned type</b>) in a <see cref="CompanyProfile"/>
/// row. Covers the EN 16931 seller postal-address group (BG-5: BT-35 street, BT-36
/// additional line, BT-37 city, BT-38 post code, BT-40 country code).
/// </summary>
/// <remarks>
/// <para>
/// This is a LOCAL owned value type deliberately duplicated inside the Sales module
/// rather than shared from <c>Numera.Modules.Crm</c>: modules must not reference each
/// other (RESEARCH.md Q4). Owned means the columns live in the <c>company_profile</c>
/// row — no separate table, no extra RLS policy, no join.
/// </para>
/// <para>
/// The reflective snake-case naming in <c>NumeraDbContext</c> maps these to
/// navigation-prefixed columns (e.g. <c>address_street</c>), so no configuration is
/// needed here.
/// </para>
/// </remarks>
[Owned]
public sealed class Address
{
    /// <summary>Street and house number (BT-35).</summary>
    public required string Street { get; set; }

    /// <summary>Additional address line (BT-36), e.g. c/o or building.</summary>
    public string? Line2 { get; set; }

    /// <summary>Postal / ZIP code (BT-38).</summary>
    public required string PostalCode { get; set; }

    /// <summary>City / town (BT-37).</summary>
    public required string City { get; set; }

    /// <summary>
    /// Country as an ISO 3166-1 alpha-2 <b>code</b> (BT-40), e.g. <c>DE</c> — never a
    /// country name. Defaults to Germany.
    /// </summary>
    public string CountryCode { get; set; } = "DE";

    /// <summary>Optional PO box (Postfach) when post is not delivered to the street address.</summary>
    public string? PoBox { get; set; }
}
