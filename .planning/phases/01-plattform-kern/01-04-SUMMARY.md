---
phase: 01-plattform-kern
plan: 04
subsystem: audit
tags: [postgres18, rls, append-only, immutability, trigger, audit-log, gobd, ef-core-10, jsonb, uuidv7]

# Dependency graph
requires:
  - phase: 01-01
    provides: NumeraDbContext base + TenantConnectionInterceptor (app.current_tenant GUC), ITenantEntity marker + reflective module-entity discovery, DesignTimeDbContextFactory
  - phase: 01-02
    provides: FORCE RLS + tenant_isolation pattern on every tenant table, numera_migrator/numera_app least-privilege roles (audit UPDATE/DELETE REVOKE deferred to this plan), tenant-leading composite index convention
provides:
  - ICurrentUser seam (Platform.Tenancy) — nullable actor id, referenced by AuditWriter and the Api's CurrentUser impl (01-06)
  - Numera.Platform.Audit project — AuditEvent (append-only entity), IAuditEvent, IAuditWriter, AuditWriter
  - AuditEvent entity (ITenantEntity) — actor/action/entityType/entityId + before/after jsonb + occurred_at + reserved nullable prev_hash/row_hash (bytea)
  - Synchronous in-transaction AuditWriter — appends to the caller's NumeraDbContext (no new transaction/scope, no SaveChanges)
  - AuditEvents EF migration — audit_events table with ENABLE+FORCE RLS + tenant_isolation, REVOKE UPDATE/DELETE (guarded) + GRANT INSERT/SELECT, BEFORE UPDATE OR DELETE trigger raising 'audit_events is append-only', (tenant_id, occurred_at) + (tenant_id, id) indexes
affects: [01-06-auth-current-user, invoicing-finalize-milestone, gobd-archive-hash-chain]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DB-enforced append-only: REVOKE UPDATE,DELETE from app role (privilege layer) + BEFORE UPDATE OR DELETE trigger RAISE EXCEPTION (unconditional layer) — belt-and-braces immutability"
    - "Role-existence-guarded GRANT/REVOKE (DO $$ IF EXISTS pg_roles $$) so migrations apply in CI without the app role, while the trigger remains the real enforcement"
    - "Seam placement: ICurrentUser lives in Platform.Tenancy (low-level) so Platform.Audit consumes it without depending on Numera.Api"
    - "Synchronous in-transaction audit writer: Add to caller's DbContext, no own transaction/scope/SaveChanges — audit row commits atomically with the recorded change"
    - "Reserved nullable prev_hash/row_hash (bytea) columns keep a future GoBD hash-chain a non-breaking add"
    - "Reflective cross-assembly entity discovery extended to Numera.Platform.Audit.dll (same cycle-avoidance as Numera.Modules.*.dll) so the shared migration sees AuditEvent"

key-files:
  created:
    - src/platform/Numera.Platform.Tenancy/ICurrentUser.cs
    - src/platform/Numera.Platform.Audit/Numera.Platform.Audit.csproj
    - src/platform/Numera.Platform.Audit/AuditEvent.cs
    - src/platform/Numera.Platform.Audit/IAuditEvent.cs
    - src/platform/Numera.Platform.Audit/IAuditWriter.cs
    - src/platform/Numera.Platform.Audit/AuditWriter.cs
    - src/platform/Numera.Platform.Db/Migrations/20260710023110_AuditEvents.cs
    - src/platform/Numera.Platform.Db/Migrations/20260710023110_AuditEvents.Designer.cs
  modified:
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/Numera.Api/Numera.Api.csproj

