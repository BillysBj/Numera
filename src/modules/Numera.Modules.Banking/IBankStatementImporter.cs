namespace Numera.Modules.Banking;

/// <summary>Port implemented by each supported bank-statement format parser.</summary>
public interface IBankStatementImporter
{
    bool CanImport(string fileName, string contentType);

    IAsyncEnumerable<BankTransactionDraft> ParseAsync(
        Stream content,
        CancellationToken ct);
}
