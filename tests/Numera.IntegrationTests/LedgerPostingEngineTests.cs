using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Db;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Golden-file proofs for balanced SKR posting sets and guard failures.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerPostingEngineTests(PostgresFixture fixture)
{
    private static readonly DateOnly EntryDate = new(2026, 8, 3);

    [Fact]
    public async Task Invoice_19_percent_uses_debtor_revenue_and_output_tax_accounts()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = InvoiceSource(db, InvoiceInput(
            tenant,
            [new InvoicePostingBreakdown(TaxCategory.S, 19m, 100m, 19m)],
            119m));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(3, postings.Count);
        AssertLeg(postings, numbers, "1400", PostingDirection.Debit, 119m, null);
        AssertLeg(postings, numbers, "8400", PostingDirection.Credit, 100m, Steuerschluessel.Ust19);
        AssertLeg(postings, numbers, "1776", PostingDirection.Credit, 19m, null);
    }

    [Fact]
    public async Task Invoice_7_percent_uses_reduced_revenue_and_output_tax_accounts()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = InvoiceSource(db, InvoiceInput(
            tenant,
            [new InvoicePostingBreakdown(TaxCategory.S, 7m, 100m, 7m)],
            107m));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(3, postings.Count);
        AssertLeg(postings, numbers, "1400", PostingDirection.Debit, 107m, null);
        AssertLeg(postings, numbers, "8300", PostingDirection.Credit, 100m, Steuerschluessel.Ust7);
        AssertLeg(postings, numbers, "1771", PostingDirection.Credit, 7m, null);
    }

    [Fact]
    public async Task Mixed_invoice_emits_one_revenue_and_tax_pair_per_frozen_breakdown()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = InvoiceSource(db, InvoiceInput(
            tenant,
            [
                new InvoicePostingBreakdown(TaxCategory.S, 19m, 100m, 19m),
                new InvoicePostingBreakdown(TaxCategory.S, 7m, 50m, 3.50m),
            ],
            172.50m));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(5, postings.Count);
        AssertLeg(postings, numbers, "1400", PostingDirection.Debit, 172.50m, null);
        AssertLeg(postings, numbers, "8400", PostingDirection.Credit, 100m, Steuerschluessel.Ust19);
        AssertLeg(postings, numbers, "1776", PostingDirection.Credit, 19m, null);
        AssertLeg(postings, numbers, "8300", PostingDirection.Credit, 50m, Steuerschluessel.Ust7);
        AssertLeg(postings, numbers, "1771", PostingDirection.Credit, 3.50m, null);
    }

    [Fact]
    public async Task Reverse_charge_invoice_uses_tax_free_revenue_without_output_tax_leg()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = InvoiceSource(db, InvoiceInput(
            tenant,
            [new InvoicePostingBreakdown(TaxCategory.AE, 0m, 100m, 0m)],
            100m,
            reverseCharge: true));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(2, postings.Count);
        AssertLeg(postings, numbers, "1400", PostingDirection.Debit, 100m, null);
        AssertLeg(
            postings,
            numbers,
            "8200",
            PostingDirection.Credit,
            100m,
            Steuerschluessel.ReverseChargeNoTax);
        Assert.DoesNotContain(postings, posting => numbers[posting.AccountId] is "1776" or "1771");
    }

    [Fact]
    public async Task Payment_received_debits_bank_and_credits_one_receivable_leg_per_allocation()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = new PaymentPostingSource(
            tenant,
            ChartVariant.Skr03,
            new PaymentPostingInput(
                null,
                EntryDate,
                [new PaymentPostingAllocation(null, 40m), new PaymentPostingAllocation(null, 60m)],
                false),
            new AccountResolver(db));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(3, postings.Count);
        AssertLeg(postings, numbers, "1200", PostingDirection.Debit, 100m, null);
        AssertLeg(postings, numbers, "1400", PostingDirection.Credit, 40m, null);
        AssertLeg(postings, numbers, "1400", PostingDirection.Credit, 60m, null);
    }

    [Fact]
    public async Task Payment_reversal_swaps_bank_and_receivable_directions()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = new PaymentPostingSource(
            tenant,
            ChartVariant.Skr03,
            new PaymentPostingInput(
                null,
                EntryDate,
                [new PaymentPostingAllocation(null, -100m)],
                true),
            new AccountResolver(db));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(2, postings.Count);
        AssertLeg(postings, numbers, "1200", PostingDirection.Credit, 100m, null);
        AssertLeg(postings, numbers, "1400", PostingDirection.Debit, 100m, null);
    }

    [Fact]
    public async Task Expense_19_percent_debits_expense_and_input_tax_against_creditor()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var source = new ExpensePostingSource(
            tenant,
            ChartVariant.Skr03,
            new ExpensePostingInput(null, null, 19m, 100m, 19m, EntryDate),
            new AccountResolver(db));

        var postings = source.BuildPostings();
        var numbers = await AccountNumbersAsync(db);

        AssertBalanced(postings);
        Assert.Equal(3, postings.Count);
        AssertLeg(postings, numbers, "4980", PostingDirection.Debit, 100m, Steuerschluessel.Vst19);
        AssertLeg(postings, numbers, "1576", PostingDirection.Debit, 19m, Steuerschluessel.Vst19);
        AssertLeg(postings, numbers, "1600", PostingDirection.Credit, 119m, null);
    }

    [Fact]
    public async Task Invoice_storno_is_general_reversal_and_references_original_entry()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var resolver = new AccountResolver(db);
        var engine = new PostingEngine(db);
        var input = InvoiceInput(
            tenant,
            [new InvoicePostingBreakdown(TaxCategory.S, 19m, 100m, 19m)],
            119m);
        var original = await engine.PostAsync(
            new InvoicePostingSource(input, resolver),
            Header(tenant, input.DocumentId.ToString(), PostingType.Normal),
            CancellationToken.None);

        var storno = await engine.PostAsync(
            new InvoicePostingSource(input, resolver, reverseDirections: true),
            Header(tenant, $"storno:{input.DocumentId}", PostingType.Storno, original.Id),
            CancellationToken.None);
        var numbers = await AccountNumbersAsync(db);

        Assert.Equal(PostingType.Storno, storno.PostingType);
        Assert.Equal(original.Id, storno.ReversesEntryId);
        AssertBalanced(storno.Postings);
        AssertLeg(storno.Postings, numbers, "1400", PostingDirection.Credit, 119m, null);
        AssertLeg(storno.Postings, numbers, "8400", PostingDirection.Debit, 100m, Steuerschluessel.Ust19);
        AssertLeg(storno.Postings, numbers, "1776", PostingDirection.Debit, 19m, null);
    }

    [Fact]
    public async Task PostingEngine_rejects_an_unbalanced_source_before_saving()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);
        var accountId = await db.Set<Account>()
            .Where(account => account.Number == "1200")
            .Select(account => account.Id)
            .SingleAsync();
        var source = new FixedPostingSource([
            new Posting
            {
                TenantId = tenant,
                AccountId = accountId,
                Amount = 1m,
                Direction = PostingDirection.Debit,
            },
        ]);

        var exception = await Assert.ThrowsAsync<LedgerImbalanceException>(() =>
            new PostingEngine(db).PostAsync(
                source,
                Header(tenant, "unbalanced", PostingType.Normal),
                CancellationToken.None));

        Assert.Contains("Soll 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Set<JournalEntry>().CountAsync());
    }

    [Fact]
    public async Task Missing_mapped_account_names_number_and_variant_clearly()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);

        // The tenant seeded the SKR03 chart, so resolving an SKR04 revenue account
        // (4400) has no matching row and must fail with a clear, named exception.
        // (TaxCategory.K now resolves to the seeded 8125/4125 i.g.-Lieferung accounts.)
        var exception = Assert.Throws<LedgerAccountNotFoundException>(() =>
            new AccountResolver(db).ResolveRevenue(ChartVariant.Skr04, TaxCategory.S, 19m));

        Assert.Contains("4400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Skr04", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Automatikkonto_rejects_a_conflicting_manual_tax_key()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = await CreateSeededContextAsync(tenant);

        var exception = Assert.Throws<LedgerTaxKeyConflictException>(() =>
            new AccountResolver(db).ResolveExpense(ChartVariant.Skr03, 7m, "3400"));

        Assert.Contains("3400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Vst7", exception.Message, StringComparison.Ordinal);
    }

    private async Task<NumeraDbContext> CreateSeededContextAsync(Guid tenant)
    {
        var db = fixture.CreateAppContext(tenant);
        Assert.True(await new ChartSeeder(db).SeedAsync(ChartVariant.Skr03, tenant, CancellationToken.None));
        return db;
    }

    private static InvoicePostingSource InvoiceSource(NumeraDbContext db, InvoicePostingInput input) =>
        new(input, new AccountResolver(db));

    private static InvoicePostingInput InvoiceInput(
        Guid tenant,
        IReadOnlyList<InvoicePostingBreakdown> breakdowns,
        decimal totalGross,
        bool reverseCharge = false) =>
        new(
            tenant,
            Guid.CreateVersion7(),
            "RE-2026-000001",
            EntryDate,
            ChartVariant.Skr03,
            null,
            breakdowns,
            totalGross,
            false,
            reverseCharge);

    private static JournalEntry Header(
        Guid tenant,
        string sourceRef,
        PostingType postingType,
        Guid? reversesEntryId = null) =>
        new()
        {
            TenantId = tenant,
            EntryDate = EntryDate,
            SourceRef = sourceRef,
            SourceType = LedgerSourceType.Invoice,
            Description = postingType == PostingType.Storno ? "Storno" : "Invoice",
            PostingType = postingType,
            ReversesEntryId = reversesEntryId,
        };

    private static async Task<IReadOnlyDictionary<Guid, string>> AccountNumbersAsync(NumeraDbContext db) =>
        await db.Set<Account>().AsNoTracking().ToDictionaryAsync(account => account.Id, account => account.Number);

    private static void AssertBalanced(IEnumerable<Posting> postings)
    {
        var list = postings.ToList();
        var debit = list.Where(posting => posting.Direction == PostingDirection.Debit).Sum(posting => posting.Amount);
        var credit = list.Where(posting => posting.Direction == PostingDirection.Credit).Sum(posting => posting.Amount);
        Assert.Equal(debit, decimal.Round(debit, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(credit, decimal.Round(credit, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(debit, credit);
    }

    private static Posting AssertLeg(
        IEnumerable<Posting> postings,
        IReadOnlyDictionary<Guid, string> accountNumbers,
        string accountNumber,
        PostingDirection direction,
        decimal amount,
        Steuerschluessel? key) =>
        Assert.Single(postings, posting =>
            accountNumbers[posting.AccountId] == accountNumber &&
            posting.Direction == direction &&
            posting.Amount == amount &&
            posting.Steuerschluessel == key);

    private sealed class FixedPostingSource(IReadOnlyList<Posting> postings) : IPostingSource
    {
        public IReadOnlyList<Posting> BuildPostings() => postings;
    }
}
