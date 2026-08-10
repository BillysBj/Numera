---
phase: 14-monetarisierung-stripe
plan: 05
subsystem: billing-degradation-guard
tags: [dotnet, aspnet-core, middleware, stripe, postgres, rls, testcontainers]

requires:
  - phase: 14-01
    provides: Tenant plan, subscription status, and app-managed trial deadline
  - phase: 14-04
    provides: Webhook-authoritative paid, retry, cancellation, and reactivation state transitions
provides:
  - "Request-time billing degradation and advisory effective-plan resolution"
  - "Authoritative global write guard with explicit Stripe recovery allow-list"
  - "Real-PostgreSQL coverage for active, trial, lapsed, canceled, and past-due states"
affects: [billing-status, degradation-banner, baseline-crud, entitlement-asymmetry]

tech-stack:
  added: []
  patterns:
    - "Scoped memoized billing-state resolver over the RLS tenant row"
    - "Tenant-resolution-first independent middleware guards"
    - "Reads-lenient and writes-strict trial-lapse behavior"

key-files:
  created:
    - src/Numera.Api/Auth/BillingStateService.cs
    - src/Numera.Api/Auth/BillingReadOnlyAccessPolicy.cs
    - src/Numera.Api/Auth/BillingDegradationWriteGuardMiddleware.cs
    - tests/Numera.IntegrationTests/BillingDegradationGuardTests.cs
  modified:
    - src/Numera.Api/Program.cs
    - src/Numera.Api/Endpoints/BillingEndpoints.cs

key-decisions:
  - "The billing write guard is authoritative; EffectivePlanAsync is advisory and EntitlementService remains unchanged."
  - "past_due retains full access while Stripe Smart Retries are active."
  - "Trial lapse is computed on every request; no recurring sweep job exists."

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-10
---

# Phase 14-05: Billing degradation write guard

Lapsed trials and terminal failed-payment states now become genuinely read-only across baseline and
premium writes, while every read, authentication flow, and Stripe recovery endpoint stays available.

## Implemented

- Added scoped `IBillingState` / `BillingStateService`. It reads the current tenant's plan,
  subscription status, and trial deadline under RLS once per scope and computes degradation at
  request time. `trialing`, `active`, `past_due`, or a future trial deadline retain access;
  canceled, unpaid, unknown/null status with no active trial, and lapsed trials degrade.
- `EffectivePlanAsync` returns the stored plan while access is valid and `TenantPlan.Free` while
  degraded. With no tenant in scope, billing degradation is not applicable and returns false.
- Added pure `BillingReadOnlyAccessPolicy`: GET/HEAD/OPTIONS always pass; `/health`, `/api/auth`,
  `/api/me`, Checkout, Portal, status, and webhook paths remain reachable; every other method/write
  is blocked. Comments explicitly document the visible data surfaces and recovery carve-outs.
- Added `BillingDegradationWriteGuardMiddleware`. Unauthenticated traffic passes without resolving
  billing state, non-degraded tenants pass, and degraded disallowed writes receive HTTP 403
  ProblemDetails with `error=billing_read_only` and German title/detail. It never returns 401,
  redirects, deletes rows, or hides reads.
- Registered `IBillingState` scoped and ordered the middleware immediately after
  `TenantResolutionMiddleware`, followed by the existing independent TaxAdvisor
  `ReadOnlyWriteGuardMiddleware`.
- Updated `GET /api/billing/status` to use the advisory `EffectivePlanAsync` and
  `IsDegradedAsync`, so lapsed app-managed trials expose plan Free/degraded to the frontend banner
  without writing the stored tenant plan.
- Added real-PostgreSQL integration cases for active subscription, within-trial, lapsed-trial,
  canceled, and `past_due`, including read preservation, Checkout recovery access, ProblemDetails,
  no redirect/401, and persisted tenant-data checks. Added database-free policy and unauthenticated
  bypass coverage.

## Locked entitlement asymmetry rationale

The write guard is the authoritative degradation mechanism; `EffectivePlanAsync` is advisory for
the billing status response and banner only. `EntitlementService` deliberately continues reading
`tenants.plan` directly:

1. D4's enforceable core is that writes are locked while all data stays visible and login remains
   available. The global write guard enforces that for every baseline and premium write regardless
   of the capability set.
2. On a real subscription cancellation or unpaid terminal state, the 14-04 webhook already writes
   `tenants.plan = Free`, so the existing entitlement service naturally collapses premium
   capabilities on that path; there is no asymmetry there.
3. The only asymmetric window is an app-managed trial lapse, because no Stripe event changes the
   stored plan. Keeping premium read surfaces visible in that narrow window is intentional under
   D4's "data stays intact and fully visible" rule, while the authoritative guard blocks every
   write. Rewiring the shared v1 entitlement service would incorrectly hide reads and broaden this
   phase's change.

This reads-lenient / writes-strict trial-lapse behavior is by design and must not be "corrected" by
making `EntitlementService` depend on `IBillingState`.

## Files created

- `src/Numera.Api/Auth/BillingStateService.cs`
- `src/Numera.Api/Auth/BillingReadOnlyAccessPolicy.cs`
- `src/Numera.Api/Auth/BillingDegradationWriteGuardMiddleware.cs`
- `tests/Numera.IntegrationTests/BillingDegradationGuardTests.cs`
- `.planning/phases/14-monetarisierung-stripe/14-05-SUMMARY.md`

## Files modified

- `src/Numera.Api/Program.cs`
- `src/Numera.Api/Endpoints/BillingEndpoints.cs`

`BillingEndpoints.cs` is an additional plan linkage required by the locked statement that
`EffectivePlanAsync` drives the billing status/banner while remaining advisory.

## Verification

Using `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe`:

- `build src/Numera.Api/Numera.Api.csproj --configuration Release --no-restore`: passed with 0
  warnings and 0 errors.
- `build tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj --configuration Release
  --no-restore`: passed with 0 warnings and 0 errors, compiling the complete new test suite.
- `test tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj --configuration Release
  --no-restore --no-build --filter FullyQualifiedName~BillingReadOnlyAccessPolicyTests`: 15 passed,
  0 failed. These are database-free allow-list and unauthenticated-bypass checks.
- Static source checks confirmed `past_due` is in the non-degraded set; Checkout, Portal, status,
  and webhook are allow-listed; middleware order is TenantResolution then BillingDegradation then
  TaxAdvisor ReadOnly; billing status uses the advisory service; and `EntitlementService` contains
  no billing-state reference and still projects `t.Plan` directly.
- `git diff --check`: passed.

## Pending Docker verification

Docker is down on this host, as stated before execution. The five Testcontainers PostgreSQL tests
in `BillingDegradationGuardTests` were therefore written and compiled but deliberately not run.
Run during the capstone when Docker is available:

```powershell
& 'C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe' test `
  'tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj' `
  --configuration Release --no-restore --filter BillingDegradationGuard
```

## Deviations from Plan

- No behavioral deviation. `BillingEndpoints.cs` was modified in addition to the frontmatter file
  list because the plan explicitly locks `EffectivePlanAsync` to the status/banner advisory path.

## Issues Encountered

- No build, compilation, or database-free test issue. Docker-dependent execution remains pending by
  environment constraint.

## User Setup Required

None.

## Next Phase Readiness

- The D4 backend gap is closed in source and compiled; only the capstone Docker run remains.
- No file was staged or committed.

---
*Phase: 14-monetarisierung-stripe · Plan: 05 · Tasks completed: 2/2*
