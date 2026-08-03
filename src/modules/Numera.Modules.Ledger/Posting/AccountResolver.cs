using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger.Seed;
using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Ledger;

/// <summary>Resolves SKR mappings and document-level overrides to tenant account rows.</summary>
public sealed class AccountResolver(NumeraDbContext db)
{
    /// <summary>Resolves the SKR-standard revenue account; no revenue override is supported.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveRevenue(
        ChartVariant variant,
        TaxCategory taxCategory,
        decimal ratePercent) =>
        ResolveRevenue(variant, taxCategory, ratePercent, isKleinunternehmer: false);

    /// <summary>Resolves revenue while honoring the §19 Kleinunternehmer path.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveRevenue(
        ChartVariant variant,
        TaxCategory taxCategory,
        decimal ratePercent,
        bool isKleinunternehmer)
    {
        var mapping = SkrMapping.RevenueMapping(variant, taxCategory, ratePercent, isKleinunternehmer);
        return ResolveAccount(mapping.RevenueAccount, variant, mapping.Key);
    }

    /// <summary>Resolves the mapped output-tax account, or null for exempt revenue.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto)? ResolveOutputTax(
        ChartVariant variant,
        TaxCategory taxCategory,
        decimal ratePercent,
        bool isKleinunternehmer = false)
    {
        var mapping = SkrMapping.RevenueMapping(variant, taxCategory, ratePercent, isKleinunternehmer);
        return mapping.UstAccount is null
            ? null
            : ResolveAccount(mapping.UstAccount, variant, expectedKey: null);
    }

    /// <summary>Resolves the mapped expense account, honoring a supplier/document override.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveExpense(
        ChartVariant variant,
        decimal ratePercent,
        string? overrideNumber = null)
    {
        var mapping = SkrMapping.ExpenseMapping(variant, ratePercent);
        return ResolveAccount(OverrideOrDefault(overrideNumber, mapping.ExpenseAccount), variant, mapping.Key);
    }

    /// <summary>Resolves the mapped input-tax account.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveInputTax(
        ChartVariant variant,
        decimal ratePercent)
    {
        var mapping = SkrMapping.ExpenseMapping(variant, ratePercent);
        return ResolveAccount(mapping.VorsteuerAccount, variant, mapping.Key);
    }

    /// <summary>Resolves a standard debtor, creditor, bank or cash account with an optional override.</summary>
    public (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveStandard(
        ChartVariant variant,
        StandardAccountKind kind,
        string? overrideNumber = null)
    {
        var number = OverrideOrDefault(overrideNumber, SkrMapping.StandardAccount(variant, kind));
        return ResolveAccount(number, variant, expectedKey: null);
    }

    private (Guid AccountId, Steuerschluessel? Key, bool IsAutomatikkonto) ResolveAccount(
        string number,
        ChartVariant variant,
        Steuerschluessel? expectedKey)
    {
        var account = db.Set<Account>()
            .AsNoTracking()
            .SingleOrDefault(candidate =>
                candidate.Number == number &&
                candidate.ChartVariant == variant &&
                candidate.IsActive)
            ?? throw new LedgerAccountNotFoundException(number, variant);

        if (account.IsAutomatikkonto && account.Steuerschluessel != expectedKey)
        {
            throw new LedgerTaxKeyConflictException(
                number,
                variant,
                account.Steuerschluessel,
                expectedKey);
        }

        return (account.Id, expectedKey, account.IsAutomatikkonto);
    }

    private static string OverrideOrDefault(string? overrideNumber, string defaultNumber) =>
        string.IsNullOrWhiteSpace(overrideNumber) ? defaultNumber : overrideNumber.Trim();
}

/// <summary>Raised when the selected tenant chart lacks an account required by an SKR mapping.</summary>
public sealed class LedgerAccountNotFoundException(string accountNumber, ChartVariant variant)
    : InvalidOperationException(
        $"Active ledger account '{accountNumber}' for chart variant {variant} was not found in the tenant chart.");

/// <summary>Raised when a manual BU key conflicts with an Automatikkonto's implicit key.</summary>
public sealed class LedgerTaxKeyConflictException(
    string accountNumber,
    ChartVariant variant,
    Steuerschluessel? implicitKey,
    Steuerschluessel? requestedKey)
    : InvalidOperationException(
        $"Automatikkonto '{accountNumber}' ({variant}) implies tax key {implicitKey?.ToString() ?? "None"}; " +
        $"the requested key {requestedKey?.ToString() ?? "None"} conflicts with it.");
