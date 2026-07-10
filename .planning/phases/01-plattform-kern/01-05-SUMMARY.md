---
phase: 01-plattform-kern
plan: 05
subsystem: entitlements
tags: [entitlements, feature-management, multi-tenancy, tier-gating, dotnet10, ef-core-10, plan-capability]

# Dependency graph
requires:
  - phase: 01-01
    provides: NumeraDbContext base, ICurrentTenant/TenantContext scoped abstraction
  - phase: 01-02
    provides: Tenant entity with TenantPlan (S/M/L/XL) column — the entitlement seam; RLS self-scoping tenants to the current tenant
provides:
  - Numera.Platform.Entitlements project (net10.0)
  - Plan (re-export of TenantPlan — single source of truth, no duplicate enum)
  - Capability enum (MultiUser/DataExport/Dunning/EInvoicing/ApiAccess)
  - PlanCapabilityMap — static S/M/L/XL -> capability matrix, strictly increasing by tier (single source of truth)
  - IEntitlementService / EntitlementService — server-authoritative resolution of current tenant plan -> capability set, deny-by-default
  - PlanFeatureFilter — Microsoft.FeatureManagement IFeatureFilter ([FilterAlias("Plan")]) delegating to IEntitlementService
  - EntitlementServiceTests — 9 resolution + gate tests
affects: [01-06-api-host-gating, 01-07-frontend-entitlement-visibility, invoicing-einvoicing-gate]

# Tech tracking
tech-stack:
  added:
    - Microsoft.FeatureManagement.AspNetCore 4.6.0 (Entitlements project)
    - Microsoft.EntityFrameworkCore.InMemory 10.0.* (test project — resolution tests without Postgres)
  patterns:
    - "Tier entitlement = pure projection of the persisted tenants.plan column; a future billing webhook flips one column and the whole capability surface follows"
    - "Single source of truth: PlanCapabilityMap is the ONLY place capabilities-per-plan are declared; enum re-export (global using Plan = TenantPlan) avoids a second tier enum"
    - "Server-authoritative gate: [FeatureGate] custom filter reads server-side entitlements (auth ∧ tenant ∧ entitlement); frontend visibility is cosmetic only"
    - "Deny-by-default: no tenant / unknown tenant / unknown capability -> empty capabilities / false"
    - "FeatureManagement filter holds zero plan logic — delegates entirely to IEntitlementService.HasCapabilityAsync"

key-files:
  created:
    - src/platform/Numera.Platform.Entitlements/Numera.Platform.Entitlements.csproj
    - src/platform/Numera.Platform.Entitlements/Plan.cs
    - src/platform/Numera.Platform.Entitlements/Capability.cs
    - src/platform/Numera.Platform.Entitlements/PlanCapabilityMap.cs
    - src/platform/Numera.Platform.Entitlements/IEntitlementService.cs
    - src/platform/Numera.Platform.Entitlements/EntitlementService.cs
    - src/platform/Numera.Platform.Entitlements/PlanFeatureFilter.cs
    - tests/Numera.Platform.Tests/Entitlements/EntitlementServiceTests.cs
  modified:
    - Numera.sln (added Entitlements project)
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj (retargeted net8.0 -> net10.0; +EF InMemory; +Entitlements ref)

key-decisions:
  - "Plan is a namespace-scoped re-export (global using Plan = Numera.Platform.Db.Entities.TenantPlan) rather than a new enum — keeps ONE tier definition on the tenant DB column and prevents drift between the DB seam and the entitlement layer"
  - "IEntitlementService made async (HasCapabilityAsync/CurrentCapabilitiesAsync) because resolving the tenant's plan requires a DB read; the resolved set is memoised per DI scope so repeated checks in one request are a single query"
  - "PlanFeatureFilter reads the capability from filter Parameters ([FilterAlias(\"Plan\")], Parameters.Capability) instead of a generic IContextualFeatureFilter<Capability>, so any feature can be gated by naming the capability in appsettings without a new filter type"
  - "DI registration NOT wired here — documented as an XML-doc snippet on PlanFeatureFilter for the Api host to consume in plan 01-06 (avoids touching the Api host, owned/edited by the parallel 01-04 executor)"
  - "No EF migration created — entitlements read the existing tenants.plan column; deliberately avoided a migration to prevent ordering races with the parallel 01-04 audit migration"

# Metrics
duration: ~15min
completed: 2026-07-10
---

# Phase 1 Plan 05: Tier Entitlements (Plan→Capability Map + EntitlementService + FeatureManagement Filter) Summary

**A server-authoritative tier entitlement layer: the tenant's persisted plan (S/M/L/XL) resolves through a single-source-of-truth `PlanCapabilityMap` to a fixed `Capability` set, exposed via an async `IEntitlementService` and enforced by a `Microsoft.FeatureManagement` custom filter that delegates entirely to that service — deny-by-default, no payment code.**

## Performance

- **Duration:** ~15 min
- **Completed:** 2026-07-10
- **Tasks:** 2
- **Files created/modified:** 10 (8 created, 2 modified)

