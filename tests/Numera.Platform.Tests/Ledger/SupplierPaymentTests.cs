using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Api.Services;
using Numera.IntegrationTests;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.Belege.Payments;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

/// <summary>Exercises the actual posting, settlement and cash SQL paths under PostgreSQL RLS.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SupplierPaymentTests(PostgresFixture fixture)
{
    private static readonly DateOnly BookingDate = new(2026, 1, 12);
    private static readonly DateOnly PaymentDate = new(2026, 2, 10);

    [Theory]
    [InlineData(119, 0, ReceiptPaymentStatus.Paid)]
    [InlineData(59.5, 59.5, ReceiptPaymentStatus.PartiallyPaid)]
    public async Task Record_and_reverse_preserve_original_and_restore_payable(
        decimal amount, decimal expectedOpen, ReceiptPaymentStatus expectedStatus)
    {
        var (tenant, receiptId) = await SetupAsync();
        var paymentId = await PayAsync(tenant, receiptId, amount);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var receipt = await db.Set<Receipt>().SingleAsync();
            Assert.Equal(expectedOpen, receipt.OpenAmount);
            Assert.Equal(expectedStatus, receipt.PaymentStatus);
            var entry = await EntryAsync(db, paymentId);
            Assert.Equal(PaymentDate, entry.EntryDate);
            var numbers = await db.Set<Account>().ToDictionaryAsync(account => account.Id, account => account.Number);
            AssertLeg(entry.Postings, numbers, "1600", PostingDirection.Debit, amount);
            AssertLeg(entry.Postings, numbers, "1200", PostingDirection.Credit, amount);
        }

        Guid reversalId;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var result = await Service(db, tenant).ReverseAsync(receiptId, paymentId);
            Assert.Equal(PaymentOperationStatus.Success, result.Status);
            reversalId = result.PaymentId!.Value;
        }

        await using (var db = fixture.CreateAppContext(tenant))
        {
            var receipt = await db.Set<Receipt>().SingleAsync();
            Assert.Equal(119m, receipt.OpenAmount);
            Assert.Equal(ReceiptPaymentStatus.Unpaid, receipt.PaymentStatus);
            var original = await db.Set<SupplierPayment>().SingleAsync(payment => payment.Id == paymentId);
            var reversal = await db.Set<SupplierPayment>().SingleAsync(payment => payment.Id == reversalId);
            Assert.Equal(amount, original.Amount);
            Assert.Equal(-amount, reversal.Amount);
            Assert.Equal(paymentId, reversal.ReversesPaymentId);
            Assert.Equal(0m, await db.Set<SupplierPaymentAllocation>().SumAsync(allocation => allocation.AllocatedAmount));
            var entry = await EntryAsync(db, reversalId);
            Assert.Equal((await EntryAsync(db, paymentId)).Id, entry.ReversesEntryId);
            Assert.Equal(PostingType.Storno, entry.PostingType);
            var numbers = await db.Set<Account>().ToDictionaryAsync(account => account.Id, account => account.Number);
            AssertLeg(entry.Postings, numbers, "1600", PostingDirection.Credit, amount);
            AssertLeg(entry.Postings, numbers, "1200", PostingDirection.Debit, amount);
            var second = await Service(db, tenant).ReverseAsync(receiptId, paymentId);
            Assert.Equal(PaymentOperationStatus.Conflict, second.Status);
        }
    }

    [Theory]
    [InlineData(false, ChartVariant.Skr03, "3400", "27", 100, 19)]
    [InlineData(true, ChartVariant.Skr03, "3400", "27", 119, 0)]
    [InlineData(false, ChartVariant.Skr03, "4980", "60", 100, 19)]
    [InlineData(false, ChartVariant.Skr04, "5400", "27", 100, 19)]
    [InlineData(true, ChartVariant.Skr04, "6300", "60", 119, 0)]
    public async Task Euer_recognizes_booked_account_and_input_vat_only_when_paid(
        bool smallBusiness, ChartVariant chart, string account, string line, decimal expense, decimal inputVat)
    {
        var (tenant, receiptId) = await SetupAsync(smallBusiness, chart, account);
        await PayAsync(tenant, receiptId, 119m);
        await using var db = fixture.CreateAppContext(tenant);
        var calculator = new EuerCalculator(db, new RecognitionReader(db));
        var unpaidWindow = await calculator.ComputeAsync(2026, new(2026, 1, 1), new(2026, 1, 31), default);
        Assert.Equal(0m, unpaidWindow.SummeAusgaben);
        var report = await calculator.ComputeAsync(2026, PaymentDate, PaymentDate, default);
        Assert.Equal(expense, Assert.Single(report.Betriebsausgaben, row => row.Zeile == line).Betrag);
        Assert.Equal(119m, report.SummeAusgaben);
        Assert.Equal(-119m, report.Gewinn);
        Assert.False(report.IsExpenseDataIncomplete);
        Assert.Null(report.Hinweis);
        if (smallBusiness)
        {
            Assert.DoesNotContain(report.Betriebsausgaben, row => row.Zeile == "57");
        }
        else
        {
            Assert.Equal(inputVat, Assert.Single(report.Betriebsausgaben, row => row.Zeile == "57").Betrag);
        }
    }

    [Fact]
    public async Task Euer_routes_an_unmapped_expense_account_to_sonstige_without_throwing()
    {
        // 4210 exists in the chart but has no explicit EuerLineMap entry. The report must
        // fold it into "Übrige unbeschränkt abziehbare Betriebsausgaben" (Zeile 60) and never 500.
        var (tenant, receiptId) = await SetupAsync(expenseAccount: "4210", extraExpenseAccount: "4210");
        await PayAsync(tenant, receiptId, 119m);
        await using var db = fixture.CreateAppContext(tenant);

        var report = await new EuerCalculator(db, new RecognitionReader(db))
            .ComputeAsync(2026, PaymentDate, PaymentDate, default);

        Assert.Equal(100m, Assert.Single(report.Betriebsausgaben, row => row.Zeile == "60").Betrag);
        Assert.Equal(19m, Assert.Single(report.Betriebsausgaben, row => row.Zeile == "57").Betrag);
        Assert.Equal(119m, report.SummeAusgaben);
        Assert.False(report.IsExpenseDataIncomplete);
    }

    [Fact]
    public async Task Partial_payments_and_reversal_recognize_pro_rata_on_their_own_dates()
    {
        var (tenant, receiptId) = await SetupAsync();
        var paymentId = await PayAsync(tenant, receiptId, 59.5m);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var report = await new EuerCalculator(db, new RecognitionReader(db))
                .ComputeAsync(2026, PaymentDate, PaymentDate, default);
            Assert.Equal(50m, Assert.Single(report.Betriebsausgaben, row => row.Zeile == "60").Betrag);
            Assert.Equal(9.5m, Assert.Single(report.Betriebsausgaben, row => row.Zeile == "57").Betrag);
            Assert.Equal(PaymentOperationStatus.Success,
                (await Service(db, tenant).ReverseAsync(receiptId, paymentId)).Status);
        }

        await using var read = fixture.CreateAppContext(tenant);
        var reversal = await read.Set<SupplierPayment>().SingleAsync(payment => payment.ReversesPaymentId == paymentId);
        var rows = await new RecognitionReader(read).ReadExpenseCashRecognitionAsync(reversal.ValueDate, reversal.ValueDate, default);
        var row = Assert.Single(rows);
        Assert.Equal(-50m, row.NetAmount);
        Assert.Equal(-9.5m, row.VatAmount);
        var netted = await new EuerCalculator(read, new RecognitionReader(read))
            .ComputeAsync(2026, PaymentDate, reversal.ValueDate, default);
        Assert.Equal(0m, netted.SummeAusgaben);
    }

    [Fact]
    public async Task Invalid_and_cross_tenant_payments_do_not_change_payable()
    {
        var (tenant, receiptId) = await SetupAsync();
        await using (var db = fixture.CreateAppContext(tenant))
        {
            Assert.Equal(PaymentOperationStatus.Invalid,
                (await Service(db, tenant).RecordAsync(receiptId, Request(120m))).Status);
            Assert.Empty(await db.Set<SupplierPayment>().ToListAsync());
            Assert.Equal(119m, (await db.Set<Receipt>().SingleAsync()).OpenAmount);
        }

        var other = Guid.CreateVersion7();
        await using var foreign = fixture.CreateAppContext(other);
        Assert.Equal(PaymentOperationStatus.NotFound,
            (await Service(foreign, other).RecordAsync(receiptId, Request(119m))).Status);
        var paymentId = await PayAsync(tenant, receiptId, 119m);
        Assert.Empty(await foreign.Set<SupplierPayment>().IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await foreign.Set<SupplierPaymentAllocation>().IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await new RecognitionReader(foreign).ReadExpenseCashRecognitionAsync(PaymentDate, PaymentDate, default));
        Assert.Equal(PaymentOperationStatus.NotFound,
            (await Service(foreign, other).ReverseAsync(receiptId, paymentId)).Status);

        await using var own = fixture.CreateAppContext(tenant);
        var protectedTables = await own.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*)::integer AS "Value" FROM pg_class
             WHERE relname IN ('supplier_payment', 'supplier_payment_allocation')
               AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync();
        Assert.Equal(2, protectedTables);
        await Assert.ThrowsAsync<PostgresException>(() => own.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_payment WHERE id = {paymentId}"));
        await Assert.ThrowsAsync<PostgresException>(() => own.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_payment_allocation WHERE payment_id = {paymentId}"));
    }

    [Fact]
    public async Task Missing_ledger_keeps_setup_required_report()
    {
        await using var db = fixture.CreateAppContext(Guid.CreateVersion7());
        var report = await new EuerCalculator(db, new RecognitionReader(db))
            .ComputeAsync(2026, PaymentDate, PaymentDate, default);
        Assert.True(report.IsExpenseDataIncomplete);
        Assert.Contains("Kontenrahmen", report.Hinweis);
    }

    private async Task<(Guid Tenant, Guid Receipt)> SetupAsync(
        bool smallBusiness = false, ChartVariant chart = ChartVariant.Skr03, string? expenseAccount = null,
        string? extraExpenseAccount = null)
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);
        await new ChartSeeder(db).SeedAsync(chart, tenant);
        db.Add(new LedgerSettings { TenantId = tenant, ChartVariant = chart });
        if (extraExpenseAccount is not null)
        {
            // A real expense account that exists in the chart (so booking succeeds) but
            // is NOT in EuerLineMap — models the Steuerberater extending the chart.
            db.Add(new Account
            {
                TenantId = tenant, ChartVariant = chart, Number = extraExpenseAccount,
                Name = "Zusatzaufwand", Type = AccountType.Expense, IsActive = true,
            });
        }
        db.Add(new CompanyProfile
        {
            TenantId = tenant, LegalName = "Supplier payment test", IsKleinunternehmer = smallBusiness,
            Address = new() { Street = "Test 1", PostalCode = "10115", City = "Berlin", CountryCode = "DE" },
        });
        var receipt = new Receipt
        {
            TenantId = tenant, ContentHash = Guid.NewGuid().ToString(), Status = ReceiptStatus.Reviewed,
            ReviewedByUserId = Guid.NewGuid(), NetAmount = 100m, VatAmount = 19m, GrossAmount = 119m,
            VatRatePercent = 19m, InvoiceDate = BookingDate, ExpenseAccountOverride = expenseAccount,
        };
        db.Add(receipt);
        await db.SaveChangesAsync();
        var result = await ReceiptEndpoints.ConfirmBookAsync(receipt.Id, db, new PostingEngine(db),
            new AccountResolver(db), new NoOpAudit(), Tenant(tenant), NullLoggerFactory.Instance, default);
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(119m, receipt.OpenAmount);
        Assert.Equal(ReceiptPaymentStatus.Unpaid, receipt.PaymentStatus);
        return (tenant, receipt.Id);
    }

    private async Task<Guid> PayAsync(Guid tenant, Guid receipt, decimal amount)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var result = await Service(db, tenant).RecordAsync(receipt, Request(amount));
        Assert.Equal(PaymentOperationStatus.Success, result.Status);
        return result.PaymentId!.Value;
    }

    private static RecordSupplierPaymentRequest Request(decimal amount) =>
        new(amount, PaymentDate, PaymentMethod.BankTransfer, "SUPPLIER-TEST");

    private static TenantContext Tenant(Guid id)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(id);
        return tenant;
    }

    private static SupplierPaymentService Service(NumeraDbContext db, Guid tenant) =>
        new(db, Tenant(tenant), new NoOpAudit(), new PostingEngine(db), new AccountResolver(db));

    private static Task<JournalEntry> EntryAsync(NumeraDbContext db, Guid payment) =>
        db.Set<JournalEntry>().Include(entry => entry.Postings).SingleAsync(entry => entry.SourceRef == payment.ToString());

    private static void AssertLeg(IEnumerable<Posting> postings, Dictionary<Guid, string> numbers,
        string account, PostingDirection direction, decimal amount) =>
        Assert.Single(postings, posting => numbers[posting.AccountId] == account
            && posting.Direction == direction && posting.Amount == amount);

    private sealed class NoOpAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent auditEvent, CancellationToken ct = default) => Task.CompletedTask;
    }
}
