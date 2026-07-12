using FluentValidation;

using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;

using Numera.Api.Auth;
using Numera.Api.Endpoints;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// Hangfire owns/creates its own schema (DDL), so it must NOT run as the least-privilege
// runtime role. Its storage tables are infrastructure, not tenant data (RLS does not
// apply), so a schema-owning connection is used here while the tenant-scoped
// NumeraDbContext above stays on the RLS-subject "Default" (numera_app) connection.
var hangfireConnectionString = builder.Configuration.GetConnectionString("Hangfire")
    ?? connectionString;

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

// --- Sales finalize services (plan 03-05) ----------------------------------
// NumberingService claims the race-safe document number inside the finalize
// transaction; the domain-event publisher fires InvoiceFinalized after commit (a
// no-op seam until a handler is registered). VatCalculationService is static.
builder.Services.AddScoped<NumberingService>();
builder.Services.AddScoped<IDomainEventPublisher, InProcessDomainEventPublisher>();
// Scoped feature management: PlanFeatureFilter consumes the scoped IEntitlementService
// (which reads the per-request tenant + DbContext), so the feature manager and its
// filters must live in the request scope — AddFeatureManagement() would register them
// as singletons and fail DI scope validation ("cannot consume scoped from singleton").
builder.Services.AddScopedFeatureManagement().AddFeatureFilter<PlanFeatureFilter>();

// --- Request validation (FluentValidation, Apache-2.0) ---------------------
// Scans THIS Api assembly for every AbstractValidator<T>, so the later Catalog
// plan (02-05) only needs to drop its validators into this assembly — no DI edit.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// --- Registration (Keycloak Admin API) + background jobs -------------------
builder.Services.AddHttpClient();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddTransient<WelcomeEmailJob>();

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
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();

// Liveness probe — intentionally unauthenticated and tenant-agnostic.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseAuthentication();
app.UseAuthorization();

// After authentication: map the organization claim -> tenant_id -> ICurrentTenant so
// RLS receives the tenant on every request.
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapPartnerEndpoints();
app.MapCatalogEndpoints();
app.MapCompanyProfileEndpoints();
app.MapSalesDocumentEndpoints();
app.MapOpenItemEndpoints();

app.Run();
