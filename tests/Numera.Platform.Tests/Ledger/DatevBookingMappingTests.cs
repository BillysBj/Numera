using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

/// <summary>Exercise actual posting-source shapes against both seeded SKR charts without a database server.</summary>
public sealed class DatevBookingMappingTests
{
    [Theory]
    [InlineData(ChartVariant.Skr03, false)]
    [InlineData(ChartVariant.Skr04, false)]
    [InlineData(ChartVariant.Skr03, true)]
    [InlineData(ChartVariant.Skr04, true)]
    public async Task Invoice_emits_one_gross_row_per_rate_and_no_output_vat(ChartVariant chart, bool reversal)
    {
        await using var fixture = await Fixture.CreateAsync(chart);
        var postings = fixture.Invoice([
            new(TaxCategory.S, 19m, 100m, 19m),
            new(TaxCategory.S, 7m, 200m, 14m),
        ], reversal: reversal);
        Assert.Equal(5, postings.Count);
        var rows = fixture.Rows(postings);
        Assert.Equal(2, rows.Length);
        var debtor = SkrMapping.StandardAccount(chart, StandardAccountKind.Debtor);
        AssertRow(Assert.Single(rows, x => x[8] == "3"), "119,00", reversal ? "H" : "S", debtor,
            chart == ChartVariant.Skr03 ? "8400" : "4400", "3");
        AssertRow(Assert.Single(rows, x => x[8] == "2"), "214,00", reversal ? "H" : "S", debtor,
            chart == ChartVariant.Skr03 ? "8300" : "4300", "2");
        Assert.Equal(333m, SumAmounts(rows));
        AssertNoVatAccounts(rows, chart);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, false)]
    [InlineData(ChartVariant.Skr04, false)]
    [InlineData(ChartVariant.Skr03, true)]
    [InlineData(ChartVariant.Skr04, true)]
    public async Task Expense_emits_gross_per_rate_with_input_vat_key(ChartVariant chart, bool reversal)
    {
        await using var fixture = await Fixture.CreateAsync(chart);
        var postings = new ExpensePostingSource(fixture.TenantId, chart,
            new ExpensePostingInput(null, null, [new(19m, 100m, 19m), new(7m, 200m, 14m)], new(2026, 9, 1)),
            new AccountResolver(fixture.Db)).BuildPostings();
        if (reversal)
        {
            foreach (var posting in postings)
            {
                posting.Direction = posting.Direction == PostingDirection.Debit
                    ? PostingDirection.Credit : PostingDirection.Debit;
            }
        }

        Assert.Equal(5, postings.Count);
        var rows = fixture.Rows(postings);
        Assert.Equal(2, rows.Length);
        var expense = chart == ChartVariant.Skr03 ? "4980" : "6300";
        var creditor = SkrMapping.StandardAccount(chart, StandardAccountKind.Creditor);
        AssertRow(Assert.Single(rows, x => x[8] == "9"), "119,00", reversal ? "H" : "S", expense, creditor, "9");
        AssertRow(Assert.Single(rows, x => x[8] == "8"), "214,00", reversal ? "H" : "S", expense, creditor, "8");
        Assert.Equal(333m, SumAmounts(rows));
        AssertNoVatAccounts(rows, chart);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03)]
    [InlineData(ChartVariant.Skr04)]
    public async Task Repeated_rate_legs_share_the_recorded_tax_once(ChartVariant chart)
    {
        await using var fixture = await Fixture.CreateAsync(chart);
        var rows = fixture.Rows(fixture.Invoice([
            new(TaxCategory.S, 19m, 100m, 19m),
            new(TaxCategory.S, 19m, 50m, 9.5m),
        ]));
        Assert.Equal(2, rows.Length);
        Assert.Equal(["119,00", "59,50"], rows.Select(x => x[0]).Order(StringComparer.Ordinal));
        Assert.All(rows, x => Assert.Equal("3", x[8]));
        Assert.Equal(178.5m, SumAmounts(rows));
        AssertNoVatAccounts(rows, chart);
    }

    [Theory]
    [InlineData(TaxCategory.E, false, "")]
    [InlineData(TaxCategory.S, true, "")]
    // §13b reverse charge: no automatic BU-Schlüssel is emitted (20 is not a valid DATEV
    // key); the Buchhaltungsbüro assigns the correct §13b key manually on that line.
    [InlineData(TaxCategory.AE, false, "")]
    public async Task Exempt_small_business_and_reverse_charge_do_not_invent_vat(
        TaxCategory category, bool smallBusiness, string key)
    {
        await using var fixture = await Fixture.CreateAsync(ChartVariant.Skr03);
        var rows = fixture.Rows(fixture.Invoice([new(category, 19m, 100m, 0m)], smallBusiness: smallBusiness));
        AssertRow(Assert.Single(rows), "100,00", "S", "1400", "8200", key);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03, false)]
    [InlineData(ChartVariant.Skr04, false)]
    [InlineData(ChartVariant.Skr03, true)]
    [InlineData(ChartVariant.Skr04, true)]
    public async Task Payment_and_reversal_pair_the_two_legs_once_without_vat(ChartVariant chart, bool reversal)
    {
        await using var fixture = await Fixture.CreateAsync(chart);
        var postings = new PaymentPostingSource(fixture.TenantId, chart,
            new PaymentPostingInput(null, new(2026, 9, 1), [new(null, 119m)], reversal),
            new AccountResolver(fixture.Db)).BuildPostings();
        var rows = fixture.Rows(postings);
        var bank = SkrMapping.StandardAccount(chart, StandardAccountKind.Bank);
        var debtor = SkrMapping.StandardAccount(chart, StandardAccountKind.Debtor);
        AssertRow(Assert.Single(rows), "119,00", "S", reversal ? debtor : bank, reversal ? bank : debtor, "");
    }

    [Theory]
    [InlineData(ChartVariant.Skr03)]
    [InlineData(ChartVariant.Skr04)]
    public async Task Fallback_pairs_non_vat_debits_with_largest_non_vat_credit(ChartVariant chart)
    {
        await using var fixture = await Fixture.CreateAsync(chart);
        var bank = SkrMapping.StandardAccount(chart, StandardAccountKind.Bank);
        var cash = SkrMapping.StandardAccount(chart, StandardAccountKind.Kasse);
        var debtor = SkrMapping.StandardAccount(chart, StandardAccountKind.Debtor);
        var creditor = SkrMapping.StandardAccount(chart, StandardAccountKind.Creditor);
        var rows = fixture.Rows([
            fixture.Leg(bank, 70m, PostingDirection.Debit),
            fixture.Leg(cash, 30m, PostingDirection.Debit),
            fixture.Leg(SkrMapping.ExpenseMapping(chart, 19m).VorsteuerAccount!, 190m, PostingDirection.Debit),
            fixture.Leg(debtor, 80m, PostingDirection.Credit),
            fixture.Leg(creditor, 20m, PostingDirection.Credit),
            fixture.Leg(SkrMapping.RevenueMapping(chart, TaxCategory.S, 19m).UstAccount!, 190m, PostingDirection.Credit),
        ]);
        Assert.Equal(2, rows.Length);
        AssertRow(Assert.Single(rows, x => x[6] == bank), "70,00", "S", bank, debtor, "");
        AssertRow(Assert.Single(rows, x => x[6] == cash), "30,00", "S", cash, debtor, "");
        AssertNoVatAccounts(rows, chart);
    }

    [Fact]
    public async Task Unfoldable_vat_is_skipped_and_custom_account_numbers_are_preserved()
    {
        await using var fixture = await Fixture.CreateAsync(ChartVariant.Skr03);
        var postings = fixture.Invoice([new(TaxCategory.S, 19m, 100m, 19m)]);
        var revenue = Assert.Single(postings, x => fixture.Accounts[x.AccountId].Type == AccountType.Revenue);
        revenue.Steuerschluessel = null;
        fixture.Accounts[revenue.AccountId].Number = "800";
        var debtor = Assert.Single(postings, x => x.Direction == PostingDirection.Debit);
        fixture.Accounts[debtor.AccountId].Number = "10001";
        var rows = fixture.Rows(postings);
        AssertRow(Assert.Single(rows), "100,00", "S", "10001", "0800", "");
        AssertNoVatAccounts(rows, ChartVariant.Skr03);
    }

    private static decimal SumAmounts(string[][] rows) =>
        rows.Sum(x => decimal.Parse(x[0], CultureInfo.GetCultureInfo("de-DE")));

    private static void AssertRow(string[] row, string amount, string direction, string account, string counter, string key)
    {
        Assert.Equal(116, row.Length);
        Assert.Equal(amount, row[0]);
        Assert.Equal($"\"{direction}\"", row[1]);
        Assert.Equal("\"EUR\"", row[2]);
        Assert.Equal(account, row[6]);
        Assert.Equal(counter, row[7]);
        Assert.Equal(key, row[8]);
        Assert.Equal("0109", row[9]);
        Assert.Equal("\"RE-42\"", row[10]);
    }

    private static void AssertNoVatAccounts(string[][] rows, ChartVariant chart)
    {
        foreach (var rate in new[] { 7m, 19m })
        {
            var output = SkrMapping.RevenueMapping(chart, TaxCategory.S, rate).UstAccount;
            var input = SkrMapping.ExpenseMapping(chart, rate).VorsteuerAccount;
            Assert.All(rows, row =>
            {
                Assert.DoesNotContain(output, row.Skip(6).Take(2));
                Assert.DoesNotContain(input, row.Skip(6).Take(2));
            });
        }
    }

    private sealed class Fixture(NumeraDbContext db, Guid tenantId, ChartVariant chart, Dictionary<Guid, Account> accounts)
        : IAsyncDisposable
    {
        public NumeraDbContext Db => db;
        public Guid TenantId => tenantId;
        public Dictionary<Guid, Account> Accounts => accounts;

        public static async Task<Fixture> CreateAsync(ChartVariant chart)
        {
            var tenant = new TenantContext();
            var tenantId = Guid.CreateVersion7();
            tenant.SetTenant(tenantId);
            var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
            await new ChartSeeder(db).SeedAsync(chart, tenantId);
            return new Fixture(db, tenantId, chart, await db.Set<Account>().AsNoTracking().ToDictionaryAsync(x => x.Id));
        }

        public IReadOnlyList<Posting> Invoice(
            IReadOnlyList<InvoicePostingBreakdown> breakdowns, bool reversal = false, bool smallBusiness = false) =>
            new InvoicePostingSource(new InvoicePostingInput(tenantId, Guid.CreateVersion7(), "RE-42", new(2026, 9, 1),
                chart, null, breakdowns, breakdowns.Sum(x => x.TaxableBase + x.TaxAmount), smallBusiness, false),
                new AccountResolver(db), reversal).BuildPostings();

        public Posting Leg(string number, decimal amount, PostingDirection direction) => new()
        {
            AccountId = accounts.Values.Single(x => x.Number == number).Id,
            Amount = amount, Direction = direction,
        };

        public string[][] Rows(IReadOnlyList<Posting> postings)
        {
            var entry = new JournalEntry
            {
                SourceRef = "RE-42", Description = "Testbuchung", EntryDate = new(2026, 9, 1), Postings = postings.ToList(),
            };
            var lines = DatevExportService.MapEntry(entry, accounts, "RE-42");
            var bytes = DatevExport.Render(new LedgerSettings { ChartVariant = chart }, new(2026, 9, 1), new(2026, 9, 30),
                lines, DateTimeOffset.UtcNow);
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes)
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(2).Select(x => x.Split(';')).ToArray();
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
