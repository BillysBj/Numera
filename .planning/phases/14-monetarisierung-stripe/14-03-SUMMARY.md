---
phase: 14-monetarisierung-stripe
plan: 03
subsystem: stripe-adapter-api
tags: [dotnet, stripe, checkout, customer-portal, stripe-tax, configuration, minimal-api]

requires:
  - phase: 14-01
    provides: Tenant Stripe linkage, subscription status, period, trial, and paid-plan columns
  - phase: 14-02
    provides: Provider-neutral billing port, contracts, and no-network stub
provides:
  - "Config-gated Stripe.net adapter over an injected IStripeClient"
  - "Single bidirectional Stripe price-to-tenant-plan mapping"
  - "Authenticated hosted Checkout and Customer Portal session endpoints"
  - "Authenticated billing status including trial and degradation state"
affects: [stripe-webhooks, billing-gate, checkout-ui, customer-portal, subscription-status]

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-10
---

# Phase 14-03: Stripe adapter and authenticated billing API

Stripe TEST-mode billing now sits behind the provider-neutral port and activates only when both the
API key and webhook secret are configured. Authenticated tenants can request hosted Checkout and
Customer Portal URLs and read the server-authoritative billing/trial status without exposing Stripe
types outside the API adapter.

## Implemented

- Added `StripeOptions` bound from the `Stripe` section with SecretKey, WebhookSecret, and the
  `Stripe:PriceIds:{S,M,L,XL}` dictionary.
- Added `PriceIdFor(TenantPlan)` and `PlanForPriceId(string)` over that single dictionary; Free and
  missing/blank mappings return null.
- Added `StripeBillingProvider` over injected `Stripe.IStripeClient`; no global
  `StripeConfiguration.ApiKey` state is used.
- Checkout creates a subscription-mode hosted session with client-reference and subscription
  metadata tenant linkage, one configured price, automatic Stripe Tax, tax-ID collection,
  customer address/name update, existing-customer reuse, and email fallback for first purchase.
- Customer Portal creates a hosted session for the tenant's existing Stripe customer.
- Subscription lookup maps id/status/customer/metadata plus both price id and current-period end
  from the first subscription item, matching the Stripe.net 52.x relocation.
- Added `AddBillingProvider`: both SecretKey and WebhookSecret select singleton `IStripeClient` plus
  scoped `StripeBillingProvider`; any incomplete configuration selects scoped
  `StubBillingProvider`.
- Added three individually authenticated endpoints:
  - `POST /api/billing/checkout` accepts only S/M/L/XL and returns the hosted URL.
  - `POST /api/billing/portal` returns 409 when no Stripe customer exists.
  - `GET /api/billing/status` returns plan, trial/subscription timestamps and state, subscription
    presence, active-trial state, and degradation using the specified trialing/active/past_due rule.
- Registered the provider immediately after Banking's provider gate and mapped billing endpoints
  immediately after `MapMeEndpoints`; no webhook endpoint was added.
- Added empty-config and complete-config DI tests, including singleton `IStripeClient` verification.

## Files modified

- `src/Numera.Api/Program.cs`

`src/Numera.Api/Numera.Api.csproj` was not modified because the reviewed gating step was already
complete and its Stripe.net 52.2.0 reference was present/restored.

## Files created

- `src/Numera.Api/Services/Stripe/StripeOptions.cs`
- `src/Numera.Api/Services/Stripe/StripeBillingProvider.cs`
- `src/Numera.Api/Services/BillingModuleServiceCollectionExtensions.cs`
- `src/Numera.Api/Endpoints/BillingEndpoints.cs`
- `tests/Numera.IntegrationTests/BillingProviderRegistrationTests.cs`
- `.planning/phases/14-monetarisierung-stripe/14-03-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe`.
- `Numera.Api.csproj` Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- `Numera.IntegrationTests.csproj` with `--filter BillingProviderRegistration`: 2 passed, 0 failed,
  0 skipped.
- `git diff --check`: passed.
- Static checks confirm Stripe.net 52.2.0, both-secret gating, injected client use, three authorized
  endpoints, tenant linkage, automatic tax/tax-ID flags, item-level price/period access, no
  subscription-root period access, and no webhook route.

## Stripe.net 52.2.0 API confirmation

- The installed package XML exposes all planned `SessionCreateOptions` members unchanged:
  `Mode`, `ClientReferenceId`, `Customer`, `CustomerEmail`, `LineItems`, `SubscriptionData`,
  `AutomaticTax`, `TaxIdCollection`, `CustomerUpdate`, `SuccessUrl`, and `CancelUrl`.
- Both item-level members compiled exactly as reviewed: `firstItem.Price.Id` and
  `firstItem.CurrentPeriodEnd`.
- No Stripe.net property name required correction during implementation.

## Deviations and uncertainties

- The repository contains no application/frontend base-URL configuration key to reuse; its only
  `BaseUrl` keys address external FinApi and e-invoice services. To avoid inventing a new config key,
  Checkout and Portal return URLs use the authenticated request origin (`Scheme`, `Host`, and
  `PathBase`) plus the exact planned `/billing/...` paths. Deployments behind a reverse proxy must
  continue supplying the correct forwarded request origin.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was untouched.

