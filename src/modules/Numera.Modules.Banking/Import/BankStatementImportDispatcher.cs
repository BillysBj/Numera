namespace Numera.Modules.Banking.Import;

/// <summary>Selects the first importer that supports the supplied statement metadata.</summary>
public sealed class BankStatementImportDispatcher(IEnumerable<IBankStatementImporter> importers)
{
    private readonly IReadOnlyList<IBankStatementImporter> _importers =
        (importers ?? throw new ArgumentNullException(nameof(importers))).ToArray();

    public async Task<IReadOnlyList<BankTransactionDraft>> ImportAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(contentType);

        var importer = _importers.FirstOrDefault(candidate => candidate.CanImport(fileName, contentType))
            ?? throw new ArgumentException("Nicht unterstütztes Kontoauszugsformat", nameof(fileName));
        var drafts = new List<BankTransactionDraft>();
        await foreach (var draft in importer.ParseAsync(content, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            drafts.Add(draft);
        }

        return drafts;
    }
}
