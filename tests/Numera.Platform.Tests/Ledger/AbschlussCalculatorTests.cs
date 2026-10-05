using Microsoft.EntityFrameworkCore;

using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class AbschlussCalculatorTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly DateOnly EntryDate = new(2026, 6, 1);

    [Theory]
    [InlineData(ChartVariant.Skr03, "1200", "0800", "8400", "4980", "1700")]
    [InlineData(ChartVariant.Skr04, "1800", "2900", "4400", "6300", "3500")]
    public async Task Balanced_entries_present_natural_signs_and_fold_profit_into_equity(
        ChartVariant chart, string bankNumber, string equityNumber, string revenueNumber, string expenseNumber, string liabilityNumber)
    {
        await using var db = CreateDb(chart);
        var bank = AddAccount(db, bankNumber, AccountType.Asset, chart);
        var equity = AddAccount(db, equityNumber, AccountType.Equity, chart);
        var revenue = AddAccount(db, revenueNumber, AccountType.Revenue, chart);
        var expense = AddAccount(db, expenseNumber, AccountType.Expense, chart);
        var liability = AddAccount(db, liabilityNumber, AccountType.Liability, chart);
        AddEntry(db, EntryDate, bank, equity, 1000m);
        AddEntry(db, EntryDate, bank, revenue, 300m);
        AddEntry(db, EntryDate, expense, bank, 80m);
        AddEntry(db, EntryDate, bank, liability, 200m);
        // Contra-postings reduce the natural-side balances rather than using absolute values.
        AddEntry(db, EntryDate, revenue, bank, 50m);
        AddEntry(db, EntryDate, bank, expense, 10m);
        revenue.IsActive = false; // Existing postings still contribute after deactivation.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(2026, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(2026, CancellationToken.None);

        Assert.Equal(250m, Assert.Single(guv.Ertraege).Betrag);
        Assert.Equal(70m, Assert.Single(guv.Aufwendungen).Betrag);
        Assert.Equal(180m, guv.Jahresueberschuss);
        Assert.Equal(1380m, bilanz.SummeAktiva);
        Assert.Equal(bilanz.SummeAktiva, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
        Assert.Equal(1180m, bilanz.Passiva.Where(line => line.Gruppe == "Eigenkapital").Sum(line => line.Betrag));
        Assert.Equal(200m, Assert.Single(bilanz.Passiva, line => line.Gruppe == "Verbindlichkeiten").Betrag);
        Assert.Equal(guv.Jahresueberschuss, Assert.Single(bilanz.Passiva, line => line.Bezeichnung == "Jahresüberschuss").Betrag);
        Assert.Null(guv.Hinweis);
        Assert.Null(bilanz.Hinweis);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(6, await db.Set<JournalEntry>().CountAsync());
        Assert.Equal(12, await db.Set<Posting>().CountAsync());
    }

    [Fact]
    public async Task Contra_balances_and_losses_keep_their_negative_sign()
    {
        await using var db = CreateDb();
        var bank = AddAccount(db, "1200", AccountType.Asset);
        var revenue = AddAccount(db, "8400", AccountType.Revenue);
        var expense = AddAccount(db, "4980", AccountType.Expense);
        AddEntry(db, EntryDate, revenue, bank, 100m);
        AddEntry(db, EntryDate, bank, expense, 20m);
        await db.SaveChangesAsync();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(2026, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(2026, CancellationToken.None);

        Assert.Equal(-100m, Assert.Single(guv.Ertraege).Betrag);
        Assert.Equal(-20m, Assert.Single(guv.Aufwendungen).Betrag);
        Assert.Equal(-80m, guv.Jahresueberschuss);
        Assert.Equal(-80m, bilanz.SummeAktiva);
        Assert.Equal(-80m, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
    }

    [Theory]
    [InlineData(1, 2026, 12, 31)]
    [InlineData(4, 2027, 3, 31)]
    [InlineData(3, 2028, 2, 29)]
    public async Task Fiscal_year_is_inclusive_and_bilanz_keeps_opening_stock(
        int startMonth, int endYear, int endMonth, int endDay)
    {
        var jahr = startMonth == 3 ? 2027 : 2026;
        var from = new DateOnly(jahr, startMonth, 1);
        var to = new DateOnly(endYear, endMonth, endDay);
        await using var db = CreateDb(startMonth: startMonth);
        var bank = AddAccount(db, "1200", AccountType.Asset);
        var equity = AddAccount(db, "0800", AccountType.Equity);
        var revenue = AddAccount(db, "8400", AccountType.Revenue);
        AddEntry(db, from.AddDays(-1), bank, equity, 1000m);
        AddEntry(db, from, bank, revenue, 100m);
        AddEntry(db, to, bank, revenue, 200m);
        AddEntry(db, to.AddDays(1), bank, revenue, 900m);
        await db.SaveChangesAsync();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(jahr, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(jahr, CancellationToken.None);

        Assert.Equal(from, guv.From);
        Assert.Equal(to, guv.To);
        Assert.Equal(to, bilanz.Stichtag);
        Assert.Equal(300m, guv.Jahresueberschuss);
        Assert.Equal(1300m, bilanz.SummeAktiva);
        Assert.Equal(1300m, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
    }

    [Fact]
    public async Task Prior_year_unclosed_profit_is_exposed_as_difference_without_throwing()
    {
        await using var db = CreateDb();
        var bank = AddAccount(db, "1200", AccountType.Asset);
        var revenue = AddAccount(db, "8400", AccountType.Revenue);
        AddEntry(db, new DateOnly(2025, 12, 31), bank, revenue, 100m);
        await db.SaveChangesAsync();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(2026, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(2026, CancellationToken.None);

        Assert.Equal(0m, guv.Jahresueberschuss);
        Assert.Equal(100m, bilanz.SummeAktiva);
        Assert.Equal(0m, bilanz.SummePassiva);
        Assert.Equal(100m, bilanz.BilanzDifferenz);
    }

    [Theory]
    [InlineData(ChartVariant.Skr03)]
    [InlineData(ChartVariant.Skr04)]
    public async Task Unknown_numbers_fall_back_and_round_after_grouping(ChartVariant chart)
    {
        await using var db = CreateDb(chart);
        var bank = AddAccount(db, "CUSTOM-BANK", AccountType.Asset, chart);
        var revenue = AddAccount(db, "CUSTOM-REVENUE", AccountType.Revenue, chart);
        var secondRevenue = AddAccount(db, "9999", AccountType.Revenue, chart);
        var expense = AddAccount(db, "CUSTOM-EXPENSE", AccountType.Expense, chart);
        AddEntry(db, EntryDate, bank, revenue, 10.004m);
        AddEntry(db, EntryDate, bank, secondRevenue, 10.004m);
        AddEntry(db, EntryDate, expense, bank, 5.006m);
        await db.SaveChangesAsync();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(2026, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(2026, CancellationToken.None);

        Assert.Equal(new GuvPosition("Sonstige betriebliche Erträge", 20.01m), Assert.Single(guv.Ertraege));
        Assert.Equal(new GuvPosition("Sonstige betriebliche Aufwendungen", 5.01m), Assert.Single(guv.Aufwendungen));
        Assert.Equal(15m, guv.Jahresueberschuss);
        Assert.Equal("Umlaufvermögen", Assert.Single(bilanz.Aktiva).Gruppe);
        Assert.Equal(15m, bilanz.SummeAktiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
    }

    [Theory]
    [InlineData(null, ChartVariant.Skr03)]
    [InlineData(Gewinnermittlungsart.Euer, ChartVariant.Skr03)]
    [InlineData(Gewinnermittlungsart.Euer, ChartVariant.Skr04)]
    [InlineData(Gewinnermittlungsart.Bilanz, (ChartVariant)0)]
    public async Task Missing_setup_and_Euer_return_empty_hint_reports(Gewinnermittlungsart? method, ChartVariant chart)
    {
        await using var db = CreateDb(chart, method);
        var bank = AddAccount(db, "1200", AccountType.Asset);
        var revenue = AddAccount(db, "8400", AccountType.Revenue);
        AddEntry(db, EntryDate, bank, revenue, 100m);
        await db.SaveChangesAsync();

        var calculator = new AbschlussCalculator(db);
        var guv = await calculator.ComputeGuvAsync(2026, CancellationToken.None);
        var bilanz = await calculator.ComputeBilanzAsync(2026, CancellationToken.None);

        Assert.Empty(guv.Ertraege);
        Assert.Empty(guv.Aufwendungen);
        Assert.Equal(0m, guv.Jahresueberschuss);
        Assert.Empty(bilanz.Aktiva);
        Assert.Empty(bilanz.Passiva);
        Assert.Equal(0m, bilanz.SummeAktiva);
        Assert.Equal(0m, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
        Assert.Contains(method == Gewinnermittlungsart.Euer ? "bilanzierender" : "nicht eingerichtet", guv.Hinweis);
        Assert.Equal(guv.Hinweis, bilanz.Hinweis);
    }

    private static NumeraDbContext CreateDb(
        ChartVariant chart = ChartVariant.Skr03,
        Gewinnermittlungsart? method = Gewinnermittlungsart.Bilanz,
        int startMonth = 1)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(TenantId);
        var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        if (method is { } value)
        {
            db.Add(new LedgerSettings
            {
                TenantId = TenantId, ChartVariant = chart, Gewinnermittlungsart = value,
                FiscalYearStartMonth = startMonth,
            });
        }

        return db;
    }

    private static Account AddAccount(NumeraDbContext db, string number, AccountType type, ChartVariant chart = ChartVariant.Skr03)
    {
        var account = new Account { TenantId = TenantId, Number = number, Name = number, Type = type, ChartVariant = chart };
        db.Add(account);
        return account;
    }

    private static void AddEntry(NumeraDbContext db, DateOnly date, Account debit, Account credit, decimal amount)
    {
        var entry = new JournalEntry
        {
            TenantId = TenantId, EntryDate = date, SourceRef = Guid.NewGuid().ToString(), Description = "Testbuchung",
        };
        entry.Postings.Add(new Posting
        {
            TenantId = TenantId, JournalEntryId = entry.Id, AccountId = debit.Id, Amount = amount, Direction = PostingDirection.Debit,
        });
        entry.Postings.Add(new Posting
        {
            TenantId = TenantId, JournalEntryId = entry.Id, AccountId = credit.Id, Amount = amount, Direction = PostingDirection.Credit,
        });
        db.Add(entry);
    }
}
