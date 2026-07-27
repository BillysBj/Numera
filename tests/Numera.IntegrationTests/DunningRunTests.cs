using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Numera.Api.Endpoints;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Dunning;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Real Postgres + Mailpit proof of the manual dunning run and send workflow.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DunningRunTests(PostgresFixture fixture)
{
    static DunningRunTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    [Fact]
    public async Task Run_is_idempotent_escalates_keeps_ancillary_claims_separate_and_sends_frozen_recipient()
    {
        var tenant = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        const string recipient = "mahnung@example.com";
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100);

        await SalesTestData.SeedProfileAsync(fixture, tenant);
        await SalesTestData.SeedFormatAsync(fixture, tenant, DocumentType.Rechnung, "RE-");
        var partner = await SalesTestData.SeedPartnerAsync(fixture, tenant, netDays: 1);
        await using (var update = fixture.CreateAppContext(tenant))
        {
            var tracked = await update.Set<BusinessPartner>().FirstAsync(x => x.Id == partner.Id);
            tracked.Email = recipient;
            await update.SaveChangesAsync();
        }

        var documentId = await SalesTestData.SeedDraftAsync(
            fixture,
            tenant,
            DocumentType.Rechnung,
            partner.Id,
            [new SalesTestData.LineSpec("Beratung", 1m, 100m, TaxCategory.S, 19m)],
            oldDate);
        await using (var db = fixture.CreateAppContext(tenant))
        {
            await SalesTestData.FinalizeAsync(db, documentId);
        }

        Guid openItemId;
        decimal principal;
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var item = await db.Set<OpenItem>().SingleAsync(x => x.DocumentId == documentId);
            openItemId = item.Id;
            principal = item.OpenAmount;
            _ = await CreateConfigService(db, tenant).GetConfigAsync();
        }

        // Remove the live e-mail after finalize: dispatch must fall back to the frozen snapshot.
        await using (var db = fixture.CreateAppContext(tenant))
        {
            var tracked = await db.Set<BusinessPartner>().FirstAsync(x => x.Id == partner.Id);
            tracked.Email = null;
            await db.SaveChangesAsync();
        }

        var enqueued = new CapturingBackgroundJobs();
        await RunAsync(tenant, enqueued);

        await using (var read = fixture.CreateAppContext(tenant))
        {
            var first = await read.Set<DunningNotice>().AsNoTracking().SingleAsync();
            Assert.Equal(1, first.Level);
            Assert.Equal(5m, first.Fee);
            Assert.Equal(0m, first.Interest);
            Assert.Equal(principal + first.Fee, first.TotalToPay);
            Assert.Equal(principal, await read.Set<OpenItem>()
                .Where(x => x.Id == openItemId).Select(x => x.OpenAmount).SingleAsync());
            var state = await ReadStateAsync(read, openItemId);
            Assert.Equal(1, state.Level);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), state.LastDunnedOn);
        }

        // Same-day re-run is idempotent and does not enqueue a second level.
        await RunAsync(tenant, enqueued);
        await using (var read = fixture.CreateAppContext(tenant))
        {
            Assert.Equal(1, await read.Set<DunningNotice>().CountAsync());
        }
        Assert.Single(enqueued.Jobs);

        // A later-run simulation: yesterday's last-dunned date permits level 2, whose interest is charged.
        await SetLastDunnedAsync(tenant, openItemId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await RunAsync(tenant, enqueued);
        Guid levelTwoNoticeId;
        await using (var read = fixture.CreateAppContext(tenant))
        {
            var notices = await read.Set<DunningNotice>().AsNoTracking().OrderBy(x => x.Level).ToListAsync();
            Assert.Equal([1, 2], notices.Select(x => x.Level));
            var second = notices[1];
            levelTwoNoticeId = second.Id;
            var days = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - oldDate.AddDays(1).DayNumber;
            var expectedInterest = Math.Round(
                principal * second.InterestRatePercent / 100m * days / 365m,
                2,
                MidpointRounding.AwayFromZero);
            Assert.Equal(expectedInterest, second.Interest);
            Assert.Equal(principal + second.Fee + second.Interest, second.TotalToPay);
            Assert.Equal(principal, await read.Set<OpenItem>()
                .Where(x => x.Id == openItemId).Select(x => x.OpenAmount).SingleAsync());
        }

        // The unique (tenant, open item, level) backstop rejects a duplicate.
        await using (var db = fixture.CreateAppContext(tenant))
        {
            db.Add(new DunningNotice
            {
                TenantId = tenant,
                OpenItemId = openItemId,
                DocumentId = documentId,
                Level = 2,
                IssuedOn = DateOnly.FromDateTime(DateTime.UtcNow),
                NewDueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                OverdueAmount = principal,
                TotalToPay = principal,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using (var other = fixture.CreateAppContext(otherTenant))
        {
            Assert.Equal(0, await other.Set<DunningNotice>().IgnoreQueryFilters().CountAsync());
        }
        var otherJobs = new CapturingBackgroundJobs();
        await RunAsync(otherTenant, otherJobs);
        Assert.Empty(otherJobs.Jobs);

        await using var mailpit = new ContainerBuilder("axllent/mailpit:latest")
            .WithPortBinding(1025, assignRandomHostPort: true)
            .WithPortBinding(8025, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(x => x.ForPort(8025).ForPath("/api/v1/messages")))
            .Build();
        await mailpit.StartAsync();
        await RunSendJobAsync(
            tenant,
            levelTwoNoticeId,
            mailpit.Hostname,
            mailpit.GetMappedPublicPort(1025));

        await using (var read = fixture.CreateAppContext(tenant))
        {
            var sent = await read.Set<DunningNotice>().AsNoTracking()
                .SingleAsync(x => x.Id == levelTwoNoticeId);
            Assert.Equal(1, sent.Status);
            Assert.NotNull(sent.SentAt);
            Assert.NotNull(sent.RenderedPdf);
            Assert.True(sent.RenderedPdf!.Length > 1_000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(sent.RenderedPdf, 0, 4));
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}"),
        };
        var detail = await WaitForMessageAsync(http);
        Assert.Equal(recipient, detail.To);
        Assert.Contains("Mahnung", detail.Subject, StringComparison.Ordinal);
        Assert.Contains("pdf", detail.AttachmentContentType, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunAsync(Guid tenant, CapturingBackgroundJobs jobs)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenant);
        _ = await DunningEndpoints.RunAsync(
            db,
            tenantContext,
            new NoOpAuditWriter(),
            jobs,
            CancellationToken.None);
    }

    private async Task SetLastDunnedAsync(Guid tenant, Guid openItemId, DateOnly date)
    {
        await using var db = fixture.CreateAppContext(tenant);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE open_items SET last_dunned_on = {date} WHERE id = {openItemId}");
    }

    private static async Task<(int Level, DateOnly? LastDunnedOn)> ReadStateAsync(
        NumeraDbContext db,
        Guid openItemId)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT current_dunning_level, last_dunned_on FROM open_items WHERE id = @id";
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", openItemId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1));
    }

    private async Task RunSendJobAsync(Guid tenant, Guid noticeId, string host, int port)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(x => x.UseNpgsql(fixture.AppConnectionString));
        services.Configure<EmailOptions>(x =>
        {
            x.Host = host;
            x.Port = port;
            x.UseSsl = false;
            x.FromAddress = "noreply@numera.local";
            x.FromName = "Numera";
        });
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        await using var provider = services.BuildServiceProvider();
        var job = new SendDunningNoticeJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SendDunningNoticeJob>.Instance);
        await job.RunAsync(tenant, noticeId, "de", CancellationToken.None);
    }

    private static DunningConfigService CreateConfigService(NumeraDbContext db, Guid tenant)
    {
        var context = new TenantContext();
        context.SetTenant(tenant);
        return new DunningConfigService(db, context, new NoOpAuditWriter());
    }

    private static async Task<(string? To, string? Subject, string AttachmentContentType)> WaitForMessageAsync(
        HttpClient http)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var list = JsonDocument.Parse(await http.GetStringAsync("/api/v1/messages"));
            var messages = list.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() == 1)
            {
                var item = messages[0];
                var id = item.GetProperty("ID").GetString();
                using var detail = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{id}"));
                var attachment = Assert.Single(
                    detail.RootElement.GetProperty("Attachments").EnumerateArray());
                return (
                    item.GetProperty("To")[0].GetProperty("Address").GetString(),
                    item.GetProperty("Subject").GetString(),
                    attachment.GetProperty("ContentType").GetString() ?? string.Empty);
            }

            await Task.Delay(250);
        }

        throw new Xunit.Sdk.XunitException("Mailpit did not receive the dunning notice.");
    }

    private sealed class CapturingBackgroundJobs : IBackgroundJobClient
    {
        public List<Job> Jobs { get; } = [];

        public string Create(Job job, IState state)
        {
            Jobs.Add(job);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
