using FluentValidation;

using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Numera.Api.Auth;
using Numera.Api.Endpoints;
using Numera.Api.Events;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;

// QuestPDF requires a license type to be acknowledged before the first render or it
// throws (RESEARCH.md Pitfall 1). Numera qualifies for the free Community license
// (gross revenue < $1M). Set once at startup for the whole host.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

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
builder.Services.AddScoped<ICurrentUserRole, CurrentUserRole>();
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
// Shared render entrypoint: loads the finalized doc under RLS, renders the §14 PDF from the
// frozen snapshot (+ live logo), and stores it idempotently in document_render. Used by the
// on-demand GET /{id}/pdf download and (04-03 Task 2) the Hangfire render job.
builder.Services.AddScoped<DocumentPdfService>();
// Finalize → PDF hook: the publisher (fired after the finalize commit) resolves this handler
// from scope; it ENQUEUES RenderDocumentPdfJob on the Api default queue (never renders inline,
// so finalize is not blocked). The job re-establishes tenant context before any RLS-scoped work.
builder.Services.AddTransient<RenderDocumentPdfJob>();
builder.Services.AddScoped<IDomainEventHandler<InvoiceFinalized>, EnqueuePdfOnFinalize>();
// --- E-mail dispatch (plan 04-04, DOCS-03) ---------------------------------
// The IEmailSender seam over MailKit/SMTP, configured from the "Email" section (Mailpit
// locally). SendDocumentEmailJob re-establishes tenant context, renders-if-absent and sends
// the PDF; POST /api/documents/{id}/send enqueues it after recording a Queued document_email.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddTransient<SendDocumentEmailJob>();
// --- E-invoice validation (plan 05-02, EINV-03) ----------------------------
// The IEInvoiceValidator seam over the KoSIT validator sidecar. A typed HttpClient POSTs the
// e-invoice XML to the daemon and parses the report into a structured Accepted/Rejected/Unavailable
// result with DE/EN-explained findings; an outage surfaces as Unavailable (never a false Rejected).
// 05-03 wires this into the two-stage send gate.
builder.Services.Configure<EInvoiceValidationOptions>(
    builder.Configuration.GetSection(EInvoiceValidationOptions.SectionName));
builder.Services.AddHttpClient<IEInvoiceValidator, KoSitValidatorClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<EInvoiceValidationOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
// --- E-invoice generation + two-stage gate (plan 05-03, EINV-01/EINV-03) ----
// EInvoiceService generates UBL+CII from the frozen snapshot, validates via IEInvoiceValidator
// and stores the bytes + status in document_einvoice (idempotent). The finalize → e-invoice hook
// (fired after the finalize commit) resolves EnqueueEInvoiceOnFinalize from scope and ENQUEUES
// GenerateEInvoiceJob on the Api default queue (never inline, only for a Rechnung) — coexisting
// with EnqueuePdfOnFinalize. The job re-establishes tenant context before any RLS-scoped work.
builder.Services.AddScoped<EInvoiceService>();
builder.Services.AddTransient<GenerateEInvoiceJob>();
builder.Services.AddScoped<IDomainEventHandler<InvoiceFinalized>, EnqueueEInvoiceOnFinalize>();
// --- Inbound e-invoicing (plan 05-05, EINV-04/EINV-05) ----------------------
// InboundEInvoiceService ingests a received XRechnung/ZUGFeRD: parse (detect → PdfPig extract →
// InvoiceDescriptor.Load) → validate via IEInvoiceValidator (reused 05-02 seam) → match the
// supplier by VAT id (SupplierMatcher) → store the immutable original + read-model + verdict in
// inbound_document (RLS). POST /api/inbound-documents uploads; GET list/detail/original read.
builder.Services.AddScoped<SupplierMatcher>();
builder.Services.AddScoped<InboundEInvoiceService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<DunningConfigService>();
builder.Services.AddTransient<SendDunningNoticeJob>();
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
builder.Services.AddScoped<InvitationService>();
builder.Services.AddTransient<WelcomeEmailJob>();
// Recurring-invoice generation job (07-06 infra; body in 07-07). Registered like every other
// Hangfire job so the per-template recurring schedule resolves it from DI, not just ActivatorUtilities.
builder.Services.AddTransient<GenerateRecurringInvoiceJob>();

// --- BFF authentication (cookie + Keycloak OIDC, tokens server-side) --------
builder.Services.AddKeycloakBff(builder.Configuration);
builder.Services.AddScoped<IAuthorizationHandler, RequireOwnerHandler>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(
        "RequireOwner",
        policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new RequireOwnerRequirement())));

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
app.UseMiddleware<ReadOnlyWriteGuardMiddleware>();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapPartnerEndpoints();
app.MapCatalogEndpoints();
app.MapCompanyProfileEndpoints();
app.MapSalesDocumentEndpoints();
app.MapEInvoiceEndpoints();
app.MapInboundDocumentEndpoints();
app.MapOpenItemEndpoints();
app.MapPaymentEndpoints();
app.MapDunningEndpoints();
app.MapRecurringInvoiceEndpoints();
app.MapTeamEndpoints();

app.Run();
