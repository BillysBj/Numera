---
phase: 01-plattform-kern
plan: 08
subsystem: testing
tags: [testcontainers, postgres, rls, audit, xunit, github-actions, ci, multi-tenancy, pwa]

# Dependency graph
requires:
  - phase: 01-plattform-kern (01-01)
    provides: NumeraDbContext + TenantConnectionInterceptor (app.current_tenant GUC, SET LOCAL/RESET) + TenantContext
  - phase: 01-plattform-kern (01-02)
    provides: FORCE RLS + WITH CHECK policies on every tenant table + least-privilege numera_app (NO BYPASSRLS) role
  - phase: 01-plattform-kern (01-04)
    provides: append-only audit_events (REVOKE UPDATE/DELETE + BEFORE UPDATE OR DELETE trigger) + AuditEvent entity
  - phase: 01-plattform-kern (01-06)
    provides: live register->login->RLS->audit loop wired end-to-end
  - phase: 01-plattform-kern (01-07)
    provides: React 19 PWA (app-shell precache, /api NetworkOnly) + DE/EN i18n
provides:
  - Numera.IntegrationTests project (real postgres:18 via Testcontainers) applying actual migrations + RLS + audit policies
  - Cross-tenant isolation suite as numera_app with IgnoreQueryFilters (RLS is the sole control under test) — success criterion 2
  - Audit immutability suite proving UPDATE/DELETE on audit_events is DB-rejected — success criterion 3
  - Pool-leak suite proving SET LOCAL/RESET stops stale-GUC cross-tenant leak (RESEARCH Pitfall 1)
  - .github/workflows/ci.yml gating backend (build + unit + Testcontainers integration) + web (build + test); RLS/audit as hard gates
  - Human-verified full Phase 1 foundation loop (register->session->i18n->PWA->tier gates)
affects: [02, every phase touching tenant data, e-invoicing, CI]

# Tech tracking
tech-stack:
  added: [Testcontainers.PostgreSql, xUnit integration project]
  patterns:
    - "Integration tests run as the non-BYPASSRLS numera_app role (NOT the container superuser) so RLS is actually exercised — a superuser silently bypasses FORCE RLS and makes isolation tests falsely pass"
    - "IgnoreQueryFilters() on every assertion disables the EF app-level filter so Row-Level Security is the SOLE isolation control under test"
    - "Role created BEFORE migrating so the audit migration's role-guarded REVOKE engages; baseline DML granted + audit REVOKE re-applied after for deterministic final privilege state"
    - "Single-connection pool (MaxPoolSize=MinPoolSize=1) forces physical connection reuse to deterministically catch a stale tenant GUC"
    - "RLS/audit suites are their own required CI steps (no continue-on-error) = hard gate"

key-files:
  created:
    - tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj
    - tests/Numera.IntegrationTests/PostgresFixture.cs
    - tests/Numera.IntegrationTests/RlsIsolationTests.cs
    - tests/Numera.IntegrationTests/AuditImmutabilityTests.cs
    - tests/Numera.IntegrationTests/PoolLeakTests.cs
    - .github/workflows/ci.yml
  modified:
    - Numera.sln

key-decisions:
  - "Integration assertions run as numera_app (NO BYPASSRLS), never the migrator/superuser, so RLS is the real control; combined with IgnoreQueryFilters this makes the suite fail if RLS regresses"
  - "RLS and audit-immutability suites are non-continue-on-error CI steps (hard gates) — a cross-tenant leak or a mutable audit row fails the whole pipeline (DSGVO/GoBD guard)"
  - "CI validated locally (yaml parse + all gated suites run green locally); the first live GitHub Actions run is pending a git remote — no green Actions run is claimed"

patterns-established:
  - "Non-retrofittable foundations (tenant isolation, audit immutability, pool-safety) are proven on real Postgres in CI, not asserted by app-code discipline"
  - "Shared container ICollectionFixture across suites; per-test isolation via fresh tenant GUIDs (append-only audit needs no cleanup)"

# Metrics
duration: 60min
completed: 2026-07-10
---

# Phase 01 Plan 08: Real-Postgres Test Gate + CI Summary

**Testcontainers (postgres:18) integration suite that applies the actual migrations + RLS + append-only audit policies and, running as the non-BYPASSRLS numera_app role with EF filters disabled, proves cross-tenant isolation, audit immutability, and pool-leak safety — wired as hard CI gates and confirmed by a full human-verified Phase 1 end-to-end loop.**

## Performance

- **Duration:** ~60 min (code committed pre-checkpoint 15:12–15:20; remainder was human-verify + finalization)
- **Started:** 2026-07-10T15:12:25Z (first task commit)
- **Completed:** 2026-07-10
- **Tasks:** 3 (2 auto committed pre-checkpoint + 1 human-verify approved)
- **Files modified:** 7

## Accomplishments
- `Numera.IntegrationTests` boots a real `postgres:18`, applies the production EF migrations (tables + RLS + audit REVOKE/trigger), and exposes a factory that opens `NumeraDbContext` as the least-privilege `numera_app` role bound to a tenant — the correctness detail that makes RLS actually engage.
- `RlsIsolationTests` (success criterion 2): tenant A sees only A's rows and zero B rows; a cross-tenant INSERT is rejected by the WITH CHECK policy; an unset tenant GUC fails closed — all with `IgnoreQueryFilters()` so RLS is the sole control.
- `AuditImmutabilityTests` (success criterion 3): INSERT+SELECT succeed, but UPDATE and DELETE on `audit_events` are DB-rejected (accepts either the `permission denied` REVOKE layer or the `append-only` trigger).
- `PoolLeakTests` (RESEARCH Pitfall 1): a single-connection pool forces physical reuse; tenant B on the reused connection sees only B's rows, plus a concurrent-tenants variant over a small pool.
- `ci.yml` gates backend (restore/build + unit + Testcontainers integration) and web (npm ci + build + test); the RLS and audit suites are required, non-continue-on-error steps (hard gates).
- Human-verify (Task 3) approved: register → authenticated dashboard, persistent HttpOnly-cookie session with no tokens in web storage, DE/EN i18n toggle with persistence, installable PWA with offline shell and no stale `/api` data, tier gates.

