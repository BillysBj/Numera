# Phase 14: Monetarisierung (Stripe) - Research

**Researched:** 2026-08-10
**Domain:** SaaS subscription billing (Stripe Checkout + Customer Portal + webhooks) wired behind a config-gated port, driving the existing server-authoritative `tenants.plan` entitlement gate
**Confidence:** HIGH for codebase seams and Stripe.NET/API surface; MEDIUM for the exact read-only/Free enforcement mechanism (a gap in the existing entitlement model — see Open Questions)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D1 — Stripe wiring: TEST-mode live behind a port + config-gate + fake for CI**
Real Stripe.NET calls against Stripe **TEST mode**, hidden behind an `IBillingProvider` (or equivalent) port with config-gated API keys/price IDs. A **fake/stub provider** + **signed webhook fixtures** (Stripe-CLI-style, real signature verification against a test secret) so **CI never contacts Stripe**. Same architecture shape as Phase 13's finAPI-sandbox provider + `FakeBankConnectionProvider`. Flipping to live keys is a later config-only change. Webhooks are the source of truth for entitlement.

**D2 — VAT: Stripe Tax (automatic), incl. EU-B2B Reverse-Charge**
Enable **Stripe Tax**. Stripe computes German USt (19%) for domestic B2C/B2B, validates EU VAT-IDs and applies **Reverse-Charge (0%, §13b note)** for EU-B2B, and generates the subscription invoices. We **store/reference** the resulting Stripe invoices + tax lines (for the customer's records via the Portal); we do NOT re-implement VAT math. Numera's own-subscription billing is Stripe-issued — this is the company's *outbound* self-billing, separate from the tenant's accounting ledger (do NOT book Numera's own Stripe invoices into the tenant's SKR ledger).

**D3 — Trial: no-card, app-managed, 14 days**
Trial starts at signup with **full access, NO card required**. A server-authoritative `trial_ends_at` (on the tenant) drives the gate; when it lapses, access **degrades** (see D4) until the user starts a paid plan via Stripe Checkout. Conversion is an explicit user action (start paid plan), NOT an automatic card charge. Trial length = **14 days**.

**D4 — Failed-payment / trial-lapse degradation: downgrade to read-only/Free gate (NO data loss)**
After Stripe **Smart Retries + dunning** are exhausted (subscription `past_due` → `canceled`/`unpaid` webhook), OR the no-card trial lapses, the tenant's `tenants.plan` gate drops to a **read-only / Free capability set**: data stays intact and fully visible, but write/premium features lock until payment resumes/starts. **Never** a hard login wall; **never** delete or hide data. Re-activation on a subsequent successful-payment webhook restores the paid capability set.

### Claude's Discretion (freedom areas — choose the implementation)
- Exact port/interface shape + where it lives (mirror `IBankConnectionProvider`/finAPI module placement conventions).
- Stripe object modeling: number of Products/Prices, monthly vs monthly+annual intervals, whether price IDs are config or Stripe-synced. (Recommend: 4 products S/M/L/XL, config-mapped price IDs; monthly at minimum, annual optional if cheap.)
- Webhook idempotency mechanism (processed-event table vs. Stripe event-id dedupe) — mirror the Phase-13 dedupe/`Processed*` patterns.
- How the read-only/Free capability set is expressed against the existing v1 `Capability`/entitlement system + `tenants.plan`.
- Customer Portal configuration (which actions enabled: payment method, invoices, upgrade/downgrade with proration).
- Frontend: pricing/upgrade page, current-plan/trial-status surface, "Manage billing" → Portal redirect, degradation banner.
- Whether a recurring job is needed for trial-lapse detection (app-managed trial has no Stripe webhook) vs. compute-on-read gate.

### Deferred Ideas (OUT OF SCOPE — do NOT include)
- Annual/multi-year commitment discounts beyond a simple monthly (+optional annual) price — no coupon/promo-code engine this phase.
- Usage-based / metered billing, seat-based add-ons beyond the existing MultiUser capability.
- Affiliate/referral, dunning-email *content* design beyond Stripe's built-in dunning + one degradation banner.
- Booking Numera's own Stripe subscription invoices into the tenant's SKR accounting ledger (explicitly excluded per D2).
- Live-mode go-live / real card processing (test-mode only this phase; live is a later config flip).
</user_constraints>

## Summary

This phase is an independent track that makes the S/M/L/XL tiers actually sellable. Numera already has a complete, server-authoritative entitlement gate (`tenants.plan` → `Capability` set via `PlanCapabilityMap`), and Phase 13 established the exact provider-behind-a-port + config-gate + fake-for-CI + Postgres-RLS-migration pattern to mirror. The work is therefore mostly **wiring, not invention**: introduce an `IBillingProvider` port with a Stripe.NET-backed implementation gated on config, add billing columns/tables to the schema, add an unauthenticated signed webhook endpoint that resolves the tenant from the event and flips `tenants.plan`, and add the frontend pricing/upgrade/manage-billing surface.

Stripe.NET (`Stripe.net`, current stable **52.2.0**, published 2026-07-29) is the official library and works on .NET 10 (it multi-targets net6.0/net8.0/net9.0/netstandard2.0). Hosted **Checkout** (`Stripe.Checkout.SessionService`, `mode=subscription`) starts a paid plan with no card data touching Numera; the hosted **Customer Portal** (`Stripe.BillingPortal.SessionService`) covers upgrade/downgrade-with-proration, payment-method management, and invoice history (BILL-03/06); **Stripe Tax** (`automatic_tax.enabled=true` + tax-ID collection) covers German USt and EU-B2B reverse-charge with zero custom VAT math (BILL-07); and **`EventUtility.ConstructEvent`** verifies the webhook signature so signed local fixtures can drive CI without ever calling Stripe (D1/BILL-02).

The one real design decision the planner must resolve: the existing `Capability` gate only guards **premium** features (E-Invoicing, Dunning, RecurringInvoices, …). It does **not** currently block baseline create/edit of core invoices. So "degrade to read-only/Free" (D4) cannot be achieved by the capability map alone — it needs a write-blocking mechanism. Numera already has exactly such a mechanism as precedent: `ReadOnlyWriteGuardMiddleware` (Phase 8, for the TaxAdvisor role). See Open Questions #1 and the recommended approach below.

**Primary recommendation:** Add `Stripe.net` 52.2.0 to `Numera.Api`; create `IBillingProvider` (mirror `IBankConnectionProvider`) with `StripeBillingProvider` (config-gated on `Stripe:SecretKey`) + a no-network default stub + a `FakeBillingProvider` test double; add billing columns to `tenants` and a tenant-agnostic `processed_stripe_event` dedupe table; add an anonymous `POST /api/billing/webhook` endpoint that verifies the signature, dedupes by event id, resolves `tenant_id` from `client_reference_id`/`metadata`, `SetTenant`s, and flips `tenants.plan`; and add a billing-state write-guard (mirror `ReadOnlyWriteGuardMiddleware`) plus a `Free` tier so D4 read-only degradation is real. Drive the gate by **compute-on-read** for the app-managed trial (D3), with no recurring job required.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Stripe.net` | **52.2.0** (stable, 2026-07-29; NuGet lists 52.2.0) | Official Stripe SDK: Checkout, Customer Portal, Customer, Subscription, Invoice services + `EventUtility` webhook verification | Stripe's own maintained .NET client; the only supported option. Multi-targets net6/8/9 + netstandard2.0, runs on .NET 10. |
| (existing) `Microsoft.FeatureManagement` + `PlanFeatureFilter` | in-repo | Server-side capability gating already wired | Reuse — do NOT add a new gating framework (see `src/Numera.Api/Program.cs:181`). |
| (existing) `Hangfire.PostgreSql` | 1.20.* | Optional recurring/deferred billing jobs on the `worker` queue | Already the job runtime; reuse `[Queue("worker")]` + `SetTenant`-first pattern. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (existing) EF Core + Npgsql | 10.0.* | Migration for billing columns/tables + RLS SQL | Mirror `20260805130444_Banking.cs`. |
| (existing) `Microsoft.AspNetCore.DataProtection` | in-box | Not needed — Stripe keys live in config/user-secrets, not encrypted-at-rest per-tenant like finAPI credentials | Only if you decide to persist a secret; recommend config-only. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hosted Checkout | Stripe Elements / custom card form | PROHIBITED by platform rules and CONTEXT — Numera must never touch raw card data. Do NOT build. |
| Stripe-managed trial (`trial_period_days`) | App-managed `trial_ends_at` | CONTEXT D3 locks app-managed, no-card trial. Do NOT use Stripe trials; conversion is an explicit Checkout action. |
| Stripe Entitlements API (`entitlements.active_entitlement_summary.updated`) | Existing `tenants.plan` + `PlanCapabilityMap` | CONTEXT + repo lock the existing v1 gate. Do NOT adopt Stripe's Entitlements product; map subscription → plan → existing capabilities. |
| Price IDs synced from Stripe | Config-mapped price IDs | Config-mapped (recommended in CONTEXT) is simpler and testable; keep 4 products S/M/L/XL. |

**Installation (must run on the network, NOT in the Codex exec sandbox):**
```bash
# Package restore is blocked inside the Codex/exec sandbox (documented in Phase 12,
# .planning/phases/12-belege-ausgaben/12-03-SUMMARY.md: "Package add/restore succeeded
# outside the sandbox"). The orchestrator must add the package on the network first,
# then implementation continues with --no-restore.
"C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe" add src/Numera.Api/Numera.Api.csproj package Stripe.net --version 52.2.0
```
> ⚠️ **PLANNER: gate the first task on this.** Every subsequent `dotnet build` in this repo uses `--no-restore` (see all Phase-12/13 SUMMARYs). If the package is not present, builds fail. Add `Stripe.net` to `Numera.Api.csproj` on the network before any Stripe-touching task.

## Architecture Patterns

### Recommended Project Structure (mirror Phase 13)
```
src/modules/Numera.Modules.Billing/          # NEW module (mirror Numera.Modules.Banking)
├── IBillingProvider.cs                       # port (mirror IBankConnectionProvider.cs)
├── StubBillingProvider.cs                    # no-network default (mirror StubBankConnectionProvider)
└── (records: CheckoutSession, PortalSession, SubscriptionSnapshot, InvoiceRef)

src/Numera.Api/Services/Stripe/              # Stripe adapter (mirror Services/FinApi/)
├── StripeOptions.cs                          # SectionName="Stripe", SecretKey, WebhookSecret, PriceIds map (mirror FinApiOptions.cs)
├── StripeBillingProvider.cs                  # IBillingProvider over StripeClient (mirror FinApiBankConnectionProvider.cs)
└── BillingModuleServiceCollectionExtensions.cs  # AddBillingProvider(config) config-gate (mirror BankingModuleServiceCollectionExtensions.cs)

src/Numera.Api/Endpoints/BillingEndpoints.cs # authenticated Checkout/Portal + ANONYMOUS webhook
src/Numera.Api/Jobs/                          # OPTIONAL: no recurring job needed if compute-on-read (see D3)
tests/Numera.IntegrationTests/Fixtures/FakeBillingProvider.cs   # mirror FakeBankConnectionProvider.cs
tests/Numera.IntegrationTests/Fixtures/StripeWebhookFixture.cs  # signs payloads with a test secret (see Testing)
```
The tenant billing columns and the `processed_stripe_event` table go into `Numera.Platform.Db` (a new migration, exactly like the Banking migration) because `tenants` lives there and the schema is shared.

### Pattern 1: Config-gated provider swap (mirror `AddBankConnectionProvider`)
**What:** Register the real Stripe provider only when keys are configured; otherwise a safe no-network stub. CI never gets real keys → always gets the stub/fake.
**When to use:** Registration in `Program.cs`.
```csharp
// Source: mirror src/Numera.Api/Services/BankingModuleServiceCollectionExtensions.cs:19-47
public static IServiceCollection AddBillingProvider(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
    var stripe = configuration.GetSection(StripeOptions.SectionName);
    if (!string.IsNullOrWhiteSpace(stripe[nameof(StripeOptions.SecretKey)])
        && !string.IsNullOrWhiteSpace(stripe[nameof(StripeOptions.WebhookSecret)]))
    {
        services.AddScoped<IBillingProvider, StripeBillingProvider>();
        // StripeClient is thread-safe; register a singleton built from the secret key.
        services.AddSingleton<Stripe.IStripeClient>(sp =>
            new Stripe.StripeClient(sp.GetRequiredService<IOptions<StripeOptions>>().Value.SecretKey));
    }
    else
    {
        services.AddScoped<IBillingProvider, StubBillingProvider>();
    }
    return services;
}
```
Then in `Program.cs` (after `AddBankConnectionProvider`, ~line 151): `builder.Services.AddBillingProvider(builder.Configuration);`.
Add a DI registration test mirroring `tests/Numera.IntegrationTests/BankingProviderRegistrationTests.cs` (empty config → resolves `StubBillingProvider`).

### Pattern 2: Anonymous, signature-verified webhook endpoint (BILL-02, D1)
**What:** A public `POST /api/billing/webhook` that does NOT call `.RequireAuthorization()` (like `/health` at `Program.cs:221`). Verify the signature FIRST, then dedupe, then resolve tenant, then flip the plan.
**When to use:** The single source of truth for entitlement.

Why an anonymous endpoint is safe here (verified against the middleware chain):
- `TenantResolutionMiddleware` (`Program.cs:228`) passes unauthenticated requests through untouched — no tenant in scope (see its remarks: "health, registration, the OIDC challenge/callback … passed through untouched").
- `ReadOnlyWriteGuardMiddleware` (`Program.cs:229`) returns early for unauthenticated requests (`ReadOnlyWriteGuardMiddleware.cs:23-27`) — so it never blocks the webhook.
- The webhook must **read the raw request body** (not model-bound) because signature verification hashes the exact bytes. Buffer the body with `context.Request.EnableBuffering()` / read `await new StreamReader(Request.Body).ReadToEndAsync()`.

```csharp
// Source: Stripe.NET EventUtility (github.com/stripe/stripe-dotnet .../EventUtility.cs) + docs.stripe.com/webhooks#verify-events
var json = await new StreamReader(context.Request.Body).ReadToEndAsync(ct);
Event stripeEvent;
try
{
    stripeEvent = EventUtility.ConstructEvent(
        json,
        context.Request.Headers["Stripe-Signature"],
        options.WebhookSecret);          // throwOnApiVersionMismatch: false if pinning a different API version
}
catch (StripeException) { return Results.BadRequest(); }  // bad signature -> 400, Stripe will not retry a 4xx as success
```
Return `200` fast; Stripe retries non-2xx up to 3 days (docs). Keep handler idempotent (Pattern 3).

### Pattern 3: Idempotency via a dedupe table (mirror `ProcessedBelegeMail`)
**What:** Insert the Stripe `event.id` into a dedupe table before processing; a duplicate delivery hits the unique constraint and is skipped.
**When to use:** Every webhook.
```csharp
// Source: mirror src/modules/Numera.Modules.Crm/TenantBelegeMailbox.cs:48-67 (ProcessedBelegeMail)
[Table("processed_stripe_event")]
[Index(nameof(EventId), IsUnique = true)]
public sealed class ProcessedStripeEvent      // NOT ITenantEntity — see note below
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string EventId { get; init; }   // Stripe evt_... id
    public required string EventType { get; init; }
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
```
**Critical scoping decision (differs from Phase 13):** `ProcessedBelegeMail` is an `ITenantEntity` (unique on `(tenant_id, message_id)`) because it is written inside a tenant scope. The Stripe webhook is **tenant-agnostic** — it arrives with no authenticated tenant, and some events may not map to a tenant yet. Recommend `processed_stripe_event` be a **plain, non-RLS, non-`ITenantEntity` table** keyed globally on `event_id` (like the `tenants` table itself is special-cased in `NumeraDbContext.ConfigureTenant`). This lets you dedupe **before** tenant resolution and avoids an RLS chicken-and-egg. Grant `numera_app` INSERT/SELECT; do NOT add a `tenant_isolation` policy. (If the planner prefers a tenant-scoped table, dedupe must happen after tenant resolution and cannot cover unmappable events — call this out in the plan.)

### Pattern 4: Tenant resolution inside the webhook (no RLS lookup possible)
**What:** The webhook has no tenant in scope, and `tenants` is self-scoped by RLS — so you **cannot** query `tenants` by `stripe_customer_id` from an unauthenticated request. Carry the `tenant_id` in the Stripe objects at creation time and read it back on the event.
**When to use:** Always.
- On Checkout Session create, set `ClientReferenceId = tenantId.ToString()` AND `SubscriptionData.Metadata["tenant_id"]` AND `Customer`/`CustomerCreateOptions.Metadata["tenant_id"]`.
- On each webhook, extract `tenant_id` from the event object (`session.ClientReferenceId`, `subscription.Metadata["tenant_id"]`, `invoice.SubscriptionDetails?.Metadata` / `invoice.Lines...Metadata`, or the customer's metadata).
- Then `scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId)` BEFORE any `NumeraDbContext` work — exactly the `CheckBankConsentJob.RunAsync` pattern (`CheckBankConsentJob.cs:78-81`).
- **Fallback (if metadata is missing):** you can enumerate/lookup with an RLS-bypassing owner connection — the `Hangfire` connection string is schema-owning and not RLS-subject (see `Program.cs:41-46` and `CheckBankConsentJob.RunAllAsync` raw `SELECT ... FROM bank_connection`). Prefer metadata; keep the owner-connection lookup as a documented fallback only.

### Pattern 5: Subscription-status → plan mapping (BILL-02, D4)
**What:** Translate the Stripe subscription `status` + the subscribed price into `tenants.plan`.
```
trialing | active            -> plan from price ID (S/M/L/XL)   (grant paid capabilities)
past_due                     -> keep current paid plan (Smart Retries still running) — do NOT degrade yet
canceled | unpaid            -> Free (read-only)                (D4 degrade)
customer.subscription.deleted-> Free (read-only)                (D4 degrade)
invoice.paid (status active) -> re-activate paid plan from price (D4 re-activation)
```
Map the price ID → plan via `StripeOptions.PriceIds` config (e.g. `Stripe:PriceIds:S`, `:M`, `:L`, `:XL`, optional `:S_annual` …). This is the single mapping table; keep it in config so live/test price IDs differ by environment only.

### Anti-Patterns to Avoid
- **Trusting event body without signature verification.** Always `EventUtility.ConstructEvent`. A raw JSON POST must be rejected.
- **Model-binding the webhook body.** Breaks signature verification (needs exact bytes). Read the raw stream.
- **Looking up the tenant by `stripe_customer_id` via `NumeraDbContext` in the webhook.** RLS returns zero rows with no tenant set. Use metadata/`client_reference_id`.
- **Degrading on `past_due`.** That's premature — Smart Retries/dunning are still running. Degrade only on `canceled`/`unpaid`/`deleted` (D4 says "after retries are exhausted").
- **Booking Stripe's own invoices into the tenant SKR ledger.** Explicitly forbidden (D2). Numera's subscription invoices are the company's outbound billing, referenced only for display via the Portal.
- **Re-implementing VAT.** `automatic_tax.enabled=true` + tax-ID collection; store references only (D2).
- **Using Stripe's `trial_period_days`.** The trial is app-managed (D3).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Card capture / PCI | A card form | Hosted Checkout (`Stripe.Checkout.SessionService`) | PCI scope + platform prohibition; Numera never touches PANs. |
| Payment-method / invoice management UI | Custom billing dashboard | Hosted Customer Portal (`Stripe.BillingPortal.SessionService`) | BILL-03/06 are literally "the Portal". Configured in Dashboard. |
| VAT / reverse-charge math | German USt + §13b logic | Stripe Tax `automatic_tax` + tax-ID collection | BILL-07; Stripe validates EU VAT IDs and applies reverse charge (0%). |
| Webhook signature check | HMAC-SHA256 parsing | `EventUtility.ConstructEvent` | Handles `t=`/`v1=` scheme, tolerance window, replay protection. |
| Dunning/retry schedule | Custom retry loop | Stripe Smart Retries + built-in dunning | D4/BILL-05 delegate retries to Stripe; you only react to the terminal webhook. |
| Failed-payment emails | Email templates | Stripe's built-in dunning emails | CONTEXT defers dunning-email *content*; only one in-app degradation banner is in scope. |
| Proration on upgrade/downgrade | Proration math | Portal upgrade/downgrade | BILL-03 includes proration; Stripe computes it. |

**Key insight:** Every BILL requirement maps to a hosted Stripe surface or an existing Numera seam. The custom code is glue: the port, the webhook handler, the plan flip, the schema, and the frontend redirects.

## Common Pitfalls

### Pitfall 1: The `Capability` gate does not block core writes (D4 gap)
**What goes wrong:** You map `canceled`/lapsed-trial → a `Free`/empty capability set and assume writes are blocked. But `PlanCapabilityMap` (`src/platform/Numera.Platform.Entitlements/PlanCapabilityMap.cs`) only grants **premium** capabilities (S grants just `DataExport`). Baseline invoice CRUD is **not** capability-gated, so a degraded tenant could still create/edit invoices — violating "read-only".
**Why it happens:** The v1 entitlement system gates features, not base read/write.
**How to avoid:** Add a write-blocking guard for degraded billing state, mirroring the existing `ReadOnlyWriteGuardMiddleware` (`src/Numera.Api/Auth/ReadOnlyWriteGuardMiddleware.cs`) and `ReadOnlyAccessPolicy` (`src/Numera.Api/Auth/ReadOnlyAccessPolicy.cs`) that already implement exactly "authenticated + this-condition ⇒ block POST/PUT/PATCH/DELETE, allow GET". Drive it off the tenant's effective billing state instead of the TaxAdvisor role. See Open Questions #1.
**Warning signs:** Integration test where a `canceled` tenant still 200s on `POST /api/documents`.

### Pitfall 2: Webhook ordering / out-of-order delivery
**What goes wrong:** `customer.subscription.updated` and `invoice.paid` can arrive out of order; a stale event overwrites a newer state.
**Why it happens:** Stripe does not guarantee ordering.
**How to avoid:** Prefer deriving state from the event's current object fields (status, current_period_end) and, on ambiguity, re-fetch the subscription via `SubscriptionService.GetAsync` (source of truth) rather than trusting the delivered snapshot. Store `current_period_end`; ignore events older than the stored period end when reasonable.
**Warning signs:** Flapping plan between paid and Free.

### Pitfall 3: `EventUtility.ConstructEvent` API-version mismatch exception
**What goes wrong:** If your account's API version differs from the SDK's pinned version, `ConstructEvent` can throw a version-mismatch `StripeException`, rejecting valid events.
**Why it happens:** SDK pins an `ApiVersion`; the account/event may differ.
**How to avoid:** Pass `throwOnApiVersionMismatch: false` to `ConstructEvent` (documented overload). Confirmed on the SDK `EventUtility`.
**Warning signs:** All live webhooks 400 while signatures are correct.

### Pitfall 4: Signed test fixtures with a stale timestamp
**What goes wrong:** `ConstructEvent` enforces a default 300s tolerance on the `t=` timestamp; a fixture with a hard-coded old timestamp fails verification in CI.
**Why it happens:** Replay protection.
**How to avoid:** In the test fixture, sign with `DateTimeOffset.UtcNow` (recompute the header per test run), or use the `ConstructEvent(json, header, secret, tolerance, utcNow)` overload to control `utcNow`. See Testing.
**Warning signs:** Webhook tests pass on the day written, fail later.

### Pitfall 5: Stripe Tax not enabled or address missing
**What goes wrong:** `automatic_tax` requires a determinable customer location; missing address → `invoice.finalization_failed` with `automatic_tax.status=requires_location_inputs`, subscription can't bill.
**Why it happens:** Stripe Tax must be enabled in Dashboard and the customer must have an address / tax ID.
**How to avoid:** On Checkout enable `AutomaticTax.Enabled=true`, `TaxIdCollection.Enabled=true`, and `CustomerUpdate` with `Address=Auto`/`Name=Auto` so the collected billing address is saved to the customer. Handle `invoice.finalization_failed` by surfacing a "complete your billing address" prompt (Portal deep-link).
**Warning signs:** New subscriptions stuck; no `invoice.paid`.

### Pitfall 6: Registration must set `trial_ends_at` (D3)
**What goes wrong:** New tenants get full access forever because no trial deadline is set.
**Why it happens:** `RegistrationService.RegisterAsync` (`src/Numera.Api/Services/RegistrationService.cs:92-97`) currently creates the `Tenant` with `Plan = TenantPlan.S` and no trial field.
**How to avoid:** Set `TrialEndsAt = DateTimeOffset.UtcNow.AddDays(14)` at tenant creation, and decide the initial `Plan` (recommend: keep `Plan` at the paid tier the trial grants, e.g. a full-access trial, with the effective gate computed from `TrialEndsAt` + subscription state — see Open Questions #2).
**Warning signs:** No degradation ever occurs.

## Code Examples

### Create a subscription Checkout Session (BILL-01, BILL-07)
```csharp
// Source: docs.stripe.com/checkout (subscription mode) + Stripe.net Stripe.Checkout.SessionService
var options = new Stripe.Checkout.SessionCreateOptions
{
    Mode = "subscription",
    ClientReferenceId = tenantId.ToString(),                 // Pattern 4 linkage
    Customer = existingStripeCustomerId,                     // or CustomerEmail on first purchase
    LineItems = [ new() { Price = options.PriceIds[targetPlan], Quantity = 1 } ],
    SubscriptionData = new() { Metadata = new() { ["tenant_id"] = tenantId.ToString() } },
    AutomaticTax = new() { Enabled = true },                 // D2 / BILL-07
    TaxIdCollection = new() { Enabled = true },              // EU-B2B reverse charge
    CustomerUpdate = new() { Address = "auto", Name = "auto" },
    SuccessUrl = $"{appBaseUrl}/billing/success?session_id={{CHECKOUT_SESSION_ID}}",
    CancelUrl  = $"{appBaseUrl}/billing/cancel",
};
var session = await new Stripe.Checkout.SessionService(stripeClient).CreateAsync(options, cancellationToken: ct);
return session.Url;   // 303-redirect the browser here
```

### Create a Customer Portal session (BILL-03, BILL-06)
```csharp
// Source: docs.stripe.com/customer-management/integrate-customer-portal (billing_portal/sessions)
var portal = await new Stripe.BillingPortal.SessionService(stripeClient).CreateAsync(
    new Stripe.BillingPortal.SessionCreateOptions
    {
        Customer = tenant.StripeCustomerId,                  // read via RLS (authenticated request)
        ReturnUrl = $"{appBaseUrl}/billing",
    }, cancellationToken: ct);
