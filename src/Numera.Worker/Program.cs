using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Numera.Platform.Db;
using Numera.Platform.Tenancy;

// Background worker host. Registers the same DbContext + tenancy seam as the Api so
// that a background job re-establishes tenant context (ICurrentTenant.SetTenant) and
// the TenantConnectionInterceptor sets app.current_tenant for RLS inside the job.
var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddDbContext<NumeraDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString)));

// Process a dedicated "worker" queue so this server never dequeues a job type it
// cannot load (the Api hosts the default-queue server for its own job types).
builder.Services.AddHangfireServer(options => options.Queues = ["worker"]);

var host = builder.Build();

host.Run();
