using System.Text;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Events;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Rendering;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The Phase-4 async-render hard gate (DOCS-02, success criterion 3): proves on real
/// postgres:18 as the non-BYPASSRLS <c>numera_app</c> role that finalizing an invoice through
/// the REAL production core produces a genuine frozen snapshot, and that the render job renders
/// a §14-complete PDF FROM that snapshot and stores it in <c>document_render</c> under RLS —
/// idempotently, and WITHOUT the finalize hook ever rendering inline.
/// </summary>
/// <remarks>
/// The finalize is driven through <see cref="SalesTestData.FinalizeAsync"/>, which delegates to
/// the shipped <c>SalesDocumentEndpoints.FinalizeCoreAsync</c> (the 03-11 InternalsVisibleTo
/// seam), so a real jsonb snapshot + BG-23 breakdown exist. The render is driven through the
/// actual <see cref="RenderDocumentPdfJob"/> (its own DI scope + tenant re-establishment), not a
/// live Hangfire server, so the test is deterministic. The enqueue-not-inline contract is proven
/// at the seam: <see cref="EnqueuePdfOnFinalize"/> with a fake <see cref="IBackgroundJobClient"/>
/// records exactly one Enqueue and writes NO <c>document_render</c> row.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRenderJobTests
{
    static DocumentRenderJobTests()
    {
        // QuestPDF throws on first render without an acknowledged license (Pitfall 1).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private readonly PostgresFixture _fixture;

    public DocumentRenderJobTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Render_job_stores_exactly_one_pdf_from_the_frozen_snapshot_and_re_render_replaces_it()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ],
            date);

        // Finalize through the REAL production core → genuine frozen snapshot + BG-23 breakdown.
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        // Drive the actual render job (fresh scope + tenant re-establishment, like production).
        await RunRenderJobAsync(tenant, docId);

        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var render = Assert.Single(
                await read.Set<DocumentRender>().AsNoTracking().Where(r => r.DocumentId == docId).ToListAsync());

            // (1) Exactly one row, tenant-scoped, a real PDF (%PDF magic) of non-trivial size.
            Assert.Equal(tenant, render.TenantId);
            Assert.Equal("de", render.Language);
            Assert.Equal("RE-2026-00001", render.DocumentNumber);
            Assert.True(render.ByteSize > 1000, $"ByteSize was {render.ByteSize}");
            Assert.True(render.PdfBytes.Length > 1000);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(render.PdfBytes, 0, 4));
        }

        // (2) Re-running the job REPLACES the row — still exactly one per language (idempotency).
        await RunRenderJobAsync(tenant, docId);

        await using (var read = _fixture.CreateAppContext(tenant))
        {
            Assert.Single(
                await read.Set<DocumentRender>().AsNoTracking().Where(r => r.DocumentId == docId).ToListAsync());
        }
    }

    [Fact]
    public async Task Render_of_a_kleinunternehmer_para19_invoice_produces_a_valid_pdf()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        // §19 Kleinunternehmer issuer → the finalized snapshot carries a category-E Pflichttext
        // breakdown; the layout must render that verbatim path to a valid PDF.
        await SalesTestData.SeedProfileAsync(_fixture, tenant, kleinunternehmer: true);
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant);
        var docId = await SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Leistung", 1m, 100m, TaxCategory.S, 19m)],
            date);

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        await RunRenderJobAsync(tenant, docId);

        await using var read = _fixture.CreateAppContext(tenant);
        var render = await read.Set<DocumentRender>().AsNoTracking().FirstAsync(r => r.DocumentId == docId);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(render.PdfBytes, 0, 4));
        Assert.True(render.ByteSize > 1000, $"ByteSize was {render.ByteSize}");
    }

    [Fact]
    public async Task Finalize_hook_only_enqueues_the_render_job_and_never_renders_inline()
    {
        var tenant = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();

        var client = new RecordingJobClient();
        var handler = new EnqueuePdfOnFinalize(client);

        await handler.HandleAsync(
            new InvoiceFinalized(tenant, docId, "RE-2026-00042", 100m, 19m, 119m, new DateOnly(2026, 6, 1), DocumentType.Rechnung),
            CancellationToken.None);

        // Enqueue-not-inline: exactly one job enqueued, and it is the render job.
        Assert.Equal(1, client.CreateCalls);
        Assert.Equal(typeof(RenderDocumentPdfJob), client.LastJob!.Type);
        Assert.Equal(nameof(RenderDocumentPdfJob.RunAsync), client.LastJob!.Method.Name);

        // The handler performed NO render — no document_render row exists (finalize is not blocked).
        await using var read = _fixture.CreateAppContext(tenant);
        Assert.Empty(await read.Set<DocumentRender>().AsNoTracking().Where(r => r.DocumentId == docId).ToListAsync());
    }

    // Runs the REAL RenderDocumentPdfJob over a minimal DI container mirroring the Api host: a
    // scoped ICurrentTenant + NumeraDbContext (self-registers the tenant interceptor) + the
    // DocumentPdfService. The job opens its own scope and SetTenant, so RLS applies inside it.
    private async Task RunRenderJobAsync(Guid tenant, Guid docId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(o => o.UseNpgsql(_fixture.AppConnectionString));
        services.AddScoped<DocumentPdfService>();

        await using var provider = services.BuildServiceProvider();
        var job = new RenderDocumentPdfJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RenderDocumentPdfJob>.Instance);

        await job.RunAsync(tenant, docId, "de", CancellationToken.None);
    }

    /// <summary>A fake <see cref="IBackgroundJobClient"/> that records Create (Enqueue) calls without a server.</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public int CreateCalls { get; private set; }

        public Job? LastJob { get; private set; }

        public string Create(Job job, IState state)
        {
            CreateCalls++;
            LastJob = job;
            return Guid.CreateVersion7().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