return portal.Url;   // redirect; portal features (payment method, invoices, upgrade/downgrade+proration) are Dashboard-configured
```
Portal capabilities (payment method update, invoice history, upgrade/downgrade with proration, cancel) are **configured in the Dashboard** (test + live are separate config sets), or via the `billing_portal/configurations` API. No per-feature code in Numera.

### Webhook handler skeleton (BILL-02)
```csharp
// Source: composed from CheckBankConsentJob.cs (SetTenant pattern) + Stripe webhook docs
app.MapPost("/api/billing/webhook", async (HttpContext ctx, IBillingWebhookHandler handler, CancellationToken ct) =>
{
    var json = await new StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
    var sig = ctx.Request.Headers["Stripe-Signature"].ToString();
    return await handler.HandleAsync(json, sig, ct);   // verify -> dedupe -> resolve tenant -> SetTenant -> flip plan
});   // NOTE: no .RequireAuthorization()
```
Inside the handler: `EventUtility.ConstructEvent` → insert `ProcessedStripeEvent` (skip on duplicate) → resolve `tenant_id` → `SetTenant` → load `Tenant` (RLS) → set `Plan`/`SubscriptionStatus`/`CurrentPeriodEnd`/`StripeSubscriptionId` → `SaveChangesAsync`.

### Migration RLS conventions (mirror Banking)
```csharp
// Source: src/platform/Numera.Platform.Db/Migrations/20260805130444_Banking.cs:128-153
// For any NEW tenant-scoped table (none strictly required here — see data model):
migrationBuilder.Sql("ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE <t> FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql("""
    CREATE POLICY tenant_isolation ON <t>
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
    """);
// Down: DROP POLICY IF EXISTS; NO FORCE; DISABLE; then DropTable. Snake_case table names,
// (tenant_id, id) leading index. New columns on `tenants` need NO RLS (tenants is self-scoped on id).
```
`processed_stripe_event` (tenant-agnostic) gets **no** RLS policy — just a unique index on `event_id`. New `tenants` columns inherit the existing self-scoped policy.

## Data Model (recommended)

New columns on `tenants` (self-scoped RLS on `id`, NOT `ITenantEntity` — configured in `NumeraDbContext.ConfigureTenant`, `NumeraDbContext.cs:293-304`):
| Column | Type | Purpose |
|--------|------|---------|
| `stripe_customer_id` | text null, unique | Portal session + linkage |
| `stripe_subscription_id` | text null | current subscription |
| `subscription_status` | int/text null | mapped Stripe status (trialing/active/past_due/canceled/unpaid) |
| `current_period_end` | timestamptz null | access-until timestamp (renewal margin) |
| `trial_ends_at` | timestamptz null | app-managed 14-day trial deadline (D3), set at registration |

New table (tenant-agnostic, no RLS): `processed_stripe_event(id, event_id UNIQUE, event_type, processed_at)` — idempotency.

Add `Free` to the `TenantPlan` enum (`src/platform/Numera.Platform.Db/Entities/Tenant.cs:8-21`, and mirror in `web/src/lib/entitlements.ts` if surfaced) for the D4 read-only tier. `PlanCapabilityMap.For` already denies-by-default for plans not in the `Matrix` (`PlanCapabilityMap.cs:83-84`), so `Free` automatically yields an empty capability set — but remember Pitfall 1 (empty caps ≠ read-only for core CRUD).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `StripeConfiguration.ApiKey` global static | `new StripeClient(key)` injected into services | Stable for years | DI-friendly, testable, matches the config-gate pattern. Recommend `StripeClient`. |
| Charges API | PaymentIntents + Checkout + Billing | long-settled | Use Checkout/Billing only. |
| Manual VAT rates | Stripe Tax `automatic_tax` | GA | Zero VAT math (D2). |
| `subscription` field on Invoice | still present; also `subscription_details.metadata` | current | When reading `tenant_id` off `invoice.*`, check both the subscription metadata and line metadata. |

**Deprecated/outdated:** Do not use Stripe Elements/custom card capture (prohibited). Do not use Stripe's Entitlements product (repo uses its own gate). Accounts v2 (seen in docs) is a Connect/preview concept — irrelevant; use `Customer` (v1) objects.

## Open Questions

1. **How to enforce "read-only/Free" for core CRUD (D4)?** *(HIGH importance)*
   - What we know: The `Capability` gate only guards premium features; `ReadOnlyWriteGuardMiddleware` + `ReadOnlyAccessPolicy` already implement role-based write-blocking (allow GET, deny POST/PUT/PATCH/DELETE, with a read allow-list).
   - What's unclear: Whether to (a) generalize `ReadOnlyWriteGuardMiddleware` to also block writes when the tenant's effective billing state is degraded, (b) add a parallel `BillingDegradationWriteGuardMiddleware`, or (c) add explicit "requires active subscription" capability checks on write endpoints.
   - Recommendation: **(b)** a dedicated billing write-guard middleware mirroring the Phase-8 one, ordered right after `TenantResolutionMiddleware`, reading an effective-billing-state service (subscription status + `trial_ends_at`). It reuses a proven, tested pattern and keeps the read allow-list explicit. Pair it with the `Free` plan so premium caps also drop. Planner should decide the exact allow-listed read prefixes.

2. **Initial `Plan` for a trialing tenant + how "effective plan" is computed (D3).** *(HIGH)*
   - What we know: Trial grants full access with no card; `EntitlementService` reads `tenants.plan` directly and memoizes per scope (`EntitlementService.cs:64-75`).
   - What's unclear: Whether to (a) set `tenants.plan` to a paid tier during trial and compute degradation from `trial_ends_at`/status at read time, or (b) keep `plan` = the granted tier and have a separate "is the gate active" computation.
   - Recommendation: **compute-on-read** — the effective gate = paid-plan caps if (`subscription_status` active/trialing) OR (`trial_ends_at > now`), else `Free`. Implement by having the billing write-guard + a thin "effective plan" resolver consult `trial_ends_at`/`subscription_status`; avoid a recurring job entirely (CONTEXT lists this as discretion; compute-on-read is race-free and needs no worker). A recurring trial-sweep job is only needed if you later want proactive emails (out of scope).

3. **Exact Stripe.NET service class/namespace names on 52.2.0.** *(MEDIUM)*
   - What we know: `Stripe.Checkout.SessionService`, `Stripe.BillingPortal.SessionService`, `Stripe.CustomerService`, `Stripe.SubscriptionService`, `Stripe.InvoiceService`, `Stripe.EventUtility.ConstructEvent`, and `Stripe.Events.*` event-type constants are stable across many major versions.
   - What's unclear: Minor option-property renames between major versions (e.g., proration/`SubscriptionDetails` shapes) are possible.
   - Recommendation: The planner should have the implementer confirm exact option property names against the installed 52.2.0 assembly (IntelliSense) rather than trusting doc snippets verbatim. Low risk; the top-level service names are correct.

4. **Where does the `IBillingProvider` port live?** *(LOW — discretion)*
   - Recommendation: New `src/modules/Numera.Modules.Billing` for the port + stub (mirrors `Numera.Modules.Banking`), Stripe adapter in `src/Numera.Api/Services/Stripe`. Add the module as a `ProjectReference` in `Numera.Api.csproj` and (for entity discovery) ensure any billing entities are deployed next to `Platform.Db` — though billing columns live on `tenants` in `Platform.Db` directly, so a module reference may be unnecessary if the port has no entities.

## Sources

### Primary (HIGH confidence)
- Repo files (read directly): `src/platform/Numera.Platform.Entitlements/{Capability,Plan,PlanCapabilityMap,IEntitlementService,EntitlementService}.cs`; `src/platform/Numera.Platform.Db/Entities/Tenant.cs`; `src/platform/Numera.Platform.Db/NumeraDbContext.cs`; `src/platform/Numera.Platform.Db/Migrations/20260805130444_Banking.cs`; `src/modules/Numera.Modules.Banking/IBankConnectionProvider.cs`; `src/Numera.Api/Services/BankingModuleServiceCollectionExtensions.cs`; `src/Numera.Api/Services/FinApi/{FinApiBankConnectionProvider,FinApiOptions}.cs`; `src/Numera.Api/Jobs/CheckBankConsentJob.cs`; `src/Numera.Api/Auth/{ReadOnlyWriteGuardMiddleware,ReadOnlyAccessPolicy,TenantResolutionMiddleware}.cs`; `src/Numera.Api/Program.cs`; `src/Numera.Api/Endpoints/{BankAccountEndpoints,MeEndpoints}.cs`; `src/Numera.Api/Services/RegistrationService.cs`; `src/modules/Numera.Modules.Crm/TenantBelegeMailbox.cs` (`ProcessedBelegeMail`); `tests/Numera.IntegrationTests/Fixtures/FakeBankConnectionProvider.cs`; `tests/Numera.IntegrationTests/BankingProviderRegistrationTests.cs`; `web/src/lib/entitlements.ts`; `web/src/features/shared/UpgradeHint.tsx`; `.planning/phases/12-belege-ausgaben/12-03-SUMMARY.md` (NuGet sandbox note).
- Stripe.net NuGet: latest stable **52.2.0** (2026-07-29), targets net6/8/9 + netstandard2.0 — https://www.nuget.org/packages/Stripe.net
- Stripe docs — subscription webhooks + status table — https://docs.stripe.com/billing/subscriptions/webhooks
- Stripe docs — Customer Portal integration (billing_portal/sessions, Dashboard config) — https://docs.stripe.com/customer-management/integrate-customer-portal
- Stripe.net `EventUtility.ConstructEvent` (source) — https://github.com/stripe/stripe-dotnet/blob/master/src/Stripe.net/Services/Events/EventUtility.cs

### Secondary (MEDIUM confidence)
- Stripe Tax reverse-charge / zero-tax + tax-ID collection — https://docs.stripe.com/tax/zero-tax ; https://docs.stripe.com/tax/checkout/tax-ids
- Stripe.net webhook receiver tutorials (pattern confirmation) — https://wellsb.com/csharp/aspnet/stripe-net-create-stripe-webhooks-receiver

### Tertiary (LOW confidence — validate at implementation)
- Exact 52.2.0 option-property names (verify via installed assembly IntelliSense).

## Testing Without Stripe (D1 / CI)

Mirror Phase 13's fake + fixture approach:
- **`FakeBillingProvider : IBillingProvider`** (mirror `tests/.../Fixtures/FakeBankConnectionProvider.cs`): scriptable return values for `CreateCheckoutSession`/`CreatePortalSession`/`GetSubscription`; no network. Register it in the test host in place of `StripeBillingProvider` (the config-gate resolves the stub when keys are absent — assert this in a registration test).
- **Signed webhook fixtures without Stripe:** the webhook secret is symmetric, so tests can construct a valid `Stripe-Signature` header locally:
  ```csharp
  // header format: t=<unixSeconds>,v1=<hex HMACSHA256( "<t>.<payload>", webhookSecret )>
  var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();               // fresh timestamp (Pitfall 4)
  var signedPayload = $"{t}.{json}";
  using var h = new HMACSHA256(Encoding.UTF8.GetBytes(testWebhookSecret));
  var v1 = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
  var header = $"t={t},v1={v1}";
  // then POST json + header to /api/billing/webhook; assert tenants.plan transition.
  ```
  This exercises the **real** `EventUtility.ConstructEvent` path (real signature verification) while never contacting Stripe — satisfying D1. Store canonical event JSON bodies (checkout.session.completed, customer.subscription.updated[past_due], customer.subscription.deleted, invoice.paid) as fixtures.
- **Gate-transition assertions:** drive the webhook with each fixture and assert `tenants.plan` moves paid→Free→paid and that a degraded tenant is write-blocked (Pitfall 1). Use the existing Testcontainers `postgres:18` least-privilege `numera_app` harness so RLS + the tenant-agnostic dedupe table are exercised realistically.

## Security / Platform Constraints (confirmed against repo)

- **No raw card data:** hosted Checkout + Portal only. No card fields anywhere in `web/`.
- **Signature verification mandatory:** `EventUtility.ConstructEvent`; reject unsigned/invalid with 400.
- **Secrets via config/user-secrets:** `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Stripe:PriceIds:*`. `Numera.Api` already has `UserSecretsId` (`Numera.Api.csproj:6`). CI/tests leave these unset → stub provider.
- **Webhook is public & auth-exempt, and this is safe:** `TenantResolutionMiddleware` and `ReadOnlyWriteGuardMiddleware` both no-op for unauthenticated requests (verified in source). The TaxAdvisor write-guard therefore does not interfere. The webhook must still be idempotent and signature-checked.
- **CSP/routing:** Redirects to `checkout.stripe.com` / `billing.stripe.com` are top-level browser navigations (303), not embedded frames, so no CSP `frame-src` change is needed for the hosted flows. (No CSP config found in-repo to amend; confirm at implementation if a CSP is later added.)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — official SDK, current stable version verified on NuGet; every requirement maps to a documented hosted Stripe surface.
- Architecture: HIGH — every seam (port, config-gate, fake, RLS migration, `SetTenant`-first job, anonymous endpoint, idempotency) is grounded in a real Phase-8/12/13 file read this session.
- D4 read-only enforcement: MEDIUM — the mechanism is a design choice; the gap (capabilities don't block core writes) is real and flagged with a concrete, precedented solution.
- Pitfalls: HIGH for Stripe-specific ones (docs-verified); MEDIUM for the exact SDK option property names on 52.2.0.

**Research date:** 2026-08-10
**Valid until:** ~2026-09-10 (Stripe API + Stripe.net move fast; re-verify the pinned `Stripe.net` version and any option renames if planning slips past ~30 days).

## RESEARCH COMPLETE