## Accomplishments
- **Single source of truth for tiers:** `Plan.cs` re-exports the existing `TenantPlan` (S/M/L/XL) enum from plan 01-02 via `global using Plan = ...TenantPlan` — no duplicate enum. `grep` confirms exactly one `enum TenantPlan` definition repo-wide.
- **`PlanCapabilityMap`:** a static `FrozenDictionary<Plan, IReadOnlySet<Capability>>` mapping each tier to a strictly-increasing capability set (S: DataExport → M: +MultiUser → L: +Dunning/EInvoicing → XL: +ApiAccess). This is the ONLY place capabilities-per-plan are declared; a `For(plan)` helper is deny-by-default for unknown plans.
- **`IEntitlementService`/`EntitlementService`:** resolves the current tenant via `ICurrentTenant`, reads that tenant's `Plan` from `NumeraDbContext` (RLS self-scopes the read to the caller's own row), and projects via the map. Memoises per DI scope; returns an empty set when no tenant / unknown tenant is in scope.
- **`PlanFeatureFilter`:** `[FilterAlias("Plan")]` `IFeatureFilter` reading the required `Capability` from filter parameters and delegating to `IEntitlementService.HasCapabilityAsync`. `grep` confirms zero hard-coded plan logic in the filter. XML-doc documents the Api-host DI snippet (`AddFeatureManagement().AddFeatureFilter<PlanFeatureFilter>()`) for plan 01-06 to consume.
- **9 xUnit tests, all green:** S resolves the minimal set (no EInvoicing/ApiAccess), XL resolves the full set, sets are strictly increasing by tier, no-tenant and unknown-tenant yield empty (deny-by-default), and the filter returns true for a granted capability / false for a lacked one / false with no tenant / false for an unknown capability parameter. Full test project (16 tests incl. pre-existing Money) passes.

## Task Commits

1. **Task 1: Plan/Capability model + entitlement service** - `ba64aa8` (feat)
2. **Task 2: FeatureManagement filter + resolution tests** - `51a2d7e` (feat)

_Note: commit `d8d4a55` between them is the parallel 01-04 (audit) executor's, not part of this plan._

## Files Created/Modified
- `src/platform/Numera.Platform.Entitlements/Numera.Platform.Entitlements.csproj` - net10.0 project; refs Platform.Db + Tenancy + FeatureManagement.AspNetCore
- `src/platform/Numera.Platform.Entitlements/Plan.cs` - re-export of TenantPlan (single tier definition)
- `src/platform/Numera.Platform.Entitlements/Capability.cs` - illustrative Phase-1 capability enum
- `src/platform/Numera.Platform.Entitlements/PlanCapabilityMap.cs` - static S/M/L/XL -> capability matrix (single source of truth)
- `src/platform/Numera.Platform.Entitlements/IEntitlementService.cs` - async entitlement contract
- `src/platform/Numera.Platform.Entitlements/EntitlementService.cs` - resolves current tenant plan -> capability set, deny-by-default, scope-memoised
- `src/platform/Numera.Platform.Entitlements/PlanFeatureFilter.cs` - FeatureManagement filter delegating to the service
- `tests/Numera.Platform.Tests/Entitlements/EntitlementServiceTests.cs` - 9 resolution + gate tests
- `Numera.sln` - added the Entitlements project
- `tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj` - retargeted net8.0 -> net10.0, +EF InMemory, +Entitlements ref

## Decisions Made
- **`Plan` = re-export of `TenantPlan`, not a new enum.** The plan explicitly requires ONE tier definition. The DB column (`tenants.plan`) is the authoritative seam from 01-02, so a `global using` alias lets entitlement code say `Plan.S` while the values physically live on `Tenant.Plan`. No drift risk.
- **Async entitlement API.** Resolving the tenant's plan is a DB read, so `HasCapabilityAsync`/`CurrentCapabilitiesAsync` are async; the result is memoised for the DI scope so repeated per-request checks cost one query.
- **Parameter-driven filter, not generic contextual filter.** `[FilterAlias("Plan")]` reading `Parameters.Capability` means any feature can be gated by configuration alone (appsettings) without a new filter class per capability.
- **DI wiring deferred to the Api host (01-06).** Documented as an XML-doc code snippet rather than wired here — deliberately avoids editing the Api host, which the parallel 01-04 executor was actively modifying.
- **No EF migration.** Entitlements only read the existing `tenants.plan` column; creating a migration would have raced the parallel 01-04 audit migration for ordering. None needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Retargeted the test project net8.0 -> net10.0**
- **Found during:** Task 2 (adding the Entitlements project reference to the test project)
- **Issue:** `Numera.Platform.Tests` targeted `net8.0`, but the Entitlements assembly and its deps (Platform.Db/Tenancy) are `net10.0`; a net8.0 consumer cannot reference net10.0 assemblies, blocking the required tests.
- **Fix:** Retargeted the test project to `net10.0`. The net8.0 Money library it also references remains consumable via forward compatibility.
- **Files modified:** tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj
- **Verification:** Full test project builds and all 16 tests pass (9 new entitlement + 7 pre-existing Money) — no regression to the parallel 01-03 Money suite.
- **Committed in:** `51a2d7e` (Task 2 commit)

