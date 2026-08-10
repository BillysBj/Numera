---
phase: 14-monetarisierung-stripe
plan: 02
subsystem: billing-provider-port
tags: [dotnet, stripe, billing, ports, fake, hmac, integration-testing]

requires:
  - phase: 13-banking-zahlungsabgleich
    provides: Provider-port, no-network stub, and scriptable integration-test fake conventions
provides:
  - "Provider-neutral checkout, portal, and subscription lookup port"
  - "No-network billing stub for unconfigured environments"
  - "Scriptable billing fake for integration tests"
  - "Fresh locally signed canonical Stripe webhook fixtures"
affects: [stripe-adapter, checkout, customer-portal, stripe-webhooks, billing-integration-tests]

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-10
---

# Phase 14-02: Provider-neutral billing port and test fixtures

The new Billing module exposes Checkout, Customer Portal, and subscription lookup through plain C#
contracts with a safe no-network stub, while integration tests can script provider responses and
construct freshly signed canonical Stripe webhook payloads without contacting Stripe.

## Implemented

- Added `Numera.Modules.Billing` as a pure net10.0 module using repository-wide nullable, language,
  analyzer, and documentation settings. Its project has no package or Stripe.net reference.
- Added `IBillingProvider` with the exact Checkout, Portal, and subscription snapshot methods.
- Added provider-neutral request/session/snapshot records containing only strings, GUIDs, and
  `DateTimeOffset`.
- Added `StubBillingProvider`; every method throws the required no-keys `NotSupportedException`.
- Added Billing project references to the API and integration-test projects. The existing API
  Stripe.net 52.2.0 package reference was preserved without modification.
- Added `FakeBillingProvider` with scriptable Checkout, Portal, and subscription results plus captured
  request values for assertions.
- Added `StripeWebhookFixture.CreateSignatureHeader`; it computes a fresh Unix timestamp on every
  invocation and returns lowercase HMAC-SHA256 in exact `t=<unix>,v1=<hex>` form.
- Added parameterized canonical event builders for `checkout.session.completed`, subscription
  `past_due`, `customer.subscription.deleted`, and subscription `active`, including tenant/customer/
  subscription/price/event linkage fields.

## Files modified

- `src/Numera.Api/Numera.Api.csproj` (Billing project reference only)
- `tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj`

## Files created

- `src/modules/Numera.Modules.Billing/Numera.Modules.Billing.csproj`
- `src/modules/Numera.Modules.Billing/IBillingProvider.cs`
- `src/modules/Numera.Modules.Billing/BillingContracts.cs`
- `src/modules/Numera.Modules.Billing/StubBillingProvider.cs`
- `tests/Numera.IntegrationTests/Fixtures/FakeBillingProvider.cs`
- `tests/Numera.IntegrationTests/Fixtures/StripeWebhookFixture.cs`
- `.planning/phases/14-monetarisierung-stripe/14-02-SUMMARY.md`

## Verification

- Static inspection confirms the Billing module has no `PackageReference`, Stripe namespace, or
  Stripe type dependency.
- Static inspection confirms signature timestamps are calculated inside each signing call and the
  signature uses lowercase HMAC-SHA256 over `<timestamp>.<raw-json>`.
- Billing, API, and integration-test Release `--no-restore` builds are blocked by `NETSDK1004` because
  this brand-new project has no `obj/project.assets.json` in the offline sandbox.
- Platform.Db and Platform.Entitlements builds passed independently, confirming the shared model and
  Free-tier changes compile.

## Deviations and uncertainties

- None in implementation scope. The expected first restore/build of the new project is the only
  outstanding environment step.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was untouched.

