using System.Runtime.CompilerServices;

using Numera.Modules.Banking;
using Numera.Modules.Banking.Import;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Pure parser checks for all offline bank-statement formats.</summary>
public sealed class BankStatementImporterTests
{
    [Fact]
    public async Task Camt053_importer_normalizes_credit_and_debit()
    {
        var importer = new Camt053Importer();
        Assert.True(importer.CanImport("statement.xml", "application/xml"));

        await using var content = File.OpenRead(FixturePath("sample-camt053.xml"));
        var drafts = await ParseAsync(importer, content);

        AssertDrafts(drafts, BankTransactionSource.Camt);
        Assert.Equal(new DateOnly(2026, 8, 5), drafts[0].BookingDate);
        Assert.Equal("BANK-CREDIT-1", drafts[0].ProviderId);
        Assert.Equal("EREF-CREDIT-1", drafts[0].EndToEndId);
    }

    [Fact]
    public async Task Mt940_importer_normalizes_credit_and_debit()
    {
        var importer = new Mt940Importer();
        Assert.True(importer.CanImport("statement.mt940", "application/octet-stream"));

        await using var content = File.OpenRead(FixturePath("sample.mt940"));
        var drafts = await ParseAsync(importer, content);

        AssertDrafts(drafts, BankTransactionSource.Mt940);
        Assert.All(drafts, draft => Assert.Null(draft.ProviderId));
    }

    [Fact]
    public async Task Csv_importer_normalizes_credit_and_debit_with_German_decimal_comma()
    {
        var importer = new CsvImporter();
        Assert.True(importer.CanImport("statement.csv", "text/csv; charset=utf-8"));

        await using var content = File.OpenRead(FixturePath("sample.csv"));
        var drafts = await ParseAsync(importer, content);

        AssertDrafts(drafts, BankTransactionSource.Csv);
    }

    [Fact]
    public async Task Dispatcher_routes_all_supported_formats_to_the_shared_draft_shape()
    {
        var dispatcher = new BankStatementImportDispatcher(
            [new Camt053Importer(), new Mt940Importer(), new CsvImporter()]);
        var fixtures = new[]
        {
            (Fixture: "sample-camt053.xml", FileName: "statement.xml", ContentType: "application/xml", Source: BankTransactionSource.Camt),
            (Fixture: "sample.mt940", FileName: "statement.sta", ContentType: "application/octet-stream", Source: BankTransactionSource.Mt940),
            (Fixture: "sample.csv", FileName: "statement.csv", ContentType: "text/csv", Source: BankTransactionSource.Csv),
        };

        foreach (var fixture in fixtures)
        {
            await using var content = File.OpenRead(FixturePath(fixture.Fixture));
            var drafts = await dispatcher.ImportAsync(
                content,
                fixture.FileName,
                fixture.ContentType,
                CancellationToken.None);

            AssertDrafts(drafts, fixture.Source);
        }
    }

    [Fact]
    public async Task Dispatcher_rejects_an_unknown_format()
    {
        var dispatcher = new BankStatementImportDispatcher(
            [new Camt053Importer(), new Mt940Importer(), new CsvImporter()]);
        await using var content = new MemoryStream();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => dispatcher.ImportAsync(
            content,
            "statement.bin",
            "application/octet-stream",
            CancellationToken.None));

        Assert.StartsWith("Nicht unterstütztes Kontoauszugsformat", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<BankTransactionDraft>> ParseAsync(
        IBankStatementImporter importer,
        Stream content)
    {
        var drafts = new List<BankTransactionDraft>();
        await foreach (var draft in importer.ParseAsync(content, CancellationToken.None))
        {
            drafts.Add(draft);
        }

        return drafts;
    }

    private static void AssertDrafts(
        IReadOnlyList<BankTransactionDraft> drafts,
        BankTransactionSource expectedSource)
    {
        Assert.Collection(
            drafts,
            credit =>
            {
                Assert.Equal(119m, credit.Amount);
                Assert.Equal(new DateOnly(2026, 8, 5), credit.ValueDate);
                Assert.Equal("Rechnung RE-2026-0042", credit.Purpose);
                Assert.Equal("Musterkunde GmbH", credit.CounterpartyName);
                Assert.Equal("DE89370400440532013000", credit.CounterpartyIban);
                Assert.Equal(expectedSource, credit.Source);
            },
            debit =>
            {
                Assert.Equal(-49.95m, debit.Amount);
                Assert.Equal(new DateOnly(2026, 8, 6), debit.ValueDate);
                Assert.Equal("Büromaterial", debit.Purpose);
                Assert.Equal("Bürobedarf AG", debit.CounterpartyName);
                Assert.Equal("DE12500105170648489890", debit.CounterpartyIban);
                Assert.Equal(expectedSource, debit.Source);
            });
    }

    private static string FixturePath(string fileName, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", fileName);
}
