using Numera.Modules.Ledger;

namespace Numera.Api.Contracts;

public sealed record FixedAssetRequest(
    string Bezeichnung,
    DateOnly AnschaffungsDatum,
    DateOnly InbetriebnahmeDatum,
    decimal AnschaffungskostenNetto,
    string AnlagekontoNumber,
    int NutzungsdauerJahre,
    decimal AnschaffungsnebenkostenNetto = 0m,
    string? AbschreibungskontoNumber = null,
    AfaMethode Methode = AfaMethode.Linear,
    string? Lieferant = null,
    string? BelegRef = null,
    DateOnly? Rechnungsdatum = null,
    DateOnly? AbgangsDatum = null,
    AssetDisposal AbgangsArt = AssetDisposal.None);

public sealed record AfaRunSummary(int BookedCount, int SkippedCount, decimal Total);
