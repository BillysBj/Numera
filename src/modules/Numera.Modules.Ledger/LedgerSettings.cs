using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>Whether VAT is recognized on invoice or payment.</summary>
public enum Besteuerungsart
{
    /// <summary>Soll-Versteuerung.</summary>
    Soll = 0,

    /// <summary>Ist-Versteuerung.</summary>
    Ist = 1,
}

/// <summary>The tenant's profit-determination method.</summary>
public enum Gewinnermittlungsart
{
    /// <summary>Einnahmenüberschussrechnung (EÜR).</summary>
    Euer = 0,

    /// <summary>Bilanzierung.</summary>
    Bilanz = 1,
}

/// <summary>
/// Per-tenant ledger configuration. Besteuerungsart and Gewinnermittlungsart are
/// captured here and consumed by the later USt-VA and reports phase.
/// </summary>
public sealed class LedgerSettings : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The tenant's selected chart of accounts.</summary>
    public ChartVariant ChartVariant { get; set; } = ChartVariant.Skr03;

    /// <summary>Whether VAT is recognized on invoice or payment.</summary>
    public Besteuerungsart Besteuerungsart { get; set; }

    /// <summary>The tenant's profit-determination method.</summary>
    public Gewinnermittlungsart Gewinnermittlungsart { get; set; }

    /// <summary>First month of the fiscal year (1-12).</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>
    /// Manual owner choice: the §5a obligation ends only on a capital increase to the GmbH
    /// minimum capital, not when share capital plus reserves reach EUR 25,000.
    /// </summary>
    // TODO: Track capital increases to the GmbH minimum capital before automating this choice.
    public bool UgRuecklagepflichtAktiv { get; set; } = true;
}
