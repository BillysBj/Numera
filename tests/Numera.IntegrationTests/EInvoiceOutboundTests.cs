using System.Text;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Endpoints;
using Numera.Api.Events;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Events;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The Phase-5 outbound e-invoice hard gate (EINV-01/EINV-03) on real postgres:18 as the
/// non-BYPASSRLS <c>numera_app</c> role. Proves that a finalized invoice's XRechnung (UBL+CII) is
/// generated from the frozen snapshot, validated, and stored in <c>document_einvoice</c> under RLS
/// (idempotently); that the finalize hook only ENQUEUES (never generates inline); that the stage-1
/// pre-finalize dry-run blocks a hard rejection WITHOUT burning a gapless number; and that the
/// stored verdict the stage-2 send gate reads is persisted with its explained findings.
/// </summary>
/// <remarks>
/// The validator is a deterministic FAKE (<see cref="FakeEInvoiceValidator"/>) — the real KoSIT
/// report parsing is golden-tested in 05-02, and the LIVE conformance of the real generated
/// XRechnung is proven separately by <see cref="KoSitConformanceTests"/> (Task 4). Finalize runs
/// through the shipped production core (<see cref="SalesTestData.FinalizeCoreAsync"/>), and the
/// generate job runs through the actual <see cref="GenerateEInvoiceJob"/> over a minimal DI
/// container (its own scope + tenant re-establishment), so these tests are deterministic yet
/// faithful.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class EInvoiceOutboundTests
{
    private readonly PostgresFixture _fixture;

    // The finalize job now eagerly renders the ZUGFeRD PDF/A-3 (FinalizeFormats includes
    // ZugferdPdfA3 since 05-04), so QuestPDF needs its Community license set — exactly as the
    // real Api host does at startup, mirrored here like the other PDF-rendering integration tests.
    static EInvoiceOutboundTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public EInvoiceOutboundTests(PostgresFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------- (1) generate + persist + idempotency + RLS

    [Fact]
    public async Task Generate_job_stores_one_accepted_ubl_and_cii_artifact_and_re_run_replaces_and_rls_isolates()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        var docId = await SeedAndFinalizeRechnungAsync(tenant, date);

        var validator = new FakeEInvoiceValidator { Status = EInvoiceValidationStatus.Accepted };
        await RunGenerateJobAsync(tenant, docId, validator);

        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var artifacts = await read.Set<EInvoiceArtifact>().AsNoTracking()
                .Where(a => a.DocumentId == docId).OrderBy(a => a.Format).ToListAsync();

            // One artifact per FinalizeFormats entry: UBL + CII (XRechnung XML) + ZUGFeRD PDF/A-3.
            Assert.Equal(3, artifacts.Count);
            Assert.Equal(EInvoiceFormat.XRechnungUbl, artifacts[0].Format);
            Assert.Equal(EInvoiceFormat.XRechnungCii, artifacts[1].Format);
            Assert.Equal(EInvoiceFormat.ZugferdPdfA3, artifacts[2].Format);

            // The two XRechnung syntaxes are real, KoSIT-accepted XML invoices.
            foreach (var a in artifacts.Where(a => a.Format is EInvoiceFormat.XRechnungUbl or EInvoiceFormat.XRechnungCii))
            {
                Assert.Equal(tenant, a.TenantId);
                Assert.Equal("RE-2026-00001", a.DocumentNumber);
                Assert.Equal(EInvoiceValidationStatus.Accepted, a.ValidationStatus);
                Assert.True(a.ByteSize > 0, $"ByteSize was {a.ByteSize}");
                Assert.True(a.Xml.Length > 0);
                var text = Encoding.UTF8.GetString(a.Xml);
                Assert.Contains("Invoice", text, StringComparison.Ordinal);
                Assert.NotNull(a.ValidatedAt);
            }

            // The ZUGFeRD artifact carries the PDF/A-3 bytes (not XML) and reuses the CII's verdict.
            var zugferd = artifacts[2];
            Assert.Equal(tenant, zugferd.TenantId);
            Assert.Equal("RE-2026-00001", zugferd.DocumentNumber);
            Assert.Equal(EInvoiceValidationStatus.Accepted, zugferd.ValidationStatus);
            Assert.True(zugferd.ByteSize > 0, $"ByteSize was {zugferd.ByteSize}");
            Assert.StartsWith("%PDF", Encoding.UTF8.GetString(zugferd.Xml, 0, Math.Min(8, zugferd.Xml.Length)), StringComparison.Ordinal);
        }

        // (idempotency) Re-running REPLACES, never duplicates — still exactly one per format.
        await RunGenerateJobAsync(tenant, docId, validator);
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            Assert.Equal(3, await read.Set<EInvoiceArtifact>().AsNoTracking()
                .CountAsync(a => a.DocumentId == docId));
        }

        // (RLS) A second tenant sees NONE of these rows even with the EF filter off.
        await using (var readOther = _fixture.CreateAppContext(otherTenant))
        {
            Assert.Equal(0, await readOther.Set<EInvoiceArtifact>().IgnoreQueryFilters()
                .CountAsync(a => a.DocumentId == docId));
        }
    }

    // ---------------------------------------------------------- (2) enqueue-not-inline

    [Fact]
    public async Task Finalize_hook_only_enqueues_the_generate_job_for_a_rechnung_and_never_generates_inline()
    {
        var tenant = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();

        var client = new RecordingJobClient();
        // EInvoicing granted (L+): the enqueue proceeds, so this proves enqueue-not-inline.
        var handler = new EnqueueEInvoiceOnFinalize(client, FakeEntitlementService.Granting);

        await handler.HandleAsync(
            new InvoiceFinalized(tenant, docId, "RE-2026-00042", 100m, 19m, 119m, new DateOnly(2026, 6, 1), DocumentType.Rechnung),
            CancellationToken.None);

        // Enqueue-not-inline: exactly one job enqueued, and it is the generate job.
        Assert.Equal(1, client.CreateCalls);
        Assert.Equal(typeof(GenerateEInvoiceJob), client.LastJob!.Type);
        Assert.Equal(nameof(GenerateEInvoiceJob.RunAsync), client.LastJob!.Method.Name);

        // The handler performed NO generation — no document_einvoice row exists (finalize is not blocked).
        await using var read = _fixture.CreateAppContext(tenant);
        Assert.Empty(await read.Set<EInvoiceArtifact>().AsNoTracking().Where(a => a.DocumentId == docId).ToListAsync());
    }

    [Fact]
    public async Task Finalize_hook_does_not_enqueue_for_a_non_rechnung()
    {
        var client = new RecordingJobClient();
        // EInvoicing granted: proves the TYPE gate (not the tarif gate) is what skips a non-Rechnung.
        var handler = new EnqueueEInvoiceOnFinalize(client, FakeEntitlementService.Granting);

        // A Gutschrift/Storno is out of v1 e-invoicing scope — the type gate must skip it.
        await handler.HandleAsync(
            new InvoiceFinalized(Guid.CreateVersion7(), Guid.CreateVersion7(), "GS-2026-00001", 100m, 19m, 119m, new DateOnly(2026, 6, 1), DocumentType.Gutschrift),
            CancellationToken.None);

        Assert.Equal(0, client.CreateCalls);
    }

    // ---------------------------------------------------------- (3) stage-1 dry-run burns no number

    [Fact]
    public async Task Stage1_dry_run_rejection_blocks_finalize_and_burns_no_number_while_accepted_and_unavailable_proceed()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);

        var rejectedDoc = await SeedDraftAsync(tenant, partner.Id, date);
        var acceptedDoc = await SeedDraftAsync(tenant, partner.Id, date);
        var unavailableDoc = await SeedDraftAsync(tenant, partner.Id, date);

        // A hard rejection BLOCKS finalize: the document stays Draft with no number.
        var blocked = await TryFinalizeWithGateAsync(tenant, rejectedDoc, EInvoiceValidationStatus.Rejected);
        Assert.False(blocked);
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == rejectedDoc);
            Assert.Equal(DocumentStatus.Draft, doc.Status);
            Assert.Null(doc.DocumentNumber);
        }

        // Accepted PROCEEDS — and gets RE-2026-00001, proving the rejected attempt burned NO number.
        var acceptedOk = await TryFinalizeWithGateAsync(tenant, acceptedDoc, EInvoiceValidationStatus.Accepted);
        Assert.True(acceptedOk);
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == acceptedDoc);
            Assert.Equal(DocumentStatus.Finalized, doc.Status);
            Assert.Equal("RE-2026-00001", doc.DocumentNumber);
        }

        // A validator OUTAGE does NOT block finalize — it proceeds to the next number.
        var unavailableOk = await TryFinalizeWithGateAsync(tenant, unavailableDoc, EInvoiceValidationStatus.Unavailable);
        Assert.True(unavailableOk);
        await using (var read = _fixture.CreateAppContext(tenant))
        {
            var doc = await read.Set<SalesDocument>().AsNoTracking().FirstAsync(d => d.Id == unavailableDoc);
            Assert.Equal(DocumentStatus.Finalized, doc.Status);
            Assert.Equal("RE-2026-00002", doc.DocumentNumber);
        }
    }

    // ---------------------------------------------------------- (4) stage-2 stored verdict the send gate reads

    [Fact]
    public async Task Stage2_persisted_verdict_gates_the_send_rejected_carries_explained_findings_accepted_allows()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);
        var docId = await SeedAndFinalizeRechnungAsync(tenant, date);

        // Rejected: the stored artifact carries the explained findings the gate surfaces (422 blocked).
        var rejecting = new FakeEInvoiceValidator
        {
            Status = EInvoiceValidationStatus.Rejected,
            Findings =
            [
                new EInvoiceFinding(
                    "error", "BR-DE-15", "[BR-DE-15] Buyer reference missing.",
                    "Die Käuferreferenz (BT-10) fehlt.", "The buyer reference (BT-10) is missing."),
            ],
        };

        await using (var db = _fixture.CreateAppContext(tenant))
        {
            var svc = new EInvoiceService(db, TenantOf(tenant), rejecting);
            var artifact = await svc.GenerateAndValidate(docId, EInvoiceFormat.XRechnungUbl, CancellationToken.None);

            Assert.Equal(EInvoiceValidationStatus.Rejected, artifact.ValidationStatus);
            Assert.NotNull(artifact.ValidationReport);

            // The send gate would refuse and surface explained DE/EN findings from the stored report.
            var findings = EInvoiceGate.ReadFindings(artifact.ValidationReport);
            var problem = EInvoiceGate.ToProblemDictionary(findings);
            Assert.True(problem.ContainsKey("BR-DE-15"));
            Assert.Contains(problem["BR-DE-15"], m => m.Contains("Käuferreferenz", StringComparison.Ordinal));
            Assert.Contains(problem["BR-DE-15"], m => m.Contains("buyer reference", StringComparison.OrdinalIgnoreCase));
        }

        // Accepted: the stored verdict is Accepted → the gate allows the Versand.
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            var svc = new EInvoiceService(db, TenantOf(tenant), new FakeEInvoiceValidator { Status = EInvoiceValidationStatus.Accepted });
            var artifact = await svc.GenerateAndValidate(docId, EInvoiceFormat.XRechnungUbl, CancellationToken.None);
            Assert.Equal(EInvoiceValidationStatus.Accepted, artifact.ValidationStatus);

            var stored = await svc.GetArtifactAsync(docId, EInvoiceFormat.XRechnungUbl, CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal(EInvoiceValidationStatus.Accepted, stored!.ValidationStatus);
        }

        // Unavailable: the verdict is Unavailable with a null report — the gate refuses (unverified).
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            var svc = new EInvoiceService(db, TenantOf(tenant), new FakeEInvoiceValidator { Status = EInvoiceValidationStatus.Unavailable });
            var artifact = await svc.GenerateAndValidate(docId, EInvoiceFormat.XRechnungUbl, CancellationToken.None);
            Assert.Equal(EInvoiceValidationStatus.Unavailable, artifact.ValidationStatus);
            Assert.Null(artifact.ValidationReport);
        }
    }

    // ------------------------------------------------------------- helpers

    // Mirrors the finalize endpoint's stage-1 ordering with the REAL EInvoiceService.DryRunAsync +
    // the REAL production FinalizeCoreAsync: gate (satisfied by seeds) → dry-run → finalize. Returns
    // false (blocked, no number burned) on a hard rejection, true when finalize proceeds.
    private async Task<bool> TryFinalizeWithGateAsync(Guid tenant, Guid docId, EInvoiceValidationStatus verdict)
    {
        await using var db = _fixture.CreateAppContext(tenant);
        var doc = await db.Set<SalesDocument>().Include(x => x.Lines).FirstAsync(x => x.Id == docId);
        var profile = await db.Set<Numera.Modules.Sales.CompanyProfile>().FirstAsync();
        var partner = doc.PartnerId is null
            ? null
            : await db.Set<Numera.Modules.Crm.BusinessPartner>().FirstOrDefaultAsync(p => p.Id == doc.PartnerId);

        var svc = new EInvoiceService(db, TenantOf(tenant), new FakeEInvoiceValidator { Status = verdict });
        var dryRun = await svc.DryRunAsync(doc, profile, partner, CancellationToken.None);
        if (dryRun.Status == EInvoiceValidationStatus.Rejected)
        {
            return false; // blocked BEFORE the transaction — no gapless number is burned.
        }

        await SalesTestData.FinalizeCoreAsync(db, doc, profile, partner);
        return true;
    }

    private async Task<Guid> SeedAndFinalizeRechnungAsync(Guid tenant, DateOnly date)
    {
        await SalesTestData.SeedProfileAsync(_fixture, tenant);
        await SalesTestData.SeedFormatAsync(_fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(_fixture, tenant, netDays: 30);
        var docId = await SeedDraftAsync(tenant, partner.Id, date);

        await using var db = _fixture.CreateAppContext(tenant);
        await SalesTestData.FinalizeAsync(db, docId);
        return docId;
    }

    private Task<Guid> SeedDraftAsync(Guid tenant, Guid? partnerId, DateOnly date) =>
        SalesTestData.SeedDraftAsync(
            _fixture, tenant, DocumentType.Rechnung, partnerId,
            [
                new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m),
                new SalesTestData.LineSpec("Buch", 1m, 200m, TaxCategory.S, 7m),
            ],
            date);

    private static TenantContext TenantOf(Guid tenant)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant);
        return ctx;
    }

    // Runs the REAL GenerateEInvoiceJob over a minimal DI container mirroring the Api host: a scoped
    // ICurrentTenant + NumeraDbContext + the fake validator + EInvoiceService. The job opens its own
    // scope and SetTenant, so RLS applies inside it exactly as in production.
    private async Task RunGenerateJobAsync(Guid tenant, Guid docId, IEInvoiceValidator validator)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(o => o.UseNpgsql(_fixture.AppConnectionString));
        services.AddSingleton(validator);
        services.AddScoped<EInvoiceService>();

        await using var provider = services.BuildServiceProvider();
        var job = new GenerateEInvoiceJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GenerateEInvoiceJob>.Instance);

        await job.RunAsync(tenant, docId, CancellationToken.None);
    }

    /// <summary>A deterministic <see cref="IEInvoiceValidator"/> that returns a configured verdict.</summary>
    private sealed class FakeEInvoiceValidator : IEInvoiceValidator
    {
        public EInvoiceValidationStatus Status { get; init; } = EInvoiceValidationStatus.Accepted;

        public IReadOnlyList<EInvoiceFinding> Findings { get; init; } = [];

        public Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EInvoiceValidationResult(
                Status,
                Findings,
                Status == EInvoiceValidationStatus.Unavailable ? null : "<rep:report/>"));
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