key-decisions:
  - "ICurrentUser placed in Platform.Tenancy (alongside ICurrentTenant), not Numera.Api — Platform.Audit must not depend on the Api host; Platform.Tenancy is the shared low-level home both the writer and the 01-06 Api impl reference"
  - "Two independent append-only layers: REVOKE UPDATE/DELETE (fails as 'permission denied' for numera_app) AND a BEFORE UPDATE OR DELETE trigger (fails as 'audit_events is append-only' even for a privileged/superuser/mis-granted role). The trigger is the unconditional guarantee; the REVOKE is defence-in-depth"
  - "GRANT/REVOKE guarded by pg_roles existence so the migration applies to a fresh CI DB lacking numera_app; the trigger (always created) remains the real enforcement"
  - "AuditWriter writes synchronously into the caller's NumeraDbContext — no new transaction, no DI scope, no SaveChanges — so the audit row and the audited change are one atomic unit of work"
  - "prev_hash/row_hash reserved as nullable bytea now (unused in Phase 1) so the GoBD-archive hash chain is a non-breaking migration later"
  - "Extended NumeraDbContext's reflective assembly probe to also load Numera.Platform.Audit.dll (Audit references Db, so a compile-time reference back would be circular — same solution as the ledger module)"

patterns-established:
  - "Any future append-only/immutable table: REVOKE UPDATE,DELETE + BEFORE UPDATE OR DELETE trigger RAISE, plus the standard ENABLE/FORCE RLS + tenant_isolation policy"
  - "Finance modules call IAuditWriter.RecordAsync INSIDE their finalize/change transaction; the writer never opens its own"

# Metrics
duration: ~10min
completed: 2026-07-10
---

# Phase 1 Plan 04: Immutable Append-Only Audit Log Summary

**A DB-enforced immutable `audit_events` table (tenant-scoped RLS, jsonb before/after, reserved hash columns) made append-only by two independent layers — REVOKE UPDATE/DELETE from the app role plus a BEFORE UPDATE OR DELETE trigger that raises — fronted by an `ICurrentUser` seam and a synchronous `AuditWriter` that appends in the caller's transaction. Verified live on postgres:18.**

## Performance

- **Duration:** ~10 min
- **Completed:** 2026-07-10T02:34Z
- **Tasks:** 2
- **Files created/modified:** 11

## Accomplishments

- **ICurrentUser seam** in `Numera.Platform.Tenancy` — `Guid? UserId` (nullable for system/unauthenticated contexts), placed low so `Platform.Audit` consumes it without an Api dependency; the concrete `CurrentUser` (from the authenticated `sub` claim) arrives in 01-06.
- **`Numera.Platform.Audit` project** (references Platform.Db + Platform.Tenancy): `AuditEvent : ITenantEntity` (UUIDv7 PK, `TenantId`, `ActorUserId`, `Action`, `EntityType`, nullable `EntityId`, `Before`/`After` mapped `jsonb`, `OccurredAt`, and **reserved nullable** `PrevHash`/`RowHash` as `bytea`), `IAuditEvent` (recordable change), `IAuditWriter`, and `AuditWriter`.
- **Synchronous in-transaction `AuditWriter`** — stamps tenant (`ICurrentTenant`) + actor (`ICurrentUser`) and `Add`s an `AuditEvent` to the caller's scoped `NumeraDbContext`. It opens **no** transaction, creates **no** DI scope, calls **no** `SaveChanges` — so the audit row commits atomically with the change the caller is recording.
- **`AuditEvents` migration** on `audit_events`: `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` (USING + WITH CHECK) matching every other tenant table; `REVOKE UPDATE, DELETE` / `GRANT INSERT, SELECT` to `numera_app` (guarded by `pg_roles` existence for CI); a `BEFORE UPDATE OR DELETE` trigger `audit_immutable` running `audit_no_mutate()` which `RAISE EXCEPTION 'audit_events is append-only'`; plus `(tenant_id, occurred_at)` and tenant-leading `(tenant_id, id)` indexes. A full `Down` reverses trigger/function/policy/grants and drops the table.
- **Extended reflective discovery** so the shared Platform.Db migration sees `AuditEvent` (probe now also loads `Numera.Platform.Audit.dll`, mirroring the ledger-module cycle-avoidance) and named the table `audit_events` in `OnModelCreating`.
- **Verified live on a fresh-applied postgres:18:** migration applies cleanly; RLS `relrowsecurity`+`relforcerowsecurity` both true; trigger present; `numera_app` grants are exactly INSERT+SELECT. Functionally: INSERT + SELECT under a set tenant GUC succeed; **UPDATE/DELETE as `numera_app` → "permission denied"** (REVOKE layer); **UPDATE/DELETE as the privileged owner → "audit_events is append-only"** (trigger layer); a different tenant GUC returns 0 rows and a cross-tenant INSERT is rejected by WITH CHECK.

