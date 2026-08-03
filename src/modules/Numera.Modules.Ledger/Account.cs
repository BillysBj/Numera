using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>The classification of a ledger account in a double-entry system.</summary>
public enum AccountType
{
    /// <summary>Aktiva.</summary>
    Asset = 1,

    /// <summary>Passiva / Fremdkapital.</summary>
    Liability = 2,

    /// <summary>Eigenkapital.</summary>
    Equity = 3,

    /// <summary>Ertrag.</summary>
    Revenue = 4,

    /// <summary>Aufwand.</summary>
    Expense = 5,
}

/// <summary>
/// A single account in the tenant's chart of accounts (Kontenrahmen).
/// </summary>
/// <remarks>
/// Mutable master data for the tenant's SKR03 or SKR04 chart. Posted journal
/// entries and posting legs remain immutable independently of later account edits.
/// </remarks>
public sealed class Account : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>
    /// Account number, SKR03/SKR04-ready. Modelled as a plain string column, NOT a
    /// Postgres <c>SEQUENCE</c>/<c>SERIAL</c>; legal numbering never uses DB sequences.
    /// </summary>
    public required string Number { get; set; }

    /// <summary>Human-readable account name (Kontenbezeichnung).</summary>
    public required string Name { get; set; }

    /// <summary>The account's double-entry classification.</summary>
    public AccountType Type { get; set; }

    /// <summary>The chart of accounts this account belongs to.</summary>
    public ChartVariant ChartVariant { get; set; } = ChartVariant.Skr03;

    /// <summary>The DATEV BU tax key implied by this account, when applicable.</summary>
    public Steuerschluessel? Steuerschluessel { get; set; }

    /// <summary>Whether the account automatically derives VAT postings.</summary>
    public bool IsAutomatikkonto { get; set; }

    /// <summary>Optional Umsatzsteuer-Voranmeldung Kennziffer.</summary>
    public string? UstvaKennziffer { get; set; }

    /// <summary>Whether the account is available for new postings.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Optional parent account number used to group the chart.</summary>
    public string? ParentNumber { get; set; }
}
