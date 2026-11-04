using Microsoft.EntityFrameworkCore;

using Numera.Api.Lifecycle;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// Real-PostgreSQL coverage for partner anonymization (Phase B1 Scheibe 2, Szenario 2: DSGVO
/// Art. 17 erasure of a partner that cannot be hard-deleted because GoBD documents reference it).
/// Proves that the master PII is neutralized and the PII child records are removed, while the
/// already-finalized invoice keeps its frozen recipient snapshot untouched.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartnerAnonymizationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymize_neutralizes_pii_keeps_invoice_snapshot_and_removes_child_records()
    {
        var tenant = Guid.CreateVersion7();
        var date = new DateOnly(2026, 6, 1);

        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant, name: "Empfänger AG");

        // Give the partner a number + PII-bearing children, then issue an invoice to it.
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var p = await db.Set<BusinessPartner>().SingleAsync(x => x.Id == partner.Id);
            p.CustomerNumber = "K-00042";
            db.Add(new PartnerContact { TenantId = tenant, PartnerId = partner.Id, LastName = "Müller" });
            db.Add(new PartnerNote { TenantId = tenant, PartnerId = partner.Id, AuthorUserId = Guid.CreateVersion7(), Body = "VIP-Kunde" });
            db.Add(new PartnerTask { TenantId = tenant, PartnerId = partner.Id, Title = "Rückruf Herr Müller" });
            db.Add(new PartnerActivity { TenantId = tenant, PartnerId = partner.Id, Type = PartnerActivityType.PartnerCreated, Summary = "Partner \"Empfänger AG\" created" });
            await db.SaveChangesAsync();
        }

        var docId = await SalesTestData.SeedDraftAsync(
            fixture, tenant, DocumentType.Rechnung, partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            date);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, docId);
        }

        var snapshotBefore = await ReadRecipientSnapshotAsync(tenant, docId);
        Assert.Contains("Empfänger AG", snapshotBefore);
        Assert.Contains("München", snapshotBefore);

        var audit = new CapturingAuditWriter();
        var outcome = await CreateService(tenant, audit).AnonymizeAsync(partner.Id);
        Assert.Equal(PartnerAnonymizationOutcome.Ok, outcome);

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var p = await db.Set<BusinessPartner>()
                .IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])
                .SingleAsync(x => x.Id == partner.Id);

            // PII overwritten.
            Assert.Equal("Anonymisiert", p.Name);
            Assert.Equal(string.Empty, p.BillingAddress.Street);
            Assert.Equal(string.Empty, p.BillingAddress.City);
            Assert.Null(p.VatId);
            Assert.Null(p.Email);
            Assert.NotNull(p.AnonymizedAt);
            Assert.NotNull(p.ArchivedAt);
            // Non-PII continuity preserved.
            Assert.Equal("K-00042", p.CustomerNumber);

            // PII-bearing children gone; the timeline holds exactly one neutral entry.
            Assert.Equal(0, await db.Set<PartnerContact>().CountAsync(c => c.PartnerId == partner.Id));
            Assert.Equal(0, await db.Set<PartnerNote>().CountAsync(n => n.PartnerId == partner.Id));
            Assert.Equal(0, await db.Set<PartnerTask>().CountAsync(t => t.PartnerId == partner.Id));
            var activities = await db.Set<PartnerActivity>().Where(a => a.PartnerId == partner.Id).ToListAsync();
            Assert.Single(activities);
            Assert.Equal(PartnerActivityType.Anonymized, activities[0].Type);
            Assert.DoesNotContain("Empfänger AG", activities[0].Summary);
        }

        // The legally retained invoice still shows the original buyer, and still links the partner.
        var snapshotAfter = await ReadRecipientSnapshotAsync(tenant, docId);
        Assert.Equal(snapshotBefore, snapshotAfter);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var doc = await db.Set<SalesDocument>().SingleAsync(d => d.Id == docId);
            Assert.Equal(partner.Id, doc.PartnerId);
        }

        // Accountability trail written (Art. 5(2)).
        Assert.Contains("partner.anonymized", audit.Actions);

        // Idempotent.
        var second = await CreateService(tenant, new CapturingAuditWriter()).AnonymizeAsync(partner.Id);
        Assert.Equal(PartnerAnonymizationOutcome.AlreadyAnonymized, second);
    }

    [Fact]
    public async Task Anonymize_unknown_partner_returns_not_found()
    {
        var tenant = Guid.CreateVersion7();
        var outcome = await CreateService(tenant, new CapturingAuditWriter())
            .AnonymizeAsync(Guid.CreateVersion7());
        Assert.Equal(PartnerAnonymizationOutcome.NotFound, outcome);
    }

    // --- helpers -----------------------------------------------------------

    private PartnerAnonymizationService CreateService(Guid tenant, IAuditWriter audit) =>
        new(fixture.CreateAppContext(tenant), audit, new FakeCurrentUser(Guid.CreateVersion7()));

    private async Task<string> ReadRecipientSnapshotAsync(Guid tenant, Guid docId)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var doc = await db.Set<SalesDocument>().AsNoTracking().SingleAsync(d => d.Id == docId);
        return doc.RecipientSnapshot ?? string.Empty;
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class CapturingAuditWriter : IAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task RecordAsync(IAuditEvent evt, CancellationToken ct)
        {
            Actions.Add(evt.Action);
            return Task.CompletedTask;
        }
    }
}
