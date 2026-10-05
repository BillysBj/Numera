using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Endpoints;
using Numera.Api.Reporting;
using Numera.Modules.Ledger;
using Numera.Platform.Db;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real report route delegates and PostgreSQL queries under the RLS-subject app role.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AbschlussReportEndpointsTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(ChartVariant.Skr03)]
    [InlineData(ChartVariant.Skr04)]
    public async Task Routes_return_200_with_profit_and_balanced_closing_date_stock(ChartVariant chart)
    {
        var tenant = await SeedAsync(chart, Gewinnermittlungsart.Bilanz, 300m);
        await using var app = CreateApp(tenant);

        var guv = await InvokeAsync<GuvReport>(app, "/api/reports/guv");
        var bilanz = await InvokeAsync<BilanzReport>(app, "/api/reports/bilanz");

        Assert.Equal(new DateOnly(2026, 4, 1), guv.From);
        Assert.Equal(new DateOnly(2027, 3, 31), guv.To);
        Assert.Equal(guv.To, bilanz.Stichtag);
        Assert.Equal(300m, Assert.Single(guv.Ertraege).Betrag);
        Assert.Equal(80m, Assert.Single(guv.Aufwendungen).Betrag);
        Assert.Equal(220m, guv.Jahresueberschuss);
        Assert.Equal(1220m, bilanz.SummeAktiva);
        Assert.Equal(1220m, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
        Assert.Equal(220m, Assert.Single(bilanz.Passiva, line => line.Bezeichnung == "Jahresüberschuss").Betrag);
        Assert.Null(guv.Hinweis);
        Assert.Null(bilanz.Hinweis);

        await using var db = fixture.CreateAppContext(tenant);
        Assert.Equal(4, await db.Set<JournalEntry>().CountAsync());
        Assert.Equal(8, await db.Set<Posting>().CountAsync());
    }

    [Theory]
    [InlineData(Gewinnermittlungsart.Euer)]
    [InlineData(null)]
    public async Task Euer_and_missing_setup_return_200_with_empty_hint_reports(Gewinnermittlungsart? method)
    {
        var tenant = await SeedAsync(ChartVariant.Skr03, method, 300m);
        await using var app = CreateApp(tenant);

        var guv = await InvokeAsync<GuvReport>(app, "/api/reports/guv");
        var bilanz = await InvokeAsync<BilanzReport>(app, "/api/reports/bilanz");

        Assert.Empty(guv.Ertraege);
        Assert.Empty(guv.Aufwendungen);
        Assert.Equal(0m, guv.Jahresueberschuss);
        Assert.Empty(bilanz.Aktiva);
        Assert.Empty(bilanz.Passiva);
        Assert.Equal(0m, bilanz.SummeAktiva);
        Assert.Equal(0m, bilanz.SummePassiva);
        Assert.Equal(0m, bilanz.BilanzDifferenz);
        Assert.Contains(method is null ? "nicht eingerichtet" : "bilanzierender", guv.Hinweis);
        Assert.Equal(guv.Hinweis, bilanz.Hinweis);
    }

    [Fact]
    public async Task Reports_and_Rls_keep_settings_and_postings_tenant_scoped()
    {
        var tenantA = await SeedAsync(ChartVariant.Skr03, Gewinnermittlungsart.Bilanz, 300m);
        var tenantB = await SeedAsync(ChartVariant.Skr04, Gewinnermittlungsart.Bilanz, 900m);
        await using var appA = CreateApp(tenantA);
        await using var appB = CreateApp(tenantB);
        await using var unconfiguredApp = CreateApp(Guid.CreateVersion7());

        var guvA = await InvokeAsync<GuvReport>(appA, "/api/reports/guv");
        var guvB = await InvokeAsync<GuvReport>(appB, "/api/reports/guv");
        var bilanzA = await InvokeAsync<BilanzReport>(appA, "/api/reports/bilanz");
        var bilanzB = await InvokeAsync<BilanzReport>(appB, "/api/reports/bilanz");
        var missingGuv = await InvokeAsync<GuvReport>(unconfiguredApp, "/api/reports/guv");
        var missingBilanz = await InvokeAsync<BilanzReport>(unconfiguredApp, "/api/reports/bilanz");

        Assert.Equal(220m, guvA.Jahresueberschuss);
        Assert.Equal(820m, guvB.Jahresueberschuss);
        Assert.Equal(1220m, bilanzA.SummeAktiva);
        Assert.Equal(1820m, bilanzB.SummeAktiva);
        Assert.Equal(0m, bilanzA.BilanzDifferenz);
        Assert.Equal(0m, bilanzB.BilanzDifferenz);
        Assert.NotNull(missingGuv.Hinweis);
        Assert.NotNull(missingBilanz.Hinweis);
        Assert.Empty(missingGuv.Ertraege);
        Assert.Empty(missingBilanz.Aktiva);

        // Disable EF's defence-in-depth filters to prove the database RLS itself
        // hides all four joined/configuration tables as the least-privilege role.
        await using var db = fixture.CreateAppContext(tenantA);
        Assert.Equal(tenantA, (await db.Set<LedgerSettings>().IgnoreQueryFilters().SingleAsync()).TenantId);
        Assert.All(await db.Set<Account>().IgnoreQueryFilters().ToListAsync(), account => Assert.Equal(tenantA, account.TenantId));
        Assert.Equal(4, await db.Set<JournalEntry>().IgnoreQueryFilters().CountAsync());
        Assert.All(await db.Set<JournalEntry>().IgnoreQueryFilters().ToListAsync(), entry => Assert.Equal(tenantA, entry.TenantId));
        Assert.Equal(8, await db.Set<Posting>().IgnoreQueryFilters().CountAsync());
        Assert.All(await db.Set<Posting>().IgnoreQueryFilters().ToListAsync(), posting => Assert.Equal(tenantA, posting.TenantId));
    }

    private WebApplication CreateApp(Guid tenant)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddScoped(_ => fixture.CreateAppContext(tenant));
        builder.Services.AddScoped<RecognitionReader>();
        builder.Services.AddScoped<UstVaCalculator>();
        builder.Services.AddScoped<EuerCalculator>();
        builder.Services.AddScoped<AbschlussCalculator>();
        builder.Services.AddScoped<UgRuecklageCalculator>();
        builder.Services.AddScoped<PostingEngine>();
        builder.Services.AddScoped<Numera.Platform.Audit.IAuditWriter, NoOpAuditWriter>();
        builder.Services.AddScoped<Numera.Platform.Tenancy.ICurrentTenant, Numera.Platform.Tenancy.TenantContext>();
        var app = builder.Build();
        app.MapReportEndpoints();
        return app;
    }

    private static async Task<T> InvokeAsync<T>(WebApplication app, string path)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>().Single(candidate => candidate.RoutePattern.RawText == path);
        Assert.Contains("GET", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = "GET";
        http.Request.Path = path;
        http.Request.QueryString = new QueryString("?jahr=2026");
        http.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(http);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<T>(http.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private async Task<Guid> SeedAsync(ChartVariant chart, Gewinnermittlungsart? method, decimal revenueAmount)
    {
        var tenant = Guid.CreateVersion7();
        await using var db = fixture.CreateAppContext(tenant);
        if (method is { } value)
        {
            db.Add(new LedgerSettings
            {
                TenantId = tenant, ChartVariant = chart, Gewinnermittlungsart = value, FiscalYearStartMonth = 4,
            });
        }

        var bank = Account(chart == ChartVariant.Skr03 ? "1200" : "1800", AccountType.Asset);
        var equity = Account(chart == ChartVariant.Skr03 ? "0800" : "2900", AccountType.Equity);
        var revenue = Account(chart == ChartVariant.Skr03 ? "8400" : "4400", AccountType.Revenue);
        var expense = Account(chart == ChartVariant.Skr03 ? "4980" : "6300", AccountType.Expense);
        db.AddRange(bank, equity, revenue, expense);
        AddEntry(new DateOnly(2026, 3, 31), bank, equity, 1000m);
        AddEntry(new DateOnly(2026, 4, 1), bank, revenue, revenueAmount);
        AddEntry(new DateOnly(2027, 3, 31), expense, bank, 80m);
        AddEntry(new DateOnly(2027, 4, 1), bank, revenue, 5000m);
        await db.SaveChangesAsync();
        return tenant;

        Account Account(string number, AccountType type) => new()
        {
            TenantId = tenant, Number = number, Name = number, Type = type, ChartVariant = chart,
        };

        void AddEntry(DateOnly date, Account debit, Account credit, decimal amount)
        {
            var entry = new JournalEntry
            {
                TenantId = tenant, EntryDate = date, SourceRef = Guid.NewGuid().ToString(), Description = "Abschluss-Test",
            };
            entry.Postings.Add(new Posting
            {
                TenantId = tenant, JournalEntryId = entry.Id, AccountId = debit.Id, Amount = amount, Direction = PostingDirection.Debit,
            });
            entry.Postings.Add(new Posting
            {
                TenantId = tenant, JournalEntryId = entry.Id, AccountId = credit.Id, Amount = amount, Direction = PostingDirection.Credit,
            });
            db.Add(entry);
        }
    }
}
