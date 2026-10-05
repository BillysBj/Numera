using System.ComponentModel.DataAnnotations.Schema;

using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

public enum AfaMethode
{
    Linear = 0,
    GwgSofort = 1,
    // TODO: Degressive AfA, Sonderabschreibungen and GWG-Sammelposten/Pool.
}

public enum AssetDisposal
{
    None = 0,
    Verkauf = 1,
    Verschrottung = 2,
    Entnahme = 3,
}

/// <summary>Tenant-owned fixed-asset master data; acquisition value excludes VAT.</summary>
[Table("fixed_assets")]
public sealed class FixedAsset : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public required string Bezeichnung { get; set; }
    public string? Lieferant { get; set; }
    public string? BelegRef { get; set; }
    public DateOnly? Rechnungsdatum { get; set; }
    public DateOnly AnschaffungsDatum { get; set; }
    public DateOnly InbetriebnahmeDatum { get; set; }
    [Column(TypeName = "numeric(19,4)")]
    public decimal AnschaffungskostenNetto { get; set; }
    [Column(TypeName = "numeric(19,4)")]
    public decimal AnschaffungsnebenkostenNetto { get; set; }
    public required string AnlagekontoNumber { get; set; }
    public required string AbschreibungskontoNumber { get; set; }
    public int NutzungsdauerJahre { get; set; }
    public AfaMethode Methode { get; set; }
    public DateOnly? AbgangsDatum { get; set; }
    public AssetDisposal AbgangsArt { get; set; }
    [NotMapped]
    public decimal Anschaffungswert => AnschaffungskostenNetto + AnschaffungsnebenkostenNetto;
}
