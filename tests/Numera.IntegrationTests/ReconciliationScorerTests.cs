using Microsoft.EntityFrameworkCore;

using Numera.Modules.Banking;
using Numera.Modules.Banking.Reconciliation;
using Numera.Modules.Crm;
using Numera.Modules.Sales;

using Xunit;

using CrmAddress = Numera.Modules.Crm.Address;

namespace Numera.IntegrationTests;

/// <summary>Real-Postgres proof of receivable-only, proposal-only reconciliation scoring.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReconciliationScorerTests(PostgresFixture fixture)
{
    private const string DocumentNumber = "RE-2026-0042";
    private const string CustomerIban = "DE89 3704 0044 0532 0130 00";

    [Fact]
    public async Task Incoming_transactions_are_ranked_without_booking_or_clearing()
    {
        var (tenant, openItemId, documentId) = await SeedReceivableAsync();

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var scorer = new ReconciliationScorer(db);

            var exact = Assert.Single(await scorer.ScoreAsync(Transaction(
                tenant,
                119m,
                $"Zahlung für Rechnung {DocumentNumber}",
                CustomerIban)));
            Assert.Equal(openItemId, exact.OpenItemId);
            Assert.Equal(DocumentNumber, exact.DocumentNumber);
            Assert.Equal(119m, exact.OpenAmount);
            Assert.Equal(119m, exact.SuggestedAllocation);
            Assert.Equal(MatchTier.High, exact.Tier);
            Assert.InRange(exact.Score, 0.99m, 1m);

            var partial = Assert.Single(await scorer.ScoreAsync(Transaction(
                tenant,
                60m,
                $"Teilzahlung {DocumentNumber}")));
            Assert.Equal(openItemId, partial.OpenItemId);
            Assert.Equal(60m, partial.SuggestedAllocation);
            Assert.Equal(MatchTier.Review, partial.Tier);

            var unrelated = await scorer.ScoreAsync(Transaction(
                tenant,
                500m,
                "Mitgliedsbeitrag August",
                "DE00 0000 0000 0000 0000 00",
                "Unbekannter Verein"));
            Assert.Empty(unrelated);

            var outgoing = await scorer.ScoreAsync(Transaction(
                tenant,
                -119m,
                $"Auszahlung {DocumentNumber}",
                CustomerIban));
            Assert.Empty(outgoing);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var unchangedOpenItem = await read.Set<OpenItem>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == openItemId);
        var unchangedDocument = await read.Set<SalesDocument>()
            .AsNoTracking()
            .SingleAsync(document => document.Id == documentId);
        Assert.Equal(119m, unchangedOpenItem.OpenAmount);
        Assert.Equal(OpenItemStatus.Open, unchangedOpenItem.Status);
        Assert.Equal(119m, unchangedDocument.AmountDue);
        Assert.Equal(DocumentStatus.Finalized, unchangedDocument.Status);
    }

    private async Task<(Guid Tenant, Guid OpenItemId, Guid DocumentId)> SeedReceivableAsync()
    {
        var tenant = Guid.CreateVersion7();
        var partner = new BusinessPartner
        {
            TenantId = tenant,
            Name = "Musterkunde GmbH",
            IsCustomer = true,
            Iban = CustomerIban,
            BillingAddress = new CrmAddress
            {
                Street = "Kundenweg 7",
                PostalCode = "10115",
                City = "Berlin",
                CountryCode = "DE",
            },
        };
        var document = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = DocumentType.Rechnung,
            Status = DocumentStatus.Finalized,
            DocumentNumber = DocumentNumber,
            PartnerId = partner.Id,
            DocumentDate = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 8, 31),
            Currency = "EUR",
            TotalNet = 100m,
            TotalTax = 19m,
            TotalGross = 119m,
            AmountDue = 119m,
            FinalizedAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
        };
        var openItem = new OpenItem
        {
            TenantId = tenant,
            DocumentId = document.Id,
            PartnerId = partner.Id,
            DocumentNumber = DocumentNumber,
            Currency = "EUR",
            OriginalAmount = 119m,
            OpenAmount = 119m,
            Status = OpenItemStatus.Open,
            IssuedOn = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 8, 31),
        };

        await using var db = fixture.CreateAppContext(tenant);
        db.Add(partner);
        db.Add(document);
        await db.SaveChangesAsync();
        db.Add(openItem);
        await db.SaveChangesAsync();
        return (tenant, openItem.Id, document.Id);
    }

    private static BankTransaction Transaction(
        Guid tenant,
        decimal amount,
        string purpose,
        string? counterpartyIban = null,
        string? counterpartyName = null) =>
        new()
        {
            TenantId = tenant,
            DedupeKey = Guid.NewGuid().ToString("N"),
            Source = BankTransactionSource.Csv,
            Amount = amount,
            ValueDate = new DateOnly(2026, 8, 5),
            Purpose = purpose,
            CounterpartyName = counterpartyName,
            CounterpartyIban = counterpartyIban,
        };
}
