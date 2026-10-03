using FluentValidation;

using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Numera.Api.Auth;
using Numera.Api.Endpoints;
using Numera.Api.Events;
using Numera.Api.Jobs;
using Numera.Api.Reporting;
using Numera.Api.Services;
using Numera.Api.Services.Stripe;
using Numera.Modules.Banking;
using Numera.Modules.Banking.Import;
using Numera.Modules.Banking.Reconciliation;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales.Belege;
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

var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services
        .AddDataProtection()
        .SetApplicationName("Numera")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

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
builder.Services.Configure<BillingOptions>(
    builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<IBillingState, BillingStateService>();

// --- Ledger setup ---------------------------------------------------------
// Materializes the selected embedded SKR03/SKR04 chart in the caller's tenant
// transaction. The request pipeline owns tenant resolution and the RLS GUC.
builder.Services.AddScoped<ChartSeeder>();
builder.Services.AddScoped<AccountResolver>();
builder.Services.AddScoped<PostingEngine>();
builder.Services.AddScoped<FestschreibungService>();

// --- Reporting (Phase 11 integration) --------------------------------------
// Reporting DI is deliberately centralized here: the calculators share the same
// request-scoped RLS DbContext and RecognitionReader.
builder.Services.AddScoped<RecognitionReader>();
builder.Services.AddScoped<UstVaCalculator>();
builder.Services.AddScoped<EuerCalculator>();

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
builder.Services.AddScoped<DunningNoticePdfService>();
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
builder.Services.Configure<BelegeMailboxOptions>(
    builder.Configuration.GetSection(BelegeMailboxOptions.SectionName));
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
// --- Receipt extraction + attachment scanning (plan 12-03, D1/D4) ---------
// The zero-cloud StubReceiptExtractor is the shipping default (D1). Azure is an
// explicit config opt-in and replaces it only when both endpoint and API key exist.
builder.Services.Configure<AzureDocumentIntelligenceOptions>(
    builder.Configuration.GetSection(AzureDocumentIntelligenceOptions.SectionName));
var documentIntelligence = builder.Configuration.GetSection(
    AzureDocumentIntelligenceOptions.SectionName);
if (!string.IsNullOrWhiteSpace(documentIntelligence[nameof(AzureDocumentIntelligenceOptions.Endpoint)])
    && !string.IsNullOrWhiteSpace(documentIntelligence[nameof(AzureDocumentIntelligenceOptions.ApiKey)]))
{
    builder.Services.AddScoped<IReceiptExtractor, AzureReceiptExtractor>();
}
else
{
    builder.Services.AddScoped<IReceiptExtractor, StubReceiptExtractor>();
}

// --- Banking provider (plan 13-05, D1) -----------------------------------
// The no-network stub is the safe default. finAPI is an explicit opt-in and
// replaces it only when all required sandbox application settings are present.
builder.Services.AddBankConnectionProvider(builder.Configuration);
builder.Services.AddBillingProvider(builder.Configuration);
builder.Services.AddScoped<IBillingWebhookHandler, BillingWebhookHandler>();
builder.Services.AddBankingModule(builder.Configuration);
builder.Services.AddScoped<ReconciliationScorer>();
builder.Services.AddScoped<IBankStatementImporter, CsvImporter>();
builder.Services.AddScoped<IBankStatementImporter, Mt940Importer>();
builder.Services.AddScoped<IBankStatementImporter, Camt053Importer>();
builder.Services.AddScoped<BankStatementImportDispatcher>();

builder.Services.Configure<ClamAvOptions>(
    builder.Configuration.GetSection(ClamAvOptions.SectionName));
var clamAv = builder.Configuration.GetSection(ClamAvOptions.SectionName);
if (!string.IsNullOrWhiteSpace(clamAv[nameof(ClamAvOptions.Host)]))
{
    builder.Services.AddScoped<IAttachmentScanner, ClamAvAttachmentScanner>();
}
else
{
    builder.Services.AddScoped<IAttachmentScanner, NoopAttachmentScanner>();
}
// Shared upload/e-mail capture pipeline and its RLS-safe asynchronous extractor.
builder.Services.AddScoped<ReceiptDeduplicator>();
builder.Services.AddScoped<ReceiptIngestService>();
builder.Services.AddTransient<ExtractReceiptJob>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<SupplierPaymentService>();
builder.Services.AddScoped<VatPaymentService>();
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
builder.Services.AddScoped<TenantExportService>();
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

if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
#pragma warning disable ASPDEPR005 // KnownNetworks is the requested compatibility collection.
    forwardedHeadersOptions.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

// Liveness probe — intentionally unauthenticated and tenant-agnostic.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapBillingWebhook();

app.UseAuthentication();

// Map the organization claim -> tenant_id -> ICurrentTenant BEFORE authorization, so
// tenant-scoped authorization handlers (e.g. RequireOwner, which reads the RLS-scoped
// membership role) see the tenant. Running it after UseAuthorization left the tenant
// unset during authorization, so every RequireOwner endpoint (Team, Export, Ledger)
// wrongly returned 403. The middleware passes unauthenticated requests through untouched.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.UseMiddleware<BillingDegradationWriteGuardMiddleware>();
app.UseMiddleware<ReadOnlyWriteGuardMiddleware>();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapBillingEndpoints();
app.MapPartnerEndpoints();
app.MapPartnerTaskEndpoints();
app.MapCustomerFileEndpoints();
app.MapCatalogEndpoints();
app.MapCompanyProfileEndpoints();
app.MapLedgerSetupEndpoints();
app.MapLedgerEndpoints();
app.MapReportEndpoints();
app.MapDashboardEndpoints();
app.MapSalesDocumentEndpoints();
app.MapEInvoiceEndpoints();
app.MapInboundDocumentEndpoints();
app.MapReceiptEndpoints();
app.MapSupplierPaymentEndpoints();
app.MapVatPaymentEndpoints();
app.MapMailboxEndpoints();
app.MapOpenItemEndpoints();
app.MapPaymentEndpoints();
app.MapBankAccountEndpoints();
app.MapBankTransactionEndpoints();
app.MapDunningEndpoints();
app.MapRecurringInvoiceEndpoints();
app.MapTeamEndpoints();
app.MapExportEndpoints();

app.Run();