**2. [Rule 3 - Blocking] Added Microsoft.EntityFrameworkCore.InMemory to the test project**
- **Found during:** Task 2 (resolution tests need a seeded NumeraDbContext without Postgres)
- **Issue:** No in-memory EF provider was available to seed a Tenant row and exercise `EntitlementService` against a real `NumeraDbContext`.
- **Fix:** Added `Microsoft.EntityFrameworkCore.InMemory 10.0.*` to the test project (unique DB name per test for isolation).
- **Files modified:** tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj
- **Verification:** All 9 entitlement tests pass.
- **Committed in:** `51a2d7e` (Task 2 commit)

**3. [Rule 3 - Blocking] Softened three forward-reference `cref`s to plain text**
- **Found during:** Task 1 (first Release build)
- **Issue:** XML-doc `<see cref="PlanFeatureFilter"/>` references in Capability/IEntitlementService/PlanCapabilityMap pointed at a type created only in Task 2; with `TreatWarningsAsErrors`, CS1574 failed the Task 1 build.
- **Fix:** Replaced those three crefs with plain-text "plan feature filter" (the real cref would only resolve once Task 2 landed; not worth reordering the files).
- **Files modified:** Capability.cs, IEntitlementService.cs, PlanCapabilityMap.cs
- **Verification:** Task 1 builds `-c Release` with 0 warnings / 0 errors.
- **Committed in:** `ba64aa8` (Task 1 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 3 - blocking). No architectural changes, no scope creep, no payment code.

## Coordination With Parallel Executor (01-04 Audit)
- **Shared files touched by both:** `Numera.sln` (both add a project). At commit time the working-tree `Numera.sln` diff contained only this plan's Entitlements entries; staged `Numera.sln` explicitly and left 01-04's Api.csproj/NumeraDbContext/audit-migration changes unstaged.
- **`tests/Numera.Platform.Tests.csproj`** is NOT in this plan's `files_modified`, but the plan requires adding tests to this existing project, so it was retargeted + referenced minimally and additively (see Deviation 1/2). Its diff is entirely this plan's.
- **No migration created** by this plan (per environment note) to avoid ordering races with 01-04's `20260710023110_AuditEvents` migration.
- Left untouched: `src/platform/Numera.Platform.Audit/`, `src/platform/Numera.Platform.Tenancy/ICurrentUser.cs`, `NumeraDbContext.cs`, the audit migration, and Api.csproj — all 01-04 territory.

## Issues Encountered
- **`dotnet` PATH defaulting to SDK 8.0.303:** the machine `dotnet.exe` resolves first and fails global.json (pins 10.0.301). Resolved by invoking the user-local `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` explicitly with `DOTNET_ROOT`/`DOTNET_MULTILEVEL_LOOKUP=0` (same known issue from 01-01/01-02).
- **Solution folder duplication:** `dotnet sln add --solution-folder platform` created a second "platform" solution folder (new GUID) rather than nesting into the existing one. Cosmetic only — the build and test runs are unaffected; can be tidied if the sln is ever regenerated.

## User Setup Required
None for code. For the Api host to enforce gates (plan 01-06), register:
```csharp
services.AddScoped<IEntitlementService, EntitlementService>();
services.AddFeatureManagement().AddFeatureFilter<PlanFeatureFilter>();
// appsettings: "FeatureManagement": { "EInvoicing": { "EnabledFor": [ { "Name": "Plan", "Parameters": { "Capability": "EInvoicing" } } ] } }
```

## Next Phase Readiness
- **Plan 01-06 (Api host):** can register the service + filter (snippet above), gate endpoints with `[FeatureGate("EInvoicing")]`, and expose `/me/entitlements` from `IEntitlementService.CurrentCapabilitiesAsync`.
- **Plan 01-07 (frontend):** can consume `/me/entitlements` for cosmetic visibility — the server guard is already authoritative.
- **Future billing:** flipping `tenants.plan` (e.g. a Stripe webhook in a later phase) is the only change needed to move a tenant between capability sets; no code change to this layer.
- **Concern:** the second "platform" solution folder in `Numera.sln` from `dotnet sln add` is cosmetic but worth a tidy pass if the orchestrator regenerates the solution.

## Self-Check: PASSED

All 8 created artifacts exist on disk; both task commits (`ba64aa8`, `51a2d7e`) exist in git history. Entitlements project builds `-c Release` (0/0); `dotnet test --filter Entitlement` = 9 passed; full test project = 16 passed (no Money regression). `grep` confirms a single `TenantPlan` enum, `PlanCapabilityMap` as the sole capabilities-per-plan declaration, and no hard-coded plan logic in `PlanFeatureFilter`.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
