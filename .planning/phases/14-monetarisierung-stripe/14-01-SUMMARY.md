---
phase: 14-monetarisierung-stripe
plan: 01
subsystem: billing-data-foundation
tags: [dotnet, ef-core, postgres, stripe, billing, trial, entitlements]

requires:
  - phase: 01-plattform-kern
    provides: Tenant model, PostgreSQL RLS conventions, and server-authoritative plan entitlements
provides:
  - "Nullable Stripe customer/subscription state and app-managed trial deadline on tenants"
  - "Tenant-agnostic global Stripe webhook deduplication table"
  - "Free deny-by-default entitlement tier"
  - "Fourteen-day no-card trial initialization at registration"
affects: [stripe-webhooks, checkout, customer-portal, billing-gate, billing-ui]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-10
---

# Phase 14-01: Billing data-model foundation

Tenants now carry nullable Stripe linkage and subscription state, new registrations persist a
14-day app-managed trial deadline, and a global non-RLS event table provides Stripe webhook
idempotency. The `Free = 5` tier intentionally remains outside the capability matrix and therefore
receives the existing deny-by-default empty capability set.

## Implemented

- Added nullable `StripeCustomerId`, `StripeSubscriptionId`, `SubscriptionStatus`,
  `CurrentPeriodEnd`, and `TrialEndsAt` fields to `Tenant` without making it an `ITenantEntity`.
- Added `TenantPlan.Free = 5` without renumbering S/M/L/XL and documented its intentional absence
  from `PlanCapabilityMap.Matrix`.
- Added tenant-agnostic `ProcessedStripeEvent` with UUIDv7 identity and globally unique `EventId`.
- Explicitly mapped `processed_stripe_event` outside tenant discovery/query filters and mapped the
  partial unique `tenants.stripe_customer_id` index.
- Added the `Billing` migration and matching designer/snapshot. The migration adds all five nullable
  tenant columns, creates the global dedupe table and unique event index, grants `SELECT, INSERT` to
  `numera_app`, and deliberately contains no RLS enable/force/policy SQL for the dedupe table.
- Kept registration on plan S, persisted `UtcNow.AddDays(14)`, and recorded the same `trialEndsAt`
  value in the `tenant.created` audit payload without making any Stripe call.
- Added the minimal web `PlanName = 'Free' | 'S' | 'M' | 'L' | 'XL'` type without changing the
  capability map.
- Checked all source references to `TenantPlan`; no exhaustive switch requires a new `Free` arm.

## Files modified

- `src/platform/Numera.Platform.Db/Entities/Tenant.cs`
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs`
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs`
- `src/platform/Numera.Platform.Entitlements/PlanCapabilityMap.cs`
- `src/Numera.Api/Services/RegistrationService.cs`
- `web/src/lib/entitlements.ts`

## Files created

- `src/platform/Numera.Platform.Db/Entities/ProcessedStripeEvent.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260810141528_Billing.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260810141528_Billing.Designer.cs`
- `.planning/phases/14-monetarisierung-stripe/14-01-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe`.
- Platform.Db Release build with `--no-restore`: passed with 0 warnings and 0 errors after the
  hand-authored migration/designer/snapshot were complete.
- Platform.Entitlements Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Static migration inspection confirms five nullable tenant columns, the partial unique customer
  index, global unique event index, exact `numera_app` grant, and no dedupe-table RLS statements.
- The API Release build and runtime Testcontainers migration checks remain blocked until the new
  Billing project receives its first networked restore (missing `obj/project.assets.json`).

## Deviations and uncertainties

- The exact EF scaffold command could not build in the offline sandbox because the newly created
  Billing project has no assets file. Per the plan's required fallback, the migration, full migration
  designer, and model snapshot were hand-authored; the designer model is a faithful copy of the
  updated snapshot.
- Runtime PostgreSQL migration application and registration integration assertions remain for the
  reviewer-run Testcontainers suite after restore.
- No files were staged or committed. The pre-existing Stripe.net 52.2.0 package reference and
  untracked `.claude/` directory were left untouched.

