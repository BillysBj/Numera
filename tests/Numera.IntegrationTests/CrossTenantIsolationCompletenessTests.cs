using Microsoft.EntityFrameworkCore;

using Numera.Api.Reporting;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Payments;
using Numera.Modules.Sales.Recurring;
using Numera.Platform.Db;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The consolidating cross-tenant isolation proof (Phase 9, success criterion 4) over the 10
/// phase-6→8 tables that had functional coverage but no dedicated RLS-isolation test:
/// <c>payment</c>, <c>payment_allocation</c>, <c>dunning_level_config</c>, <c>dunning_notice</c>,
/// <c>recurring_invoice_templates</c>, <c>recurring_invoice_template_lines</c>,
/// <c>sales_document_prepayment</c>, <c>partner_tasks</c>, <c>customer_files</c>,
/// <c>document_einvoice</c>, <c>inbound_document</c>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="RlsIsolationTests"/> exactly: on real postgres:18 as the non-BYPASSRLS
/// <c>numera_app</c> role, every read uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// so the EF global filter is OFF and Row-Level Security is the ONLY control under test. Two proofs
/// per table: (1) tenant A reads zero of tenant B's rows; (2) a cross-tenant INSERT is rejected by
/// the RLS WITH CHECK policy. FK seeding is kept minimal — the isolation assertion is the point.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CrossTenantIsolationCompletenessTests(PostgresFixture fixture)
{
    private static readonly DateOnly Date = new(2026, 6, 1);

    // ---------------------------------------------------------------- read-isolation

    [Fact]
    public async Task Read_isolation_holds_for_every_phase_6_to_8_table()
    {
        await ReadIsolationAsync<Payment>((t, ctx) => ctx.Add(NewPayment(t)));
        await ReadIsolationAsync<PaymentAllocation>((t, ctx) => ctx.Add(NewAllocation(t)));
        await ReadIsolationAsync<DunningLevelConfig>((t, ctx) => ctx.Add(NewDunningConfig(t)));
        await ReadIsolationAsync<DunningNotice>((t, ctx) => ctx.Add(NewDunningNotice(t)));
        await ReadIsolationAsync<RecurringInvoiceTemplate>((t, ctx) => ctx.Add(NewTemplate(t)));
        await ReadIsolationAsync<RecurringInvoiceTemplateLine>((t, ctx) =>
        {
            var template = NewTemplate(t);
            ctx.Add(template);
            ctx.Add(NewTemplateLine(t, template.Id));
        });
        await ReadIsolationAsync<SalesDocumentPrepayment>((t, ctx) =>
        {
            var doc = NewDraft(t);
            ctx.Add(doc);
            ctx.Add(NewPrepayment(t, doc.Id));
        });
        await ReadIsolationAsync<PartnerTask>((t, ctx) => ctx.Add(NewPartnerTask(t)));
        await ReadIsolationAsync<CustomerFile>((t, ctx) => ctx.Add(NewCustomerFile(t)));
        await ReadIsolationAsync<EInvoiceArtifact>((t, ctx) => ctx.Add(NewEInvoiceArtifact(t)));
        await ReadIsolationAsync<InboundDocument>((t, ctx) => ctx.Add(NewInboundDocument(t)));
        await ReadIsolationAsync<UstVaFiling>((t, ctx) => ctx.Add(NewUstVaFiling(t)));
    }

    // Seeds one row (+ any parents) as tenant A and one as tenant B, then proves — with the EF
    // filter OFF — that tenant A sees only its own rows and tenant B independently sees its own.
    private async Task ReadIsolationAsync<T>(Action<Guid, NumeraDbContext> seed) where T : class
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        await using (var ctx = fixture.CreateAppContext(a)) { seed(a, ctx); await ctx.SaveChangesAsync(); }
        await using (var ctx = fixture.CreateAppContext(b)) { seed(b, ctx); await ctx.SaveChangesAsync(); }

        await using var readA = fixture.CreateAppContext(a);
        var rowsA = await readA.Set<T>().IgnoreQueryFilters().ToListAsync();
        Assert.NotEmpty(rowsA);
        Assert.All(rowsA, r => Assert.Equal(a, ((ITenantEntity)r).TenantId));
        Assert.DoesNotContain(rowsA, r => ((ITenantEntity)r).TenantId == b);

        // The B row really was written (so A-sees-only-A is not vacuous): B sees its own.
        await using var readB = fixture.CreateAppContext(b);
        Assert.NotEmpty(await readB.Set<T>().IgnoreQueryFilters().ToListAsync());
    }

    // ---------------------------------------------------------------- WITH CHECK reject

    [Fact]
    public async Task With_check_rejects_a_cross_tenant_insert_for_every_phase_6_to_8_table()
    {
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewPayment(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewAllocation(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewDunningConfig(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewDunningNotice(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewTemplate(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewPartnerTask(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewCustomerFile(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewEInvoiceArtifact(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewInboundDocument(b)));
        await WithCheckRejectsAsync((a, b, ctx) => ctx.Add(NewUstVaFiling(b)));

        // FK tables: seed the parent as tenant A (so the ONLY violation is the foreign child
        // TenantId), then attempt the cross-tenant child insert.
        await WithCheckRejectsAsync(async (a, b, ctx) =>
        {
            var template = NewTemplate(a);
            ctx.Add(template);
            await ctx.SaveChangesAsync();
            ctx.Add(NewTemplateLine(b, template.Id));
        });
        await WithCheckRejectsAsync(async (a, b, ctx) =>
        {
            var doc = NewDraft(a);
            ctx.Add(doc);
            await ctx.SaveChangesAsync();
            ctx.Add(NewPrepayment(b, doc.Id));
        });
    }

    private Task WithCheckRejectsAsync(Action<Guid, Guid, NumeraDbContext> addForeign) =>
        WithCheckRejectsAsync((a, b, ctx) => { addForeign(a, b, ctx); return Task.CompletedTask; });

    // Acting as tenant A (GUC = A), attempts to persist a row stamped for tenant B and asserts the
    // RLS WITH CHECK policy rejects it (mirrors RlsIsolationTests).
    private async Task WithCheckRejectsAsync(Func<Guid, Guid, NumeraDbContext, Task> stage)
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        await using var ctx = fixture.CreateAppContext(a);
        await stage(a, b, ctx);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("row-level security", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- minimal row builders

    private static Payment NewPayment(Guid t) => new()
    {
        TenantId = t,
        Amount = 100m,
        ValueDate = Date,
        RecordedAt = DateTimeOffset.UtcNow,
    };

    private static PaymentAllocation NewAllocation(Guid t) => new()
    {
        TenantId = t,
        PaymentId = Guid.CreateVersion7(),
        OpenItemId = Guid.CreateVersion7(),
        AllocatedAmount = 100m,
    };

    private static DunningLevelConfig NewDunningConfig(Guid t) => new()
    {
        TenantId = t,
        Level = 1,
        Name = "Mahnung",
        DaysAfterDue = 14,
        Fee = 5m,
        TemplateTextDe = "de",
        TemplateTextEn = "en",
    };

    private static DunningNotice NewDunningNotice(Guid t) => new()
    {
        TenantId = t,
        OpenItemId = Guid.CreateVersion7(),
        DocumentId = Guid.CreateVersion7(),
        Level = 1,
        IssuedOn = Date,
        NewDueDate = Date,
        OverdueAmount = 100m,
        Fee = 5m,
        TotalToPay = 105m,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static RecurringInvoiceTemplate NewTemplate(Guid t) => new()
    {
        TenantId = t,
        Name = "Monatlich",
        IntervalCount = 1,
        StartOn = Date,
        NextRunOn = Date,
    };

    private static RecurringInvoiceTemplateLine NewTemplateLine(Guid t, Guid templateId) => new()
    {
        TenantId = t,
        TemplateId = templateId,
        LineNumber = 1,
        Name = "Position",
        Quantity = 1m,
        UnitCode = "C62",
        NetUnitPrice = 100m,
        TaxCategory = TaxCategory.S,
        VatRatePercent = 19m,
    };

    private static SalesDocument NewDraft(Guid t) =>
        SalesTestData.BuildDraft(t, DocumentType.Rechnung, null,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)], Date);

    private static SalesDocumentPrepayment NewPrepayment(Guid t, Guid documentId) => new()
    {
        TenantId = t,
        DocumentId = documentId,
        AbschlagDocumentId = Guid.CreateVersion7(),
        AbschlagNumber = "AR-2026-00001",
        AbschlagDate = Date,
        NetAmount = 100m,
        VatAmount = 19m,
        GrossAmount = 119m,
    };

    private static PartnerTask NewPartnerTask(Guid t) => new()
    {
        TenantId = t,
        PartnerId = Guid.CreateVersion7(),
        Title = "Rückruf",
    };

    private static CustomerFile NewCustomerFile(Guid t) => new()
    {
        TenantId = t,
        PartnerId = Guid.CreateVersion7(),
        Bytes = [1, 2, 3],
        FileName = "akte.pdf",
        ContentType = "application/pdf",
        ByteSize = 3,
    };

    private static EInvoiceArtifact NewEInvoiceArtifact(Guid t) => new()
    {
        TenantId = t,
        DocumentId = Guid.CreateVersion7(),
        Format = EInvoiceFormat.XRechnungUbl,
        Xml = [1, 2, 3],
        DocumentNumber = "RE-2026-00001",
        ValidationStatus = EInvoiceValidationStatus.Accepted,
        ByteSize = 3,
        GeneratedAt = DateTimeOffset.UtcNow,
    };

    private static InboundDocument NewInboundDocument(Guid t) => new()
    {
        TenantId = t,
        OriginalBytes = [1, 2, 3],
        OriginalFileName = "inbound.xml",
        OriginalContentType = "application/xml",
        ByteSize = 3,
        UploadedAt = DateTimeOffset.UtcNow,
    };

    private static UstVaFiling NewUstVaFiling(Guid t) => new()
    {
        TenantId = t,
        Jahr = 2026,
        Zeitraum = "06",
        Besteuerungsart = Numera.Modules.Ledger.Besteuerungsart.Soll,
        KzSnapshotJson = "[]",
        Zahllast = 0m,
        Status = UstVaFilingStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