## Task Commits

1. **Task 1: ICurrentUser seam + audit entity + synchronous in-transaction writer** - `d8d4a55` (feat)
2. **Task 2: AuditEvents migration — REVOKE + trigger + RLS (append-only)** - `7a7fa51` (feat)

## Files Created/Modified

- `src/platform/Numera.Platform.Tenancy/ICurrentUser.cs` - Nullable actor-id seam (low-level home for both writer and Api impl)
- `src/platform/Numera.Platform.Audit/Numera.Platform.Audit.csproj` - New project (refs Platform.Db + Platform.Tenancy)
- `src/platform/Numera.Platform.Audit/AuditEvent.cs` - Append-only entity; jsonb before/after; reserved bytea prev/row hash
- `src/platform/Numera.Platform.Audit/IAuditEvent.cs` - Recordable-change contract (no tenant/actor — stamped by writer)
- `src/platform/Numera.Platform.Audit/IAuditWriter.cs` - Writer seam (documents in-transaction contract)
- `src/platform/Numera.Platform.Audit/AuditWriter.cs` - Synchronous in-transaction writer (no txn/scope/SaveChanges)
- `src/platform/Numera.Platform.Db/Migrations/20260710023110_AuditEvents.cs` - Table + RLS + REVOKE + trigger + indexes; reversible Down
- `src/platform/Numera.Platform.Db/Migrations/20260710023110_AuditEvents.Designer.cs` - EF migration metadata
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs` - Snapshot now includes AuditEvent/audit_events
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs` - ConfigureAuditEvents (table name + occurred_at index); probe extended to Platform.Audit.dll
- `src/Numera.Api/Numera.Api.csproj` - References Platform.Audit so its assembly deploys for design-time discovery + host wiring (01-06)

## Decisions Made

- **ICurrentUser lives in Platform.Tenancy, not Numera.Api:** Platform.Audit must not depend on the Api host, so the actor seam sits in the shared low-level project (alongside ICurrentTenant); the Api's concrete `CurrentUser` in 01-06 implements it from the authenticated principal.
- **Two independent append-only layers:** the REVOKE gives `numera_app` INSERT/SELECT only (a privilege wall that surfaces "permission denied"), while the `BEFORE UPDATE OR DELETE` trigger is the *unconditional* guarantee — it raises "audit_events is append-only" even for a mis-granted role, the table owner, or a superuser. App-code discipline is explicitly insufficient (success criterion 3), so both are DB-enforced.
- **Role-existence-guarded grants:** wrapped the REVOKE/GRANT in `DO $$ IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='numera_app') $$` so the migration applies against a fresh CI DB without the app role; the always-created trigger keeps immutability intact regardless.
- **Synchronous, in-caller-transaction writer:** RecordAsync only `Add`s to the caller's context (no transaction/scope/SaveChanges) so the audit row and the recorded change are one atomic commit — the core requirement that an audit entry cannot be lost while its change persists (or vice-versa).
- **Reserved hash columns now:** `prev_hash`/`row_hash` are nullable `bytea` from day one so the GoBD-archive tamper-evident chain is a later non-breaking add; no chaining logic exists in Phase 1.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Extended reflective entity discovery to Platform.Audit.dll**
- **Found during:** Task 1 (registering the AuditEvents DbSet / making the migration see AuditEvent)
- **Issue:** The plan says "Register AuditEvents DbSet in NumeraDbContext", but `AuditEvent` lives in `Numera.Platform.Audit` which references `Numera.Platform.Db` — a strongly-typed `DbSet<AuditEvent>` (or any compile-time reference) in NumeraDbContext would be a circular dependency. This is the same structural constraint 01-02 hit with the ledger module. Additionally, the design-time EF host loads only the DbContext assembly, so `AuditEvent` was invisible to migration scaffolding.
- **Fix:** Reused the established pattern — extended `NumeraDbContext.EnsureModuleAssembliesLoaded` to also probe/load `Numera.Platform.Audit.dll` (so its `ITenantEntity` is reflectively registered, indexed, and query-filtered like ledger entities), added `ConfigureAuditEvents` to name the `audit_events` table and add the `(tenant_id, occurred_at)` index, and referenced Platform.Audit from Numera.Api so the DLL is deployed next to Platform.Db for design-time discovery.
- **Files modified:** src/platform/Numera.Platform.Db/NumeraDbContext.cs, src/Numera.Api/Numera.Api.csproj
- **Verification:** `dotnet ef migrations add AuditEvents` scaffolds the full `audit_events` table with both indexes; `dotnet build -c Release` 0 warnings/0 errors; migration applies to postgres:18.
- **Committed in:** `d8d4a55` (NumeraDbContext/Api) and `7a7fa51` (migration/snapshot)

