using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Numera.Api.Jobs;
using Numera.Api.Services;

using Numera.Modules.Banking;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

// Background worker host. Registers the same DbContext + tenancy seam as the Api so
// that a background job re-establishes tenant context (ICurrentTenant.SetTenant) and
// the TenantConnectionInterceptor sets app.current_tenant for RLS inside the job.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDataProtection().SetApplicationName("Numera");
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Numera")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<SmtpPasswordProtector>();
builder.Services.AddScoped<ITenantEmailConfigResolver, TenantEmailConfigResolver>();
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// Hangfire manages its own schema (DDL) and stores infrastructure — not tenant — data,
// so it uses a schema-owning connection while the tenant-scoped NumeraDbContext stays on
// the RLS-subject "Default" (numera_app) connection.
var hangfireConnectionString = builder.Configuration.GetConnectionString("Hangfire")
    ?? connectionString;

// Tenancy + data access (mirror of the Api registrations; no HttpContext here — a
// job sets the tenant explicitly from its arguments rather than from a request).
builder.Services.AddScoped<ICurrentTenant, TenantContext>();
builder.Services.Configure<BillingOptions>(
    builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.AddDbContext<NumeraDbContext>(options => options.UseNpgsql(connectionString));

// Shared capture closure only: scanner + audit + enqueue; no extractor or ledger services.
builder.Services.AddBelegeMailboxWorker(builder.Configuration);

// Banking jobs added by later plans resolve the same stub-default port and shared
// module registration seam as the API host.
builder.Services.AddBankConnectionProvider(builder.Configuration);
builder.Services.AddBankingModule(builder.Configuration);

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString)));

// Process a dedicated "worker" queue so this server never dequeues a job type it
// cannot load (the Api hosts the default-queue server for its own job types).
builder.Services.AddHangfireServer(options => options.Queues = ["worker"]);

var host = builder.Build();

var mailbox = host.Services.GetRequiredService<IOptions<BelegeMailboxOptions>>().Value;
var recurringJobs = host.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<PollBelegMailboxJob>(
    "belege-mailbox:poll",
    job => job.RunAsync(CancellationToken.None),
    string.IsNullOrWhiteSpace(mailbox.PollCron) ? "*/5 * * * *" : mailbox.PollCron);
var bankSyncCron = builder.Configuration["Banking:SyncCron"];
recurringJobs.AddOrUpdate<SyncBankTransactionsFanOutJob>(
    "bank-transactions:sync-fan-out",
    job => job.RunAsync(CancellationToken.None),
    string.IsNullOrWhiteSpace(bankSyncCron) ? "0 * * * *" : bankSyncCron);
recurringJobs.AddOrUpdate<CheckBankConsentJob>(
    "bank-connections:check-consent",
    job => job.RunAllAsync(CancellationToken.None),
    "0 6 * * *");

host.Run();
