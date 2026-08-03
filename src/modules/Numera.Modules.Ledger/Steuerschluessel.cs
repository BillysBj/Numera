namespace Numera.Modules.Ledger;

/// <summary>DATEV BU-Schlüssel captured on accounts and posting legs.</summary>
public enum Steuerschluessel
{
    /// <summary>Kein Steuerschlüssel / steuerfrei ohne Vorsteuerabzug.</summary>
    None = 0,

    /// <summary>Steuerfrei mit Vorsteuerabzug (0%).</summary>
    TaxFreeWithInput = 1,

    /// <summary>Umsatzsteuer 7%.</summary>
    Ust7 = 2,

    /// <summary>Umsatzsteuer 19%.</summary>
    Ust19 = 3,

    /// <summary>Vorsteuer 7%.</summary>
    Vst7 = 8,

    /// <summary>Vorsteuer 19%.</summary>
    Vst19 = 9,

    /// <summary>Reverse-Charge ohne Steuer.</summary>
    ReverseChargeNoTax = 20,

    /// <summary>Reverse-Charge-Umsatzsteuer 7% (§13b UStG).</summary>
    ReverseChargeUst7 = 22,

    /// <summary>Reverse-Charge-Umsatzsteuer 19% (§13b UStG).</summary>
    ReverseChargeUst19 = 23,

    /// <summary>Reverse-Charge-Vorsteuer 7%.</summary>
    ReverseChargeVst7 = 28,

    /// <summary>Reverse-Charge-Vorsteuer 19%.</summary>
    ReverseChargeVst19 = 29,

    /// <summary>Aufhebung der Automatik am Automatikkonto.</summary>
    AutomatikAufhebung = 40,

    /// <summary>Innergemeinschaftlicher Erwerb (Reverse-Charge).</summary>
    InnergemeinschaftlicherErwerb = 42,
}

/// <summary>Supported DATEV chart-of-accounts variants.</summary>
public enum ChartVariant
{
    /// <summary>Standardkontenrahmen 03.</summary>
    Skr03 = 3,

    /// <summary>Standardkontenrahmen 04.</summary>
    Skr04 = 4,
}
