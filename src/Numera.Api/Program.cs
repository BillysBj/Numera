using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;

using Numera.Api.Auth;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// --- Tenancy + data access -------------------------------------------------
// ICurrentTenant is scoped: one mutable holder per request, set by
// TenantResolutionMiddleware from the organization claim. NumeraDbContext resolves
// it from DI and self-registers the TenantConnectionInterceptor (app.current_tenant
// GUC) in its OnConfiguring, so RLS receives the tenant on every connection.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, TenantContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddDbContext<NumeraDbContext>(options => options.UseNpgsql(connectionString));

// --- Audit + entitlements --------------------------------------------------
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddFeatureManagement().AddFeatureFilter<PlanFeatureFilter>();

// --- BFF authentication (cookie + Keycloak OIDC, tokens server-side) --------
builder.Services.AddKeycloakBff(builder.Configuration);
builder.Services.AddAuthorization();

// --- Background jobs (Hangfire, Postgres-backed) ---------------------------
// The Api hosts a server on the default queue and both enqueues and executes the
// trivial welcome-email job (proving enqueue-after-commit + tenant re-establishment).
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();

// Liveness probe — intentionally unauthenticated and tenant-agnostic.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseAuthentication();
app.UseAuthorization();

app.Run();
