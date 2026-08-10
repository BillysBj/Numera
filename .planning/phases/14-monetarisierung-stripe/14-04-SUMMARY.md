---
phase: 14-monetarisierung-stripe
plan: 04
subsystem: stripe-webhook-entitlements
tags: [dotnet, stripe, webhook, hmac, postgres, rls, idempotency, testcontainers]

requires:
  - phase: 14-01
    provides: Tenant billing columns and global non-RLS processed_stripe_event table
  - phase: 14-02
    provides: Billing provider port, fake provider, and signed webhook fixture helpers
  - phase: 14-03
    provides: Stripe price-plan map, Stripe adapter, and billing endpoint module
provides:
  - "Anonymous raw-body Stripe webhook with real signature verification"
  - "Global event-id reservation before tenant resolution"
  - "Fresh SetTenant-first RLS scope for authoritative plan transitions"
  - "Paid-to-Free-to-paid subscription transition coverage"
affects: [billing-degradation, effective-plan, billing-ui, stripe-dunning]

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-10
---

# Phase 14-04: Signature-verified authoritative Stripe webhook

Stripe webhooks now verify the exact raw payload, reserve each event globally, resolve tenant linkage,
and mutate billing state only through a fresh RLS-bound tenant scope. Checkout activates the purchased
plan, Smart Retry `past_due` preserves it, terminal subscription state degrades to Free, and a later
paid invoice restores the configured paid tier.

## Implemented

- Added pure `SubscriptionPlanMapper` decisions:
  - `trialing`/`active` resolve through `StripeOptions.PlanForPriceId`.
  - `past_due` returns the current plan unchanged.
  - `canceled`/`unpaid` and subscription deletion return Free.
  - `invoice.paid` resolves the configured paid plan from its price.
- Added `IBillingWebhookHandler` and `BillingWebhookHandler` with the exact ordered pipeline:
  1. `EventUtility.ConstructEvent` verifies the raw body with
     `throwOnApiVersionMismatch: false`; Stripe exceptions return 400.
  2. A dedicated no-tenant DI scope resolves its own `NumeraDbContext`, inserts
     `ProcessedStripeEvent`, and returns 200 only for PostgreSQL unique-violation duplicates.
  3. Tenant linkage is resolved from Checkout client reference/metadata, subscription metadata, or
     invoice subscription details/metadata plus provider re-fetch.
  4. The dedupe scope is disposed; a fresh scope sets its fresh `ICurrentTenant` before resolving its
     own `NumeraDbContext`.
  5. The RLS-visible tenant row is loaded, stale subscription updates are ignored when their item
     period predates the stored period, billing fields and plan are updated, and the transition is
     logged.
- Checkout re-fetches the subscription for authoritative status, price, current item period, and
  fallback tenant metadata while retaining Checkout customer/subscription ids.
- Subscription events read both price and `CurrentPeriodEnd` from `Items.Data[0]`; no root-level
  subscription period is used.
- Invoice events resolve v52 subscription details from `Invoice.Parent.SubscriptionDetails` and
  re-fetch through `IBillingProvider.GetSubscriptionAsync` for current status/price/period.
- Added the anonymous `POST /api/billing/webhook` endpoint. It reads `Request.Body` with
  `StreamReader`, reads `Stripe-Signature`, delegates to the handler, and has no authorization
  requirement.
- Registered the scoped handler and mapped the webhook alongside `/health`, before authentication
  and all authenticated endpoint maps.
- Corrected subscription fixtures so `current_period_end` is inside the first subscription item and
  added a parameterized canonical `invoice.paid` fixture.
- Added least-privilege PostgreSQL tests for signature rejection, Checkout activation, duplicate
  reservation, past-due preservation, deletion degradation, and invoice-paid reactivation.
- Added database-free signed-fixture/mapper contract checks so real Stripe.net parsing and the
  item-level period path remain verifiable when Docker is unavailable.

## Persistence-scope seam

- **Dedupe context:** scope 1 has a fresh unset `TenantContext` and its own scoped
  `NumeraDbContext`. It opens the configured least-privilege Default connection without setting
  `app.current_tenant` and touches only the non-RLS `processed_stripe_event` table.
- **Tenant mutation context:** after scope 1 is disposed, scope 2 receives another fresh
  `TenantContext`. `SetTenant(tenantId)` is called before resolving its separate scoped
  `NumeraDbContext`. When that context opens its separate logical connection, the production
  interceptor sets `app.current_tenant`, and PostgreSQL RLS admits only that tenant row.
- No `NumeraDbContext`, tracked entity, or open logical connection is reused across the seam.

## Files modified

- `src/Numera.Api/Endpoints/BillingEndpoints.cs`
- `src/Numera.Api/Program.cs`
- `tests/Numera.IntegrationTests/Fixtures/StripeWebhookFixture.cs`

## Files created

- `src/Numera.Api/Services/Stripe/SubscriptionPlanMapper.cs`
- `src/Numera.Api/Services/Stripe/BillingWebhookHandler.cs`
- `tests/Numera.IntegrationTests/BillingWebhookTests.cs`
- `.planning/phases/14-monetarisierung-stripe/14-04-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe`.
- `Numera.Api.csproj` Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- `BillingWebhookContract` filter: 4 passed, 0 failed. This covers missing/bad signature 400s
  before database access, real signed subscription and invoice deserialization, item-level period,
  and mapper retry/deletion/reactivation decisions.
- Requested `BillingWebhook` filter: the solution and all seven selected tests compiled; four
  database-free tests passed. The three PostgreSQL cases could not enter their test bodies because
  Docker/Testcontainers could not connect to `npipe://./pipe/docker_engine`.
- Static ordering checks confirm verify before dedupe, dedupe before resolution, a fresh second
  scope, SetTenant before tenant DbContext resolution, anonymous mapping before authentication, no
  root-level subscription period read, and no authorization on the webhook.
- `git diff --check`: passed.

## Deviations and uncertainties

- Runtime PostgreSQL assertions remain to be rerun with Docker available; the exact requested filter
  command is unchanged. The failure is exclusively Testcontainers' Docker-unavailable guard, not a
  compilation or assertion failure.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was untouched.