## Task Commits

1. **Task 1: Testcontainers fixture + RLS cross-tenant + pool-leak suites** - `1966362` (test)
2. **Task 2: Audit immutability suite + GitHub Actions CI** - `d94b8f9` (feat)
3. **Task 3: Human-verify the full Phase 1 foundation end to end** - approved (no code; verification gate)

**Plan metadata:** committed with STATE.md (docs)

## Files Created/Modified
- `tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj` - xUnit + Testcontainers.PostgreSql; references Platform.Db/Tenancy/Audit + Modules.Ledger
- `tests/Numera.IntegrationTests/PostgresFixture.cs` - starts postgres:18, creates numera_app before migrating, applies real migrations, exposes numera_app context factory + single-connection helper
- `tests/Numera.IntegrationTests/RlsIsolationTests.cs` - cross-tenant read isolation, WITH CHECK reject, fail-closed on unset GUC (CI gate)
- `tests/Numera.IntegrationTests/AuditImmutabilityTests.cs` - INSERT/SELECT ok; UPDATE/DELETE rejected by REVOKE or trigger (CI gate)
- `tests/Numera.IntegrationTests/PoolLeakTests.cs` - reused pooled connection + concurrent-tenant isolation (RESET proof)
- `.github/workflows/ci.yml` - backend build+unit+integration (Testcontainers hard gate) + web build+test
- `Numera.sln` - added the integration test project

## Decisions Made
- **Assert as numera_app, not the migrator/superuser** — a superuser bypasses `FORCE ROW LEVEL SECURITY`, which would make isolation tests falsely pass. The fixture grants baseline DML to `numera_app` and re-applies the audit REVOKE, then every assertion runs on that role.
- **IgnoreQueryFilters on all assertions** — deliberately switches off the EF global query filter (the app-level defence-in-depth) so RLS alone decides visibility; the suite therefore fails if RLS ever regresses.
- **RLS + audit suites are hard CI gates** — their own required steps with no `continue-on-error`, so a leak (DSGVO) or a mutable audit row (GoBD) fails the whole pipeline.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] EF PendingModelChangesWarning during Migrate in the fixture**
- **Found during:** Task 1 (fixture applying real migrations)
- **Issue:** The integration project did not reference `Numera.Modules.Ledger`, so the model the test host built differed from the migrations snapshot and `Database.MigrateAsync()` tripped `PendingModelChangesWarning` (the shared migration in Platform.Db covers Ledger tables via reflective module discovery).
- **Fix:** Referenced `Numera.Modules.Ledger` from the test project so the model matches the migration snapshot; used the `PostgreSqlBuilder("postgres:18")` image ctor.
- **Files modified:** tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj, tests/Numera.IntegrationTests/PostgresFixture.cs
- **Verification:** `dotnet test tests/Numera.IntegrationTests -c Release` → 8/8 green against real postgres:18.
- **Committed in:** `1966362` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking). **Impact:** Necessary to make the fixture migrate cleanly against the real database; no scope change.

## Verification Results
- Integration suite: **8/8 green** against real `postgres:18` as `numera_app` with `IgnoreQueryFilters` (RLS the sole control under test).
- Unit suite (Money golden files + entitlements): **16/16 green**.
- Solution build `-c Release`: **0 warnings / 0 errors**.
- Web: `npm ci` + build (PWA, 10 precache entries) + **4/4 tests** green.
- `ci.yml`: parses cleanly (backend + web jobs); RLS/audit modeled as required steps.
- **Task 3 human-verify: approved** — register→authenticated dashboard, persistent HttpOnly-cookie session (no tokens in localStorage/sessionStorage), DE/EN toggle persists across reload, installable standalone PWA with offline shell + no stale `/api` financial data, tier gates enforced.

## CI Caveat (honest record)
The repository has **no git remote yet**, so the CI workflow could not run on GitHub Actions and no live Actions run was observed. CI is **validated locally**: the YAML parses cleanly and every gated suite (unit, Testcontainers integration incl. `RlsIsolationTests` + `AuditImmutabilityTests`, web build/test) runs green locally. The **first live Actions run is pending a remote** — no green GitHub Actions run is claimed here.

## Issues Encountered
- None beyond the EF model/migration snapshot mismatch documented as deviation 1.

## User Setup Required
None - no new external service configuration. (Existing Docker stack: postgres:18 + keycloak:26 with imported realm; Docker daemon required for the Testcontainers suite.)

## Next Phase Readiness
- Non-retrofittable Phase 1 foundations (tenant isolation, audit immutability, pool-safety) are proven on real Postgres and gated in CI; the whole register→login→i18n→PWA→tier-gate loop is human-verified.
- **Phase 1 execution complete (8/8 plans).**
- Follow-up (not blocking): add a git remote and observe the first green GitHub Actions run.

## Self-Check: PASSED

All claimed artifacts present on disk (integration project + 4 suites, ci.yml, SUMMARY) and both task commits (`1966362`, `d94b8f9`) present in git history.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
