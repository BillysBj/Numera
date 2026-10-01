using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class SupplierPaymentPostingSourceTests
{
    [Theory]
    [InlineData(ChartVariant.Skr03, false, "1600", "1200")]
    [InlineData(ChartVariant.Skr03, true, "1600", "1200")]
    [InlineData(ChartVariant.Skr04, false, "3300", "1800")]
    [InlineData(ChartVariant.Skr04, true, "3300", "1800")]
    public async Task Posting_source_mirrors_creditor_and_bank_directions(
        ChartVariant chart, bool reversal, string creditor, string bank)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        var options = new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NumeraDbContext(options, tenant);
        await new ChartSeeder(db).SeedAsync(chart, tenant.TenantId!.Value);
        var postings = new SupplierPaymentPostingSource(tenant.TenantId.Value, chart,
            new(null, new(2026, 2, 10), [new(null, 119m)], reversal), new AccountResolver(db)).BuildPostings();
        var numbers = await db.Set<Account>().ToDictionaryAsync(account => account.Id, account => account.Number);
        Assert.Equal(2, postings.Count);
        Assert.Single(postings, posting => numbers[posting.AccountId] == creditor
            && posting.Direction == (reversal ? PostingDirection.Credit : PostingDirection.Debit)
            && posting.Amount == 119m);
        Assert.Single(postings, posting => numbers[posting.AccountId] == bank
            && posting.Direction == (reversal ? PostingDirection.Debit : PostingDirection.Credit)
            && posting.Amount == 119m);
    }
}
