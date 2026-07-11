namespace Numera.Modules.Catalog;

/// <summary>
/// Whether a <see cref="CatalogItem"/> is a physical product (Ware) or a service
/// (Dienstleistung). Drives the default unit of measure (Stück vs. Stunde) and,
/// later, revenue-account and tax defaults.
/// </summary>
public enum CatalogItemKind
{
    /// <summary>A physical product / good (Ware) — defaults to the piece unit C62.</summary>
    Product = 1,

    /// <summary>A service (Dienstleistung) — defaults to the hour unit HUR.</summary>
    Service = 2,
}
