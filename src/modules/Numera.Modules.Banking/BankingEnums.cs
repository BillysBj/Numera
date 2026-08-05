namespace Numera.Modules.Banking;

/// <summary>Bank-connection implementation used for a tenant connection.</summary>
public enum BankProvider
{
    Stub,
    FinApi,
}

/// <summary>PSD2 consent lifecycle state.</summary>
public enum ConsentStatus
{
    Pending,
    Active,
    Expired,
    Revoked,
}

/// <summary>Origin of a normalized bank transaction.</summary>
public enum BankTransactionSource
{
    FinApi,
    Csv,
    Mt940,
    Camt,
}

/// <summary>Human-controlled reconciliation state.</summary>
public enum MatchStatus
{
    Unmatched,
    Suggested,
    Review,
    Confirmed,
    Ignored,
}
