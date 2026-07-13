using Microsoft.EntityFrameworkCore;

using Numera.Modules.Sales;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The INV-02 race-safety hard gate: under N parallel finalizations of ONE tenant's
/// Rechnung series, the atomic <c>INSERT … ON CONFLICT … RETURNING</c> counter in the
/// production <c>NumberingService</c> hands out N distinct, sequential numbers with zero
/// duplicates and the partial-unique <c>(tenant, doc_type, document_number)</c> index
/// never trips. Mirrors the Phase-1 pool/concurrency pattern (<see cref="PoolLeakTests"/>):
/// each parallel finalize runs on its OWN <c>numera_app</c> context + connection + ambient
/// transaction, so the ON CONFLICT row lock is the only thing serialising the claims.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesNumberingConcurrencyTests
{
    private const int ParallelCount = 20;

    private readonly PostgresFixture _fixture;

    public SalesNumberingConcurrencyTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Parallel_finalization_assigns_N_distinct_sequential_numbers_with_zero_duplicates()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);

        // Seed N finalizable drafts (each with a taxable line).
        var docIds = new List<Guid>();
        for (var i = 0; i < ParallelCount; i++)
        {
            docIds.Add(await SalesTestData.SeedDraftAsync(
                _fixture, tenant, DocumentType.Rechnung, partner.Id,
                [new SalesTestData.LineSpec($"Position {i}", 1m, 100m, Numera.Platform.Money.TaxCategory.S, 19m)],
                date));
        }

        // Drive all N finalizations in parallel, each on its own context/connection — the
        // SAME numbering seam the endpoint uses, under maximum contention on one series.
        var tasks = docIds.Select(async id =>
        {
            await using var db = _fixture.CreateAppContext(tenant);
            await SalesTestData.FinalizeAsync(db, id);
        });

        // Zero UniqueViolation must surface: a collision would bubble as a DbUpdateException.
        await Task.WhenAll(tasks);

        // Re-read every finalized document and assert the numbering invariants.
        await using var read = _fixture.CreateAppContext(tenant);
        var docs = await read.Set<SalesDocument>()
            .AsNoTracking()
            .Where(d => d.DocumentType == DocumentType.Rechnung)
            .ToListAsync();

        Assert.Equal(ParallelCount, docs.Count);
        Assert.All(docs, d => Assert.Equal(DocumentStatus.Finalized, d.Status));
        Assert.All(docs, d => Assert.False(string.IsNullOrWhiteSpace(d.DocumentNumber)));

        var sequences = docs.Select(d => SalesTestData.SequenceOf(d.DocumentNumber!)).OrderBy(n => n).ToList();

        // Exactly N DISTINCT values, sequential 1..N, zero duplicates.
        Assert.Equal(ParallelCount, sequences.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, ParallelCount), sequences);

        // Every rendered number is unique and follows the configured RE-2026-##### format.
        var numbers = docs.Select(d => d.DocumentNumber!).ToList();
        Assert.Equal(ParallelCount, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(@"^RE-2026-\d{5}$", n));
    }
}
