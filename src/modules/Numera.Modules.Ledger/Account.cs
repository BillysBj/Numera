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
/// Part of the <b>inert</b> ledger schema present from day one (ARCHITECTURE.md:
/// "ledger present from day one, even if inert"). This entity is a stable schema
/// seam only — there is no posting or numbering logic in Phase 1.
/// </remarks>
public sealed class Account : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>
    /// Account number, SKR03/SKR04-ready. Modelled as a plain string column, NOT a
    /// Postgres <c>SEQUENCE</c>/<c>SERIAL</c> — gapless legal numbering is Phase 3
    /// (RESEARCH.md anti-pattern: never lean on DB sequences for legal numbers).
    /// </summary>
    public required string Number { get; set; }

    /// <summary>Human-readable account name (Kontenbezeichnung).</summary>
    public required string Name { get; set; }

    /// <summary>The account's double-entry classification.</summary>
    public AccountType Type { get; set; }
}
