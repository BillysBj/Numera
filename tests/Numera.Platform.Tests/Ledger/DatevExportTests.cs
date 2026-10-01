using System.Text;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Ledger;

public sealed class DatevExportTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 12, 30, 45, TimeSpan.Zero);
    private static readonly Encoding Cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    [Fact]
    public void Header_and_posting_rows_follow_extf_700_field_order_and_encoding()
    {
        var bytes = DatevExport.Render(new LedgerSettings(), new(2026, 1, 1), new(2026, 9, 30),
        [
            new(1234.56m, PostingDirection.Debit, "1400", "8400", null, new(2026, 9, 3), "RE-42", "Büro"),
            new(1234.56m, PostingDirection.Credit, "8400", "1400", Steuerschluessel.Ust19,
                new(2026, 9, 3), "RE-42", "Büro"),
        ], CreatedAt);
        var rows = Cp1252.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, rows.Length);
        Assert.Equal(
            "\"EXTF\";700;21;\"Buchungsstapel\";7;20261001123045000;;\"RE\";\"Numera\";;0;0;20260101;4;20260101;20260930;\"Numera SKR03\";;1;0;0;\"EUR\"" + new string(';', 9),
            rows[0]);
        Assert.Equal(116, DatevExport.Columns.Count);
        Assert.Equal(116, rows[1].Split(';').Length);
        Assert.Equal("\"Datum Zuord. Steuerperiode\"", rows[1].Split(';')[115]);
        Assert.Equal(
            "1234,56;\"S\";\"EUR\";;;;1400;8400;;0309;\"RE-42\";;;\"Büro\"" + new string(';', 102),
            rows[2]);
        Assert.Equal(
            "1234,56;\"H\";\"EUR\";;;;8400;1400;3;0309;\"RE-42\";;;\"Büro\"" + new string(';', 102),
            rows[3]);
        Assert.Contains((byte)0xFC, bytes); // ü is a single CP1252 byte, not UTF-8 C3 BC.
        Assert.DoesNotContain((byte)0xC3, bytes);
    }

    [Fact]
    public void Skr04_and_non_calendar_fiscal_year_are_encoded_in_the_metadata()
    {
        var bytes = DatevExport.Render(new LedgerSettings
        {
            ChartVariant = ChartVariant.Skr04, FiscalYearStartMonth = 4,
        }, new(2026, 1, 1), new(2026, 3, 31), [], CreatedAt);
        var header = Cp1252.GetString(bytes).Split("\r\n")[0].Split(';');
        Assert.Equal("20250401", header[12]);
        Assert.Equal("4", header[13]);
        Assert.Equal("20260101", header[14]);
        Assert.Equal("20260331", header[15]);
        Assert.Equal("\"Numera SKR04\"", header[16]);
    }

    [Fact]
    public void Text_is_quoted_and_newlines_do_not_create_additional_records()
    {
        var bytes = DatevExport.Render(new LedgerSettings(), new(2026, 1, 1), new(2026, 1, 31),
        [new(10.125m, PostingDirection.Debit, "1200", "1400", Steuerschluessel.None,
            new(2026, 1, 2), "RE;\"1\"", "Öl; \"Spezial\"\n€")], CreatedAt);
        var rows = Cp1252.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, rows.Length);
        Assert.StartsWith("10,13;\"S\";\"EUR\";;;;1200;1400;;0201;\"RE;\"\"1\"\"\";;;\"Öl; \"\"Spezial\"\" €\"", rows[2]);
        Assert.Contains((byte)0x80, bytes); // Euro sign in CP1252.
    }

    [Theory]
    [InlineData("2026-02-01", "2026-01-01")]
    [InlineData("2025-12-31", "2026-01-01")]
    public void Invalid_or_cross_year_ranges_are_rejected(string from, string to)
    {
        Assert.Throws<ArgumentException>(() => DatevExport.Render(new LedgerSettings(),
            DateOnly.Parse(from), DateOnly.Parse(to), [], CreatedAt));
    }

    [Fact]
    public async Task Service_resolves_accounts_and_document_numbers_and_filters_tenant_and_dates()
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.CreateVersion7());
        await using var db = new NumeraDbContext(new DbContextOptionsBuilder<NumeraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var debit = new Account { TenantId = tenant.TenantId!.Value, Number = "1400", Name = "Debitor", Type = AccountType.Asset };
        var credit = new Account { TenantId = tenant.TenantId.Value, Number = "8200", Name = "Umsatz", Type = AccountType.Revenue };
        var document = new SalesDocument { TenantId = tenant.TenantId.Value, DocumentNumber = "RE-007" };
        db.AddRange(debit, credit, document);
        foreach (var (tenantId, date, description) in new[]
        {
            (tenant.TenantId.Value, new DateOnly(2026, 9, 1), "Im Zeitraum"),
            (tenant.TenantId.Value, new DateOnly(2026, 8, 31), "Außerhalb"),
            (Guid.CreateVersion7(), new DateOnly(2026, 9, 1), "Anderer Mandant"),
        })
        {
            var entry = new JournalEntry
            {
                TenantId = tenantId, EntryDate = date, SourceRef = document.Id.ToString(),
                SourceType = LedgerSourceType.Invoice, Description = description,
            };
            db.Add(entry);
            db.AddRange(
                new Posting { TenantId = tenantId, JournalEntryId = entry.Id, AccountId = debit.Id,
                    Amount = 100m, Direction = PostingDirection.Debit },
                new Posting { TenantId = tenantId, JournalEntryId = entry.Id, AccountId = credit.Id,
                    Amount = 100m, Direction = PostingDirection.Credit, Steuerschluessel = Steuerschluessel.None });
        }

        await db.SaveChangesAsync();
        var bytes = await new DatevExportService(db).ExportAsync(new LedgerSettings(),
            new(2026, 9, 1), new(2026, 9, 30), CancellationToken.None);
        var csv = Cp1252.GetString(bytes);
        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("100,00;\"S\";\"EUR\";;;;1400;8200;;0109;\"RE-007\"", csv);
        Assert.DoesNotContain("Außerhalb", csv);
        Assert.DoesNotContain("Anderer Mandant", csv);
    }
}
