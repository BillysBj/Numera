using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class UgRuecklageReportTests
{
    [Theory]
    [InlineData(Gewinnermittlungsart.Bilanz, true, 4000, 4000)]
    [InlineData(Gewinnermittlungsart.Bilanz, true, null, 5000)]
    [InlineData(Gewinnermittlungsart.Bilanz, false, 4000, 0)]
    [InlineData(Gewinnermittlungsart.Euer, true, 4000, 0)]
    [InlineData(null, true, 4000, 0)]
    public async Task Preview_reuses_guv_and_preserves_gates(
        Gewinnermittlungsart? method, bool active, int? lossValue, decimal expected)
    {
        decimal? loss = lossValue;
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        if (method is { } value)
        {
            db.Add(new LedgerSettings { TenantId = tenant.TenantId!.Value, Gewinnermittlungsart = value, UgRuecklagepflichtAktiv = active });
        }

        var revenue = new Account { TenantId = tenant.TenantId!.Value, Number = "8400", Name = "Revenue", Type = AccountType.Revenue };
        var bank = new Account { TenantId = tenant.TenantId.Value, Number = "1200", Name = "Bank", Type = AccountType.Asset };
        var entry = new JournalEntry { TenantId = tenant.TenantId.Value, EntryDate = new(2026, 6, 1), SourceRef = "profit", Description = "Reserve test profit" };
        db.AddRange(revenue, bank, entry,
            new Posting { TenantId = tenant.TenantId.Value, JournalEntryId = entry.Id, AccountId = bank.Id, Direction = PostingDirection.Debit, Amount = 20000m },
            new Posting { TenantId = tenant.TenantId.Value, JournalEntryId = entry.Id, AccountId = revenue.Id, Direction = PostingDirection.Credit, Amount = 20000m });
        await db.SaveChangesAsync();
        var abschluss = new AbschlussCalculator(db);
        var guv = await abschluss.ComputeGuvAsync(2026, default);
        var report = Assert.IsType<Ok<UgRuecklageReport>>(await ReportEndpoints.GetUgRuecklageAsync(
            2026, loss, db, abschluss, new UgRuecklageCalculator(), default)).Value!;
        Assert.Equal(expected, report.Ruecklage);
        if (guv.Hinweis is not null)
        {
            Assert.Equal(new UgRuecklageReport(2026, 0m, 0m, 0m, 0m, guv.Hinweis), report);
        }
        else
        {
            Assert.Equal(20000m, report.Jahresueberschuss);
            Assert.Equal(loss ?? 0m, report.VerlustvortragVorjahr);
            Assert.Equal(20000m - (loss ?? 0m), report.MassgeblicherBetrag);
            Assert.Equal(active, report.Hinweis is null);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9999, 0)]
    [InlineData(2026, -1)]
    [InlineData(2026, 0.00001)]
    [InlineData(2026, 1000000000000000)]
    public async Task Invalid_input_is_rejected_before_database_access(int year, decimal loss)
    {
        var result = await ReportEndpoints.GetUgRuecklageAsync(year, loss, null!, null!, new UgRuecklageCalculator(), default);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        result = await ReportEndpoints.BookUgRuecklageAsync(year, new(loss), null!, null!, new UgRuecklageCalculator(),
            null!, null!, null!, default);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void Omitted_flag_defaults_to_active_and_explicit_false_is_preserved()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.True(JsonSerializer.Deserialize<LedgerSetupRequest>("{}", options)!.UgRuecklagepflichtAktiv);
        Assert.True(JsonSerializer.Deserialize<LedgerSettingsUpdateRequest>("{}", options)!.UgRuecklagepflichtAktiv);
        Assert.False(JsonSerializer.Deserialize<LedgerSetupRequest>("{\"ugRuecklagepflichtAktiv\":false}", options)!.UgRuecklagepflichtAktiv);
        Assert.False(JsonSerializer.Deserialize<LedgerSettingsUpdateRequest>("{\"ugRuecklagepflichtAktiv\":false}", options)!.UgRuecklagepflichtAktiv);
    }
}