**Note (not a deviation):** the plan's REVOKE targets `numera_app`, which 01-02's `scripts/db-roles.sql` explicitly deferred to this plan — this migration is where that exception is now applied.

---

**Total deviations:** 1 auto-fixed (Rule 3 - blocking, structural cycle-avoidance reusing the existing pattern). No architectural change, no scope creep. No functional intent of the plan changed — the DbSet is registered reflectively rather than as a typed property, achieving identical mapping, RLS filter, and indexing.

## Issues Encountered

- **Dev-DB test row is now permanent:** the append-only INSERT test row in `audit_events` (tenant `...00a1`) cannot be deleted — that is the feature working as designed. Harmless in the dev database; a fresh CI/prod DB starts empty.
- **Solution file already contained the Audit project:** `Numera.sln` in HEAD already referenced `Numera.Platform.Audit` (a `dotnet sln add` produced an identical, no-diff result), so no shared-file edit was needed — avoiding merge risk with the parallel 01-05 executor. `.planning/STATE.md` and `01-05-SUMMARY.md` (touched by that executor) were deliberately left unstaged in the per-task commits.

## Authentication Gates

None.

## User Setup Required

None for code. Operationally: run `scripts/db-roles.sql` once (creates `numera_app`/`numera_migrator`) before applying migrations so the REVOKE/GRANT block engages; without it the migration still applies and the trigger still enforces immutability.

## Next Phase Readiness

- **Plan 01-06 (auth/current-user):** implement `ICurrentUser` in the Api host from the authenticated principal's `sub` claim and register `IAuditWriter`→`AuditWriter` in DI (scoped, alongside `NumeraDbContext`).
- **Invoicing finalize milestone:** call `IAuditWriter.RecordAsync` inside the finalize transaction to record `invoice.finalized` with before/after jsonb.
- **GoBD-archive phase:** populate the reserved `prev_hash`/`row_hash` to add a tamper-evident chain — no schema break required.

## Self-Check: PASSED

All created files exist on disk; both task commits (`d8d4a55`, `7a7fa51`) exist in git history. The migration was applied to postgres:18 and verified live: audit_events has ENABLE+FORCE RLS + tenant_isolation, the audit_immutable trigger, exactly INSERT+SELECT for numera_app, and both indexes; INSERT/SELECT under a tenant GUC succeed while UPDATE (permission denied / append-only) and DELETE are blocked, and cross-tenant reads/inserts are isolated by RLS.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
