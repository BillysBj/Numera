namespace Numera.Api.Auth;

/// <summary>Stable employee area keys and the server's segment-aware route map.</summary>
public static class AreaPermissions
{
    public const string Documents = nameof(Documents);
    public const string OpenItems = nameof(OpenItems);
    public const string Dunning = nameof(Dunning);
    public const string Recurring = nameof(Recurring);
    public const string Inbound = nameof(Inbound);
    public const string Banking = nameof(Banking);
    public const string Partners = nameof(Partners);
    public const string Catalog = nameof(Catalog);
    public const string Reports = nameof(Reports);

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly<string>(
        [Documents, OpenItems, Dunning, Recurring, Inbound, Banking, Partners, Catalog, Reports]);

    private static readonly (string Prefix, string Area)[] Routes =
    [
        ("/api/documents", Documents),
        ("/api/open-items", OpenItems), ("/api/payments", OpenItems),
        ("/api/dunning", Dunning),
        ("/api/recurring-templates", Recurring),
        ("/api/receipts", Inbound), ("/api/inbound-documents", Inbound),
        ("/api/bank-accounts", Banking), ("/api/bank-transactions", Banking),
        ("/api/partners", Partners),
        ("/api/catalog-items", Catalog),
        ("/api/reports", Reports), ("/api/vat-payments", Reports),
    ];

    public static string? Resolve(PathString path) =>
        Routes.FirstOrDefault(route => path.StartsWithSegments(route.Prefix, StringComparison.OrdinalIgnoreCase)).Area;

    public static string[]? Parse(string? stored) => stored?.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
