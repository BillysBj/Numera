---
phase: 01-plattform-kern
plan: 02
subsystem: database
tags: [postgres18, rls, row-level-security, ef-core-10, migrations, multi-tenancy, least-privilege, keycloak, uuidv7]

# Dependency graph
requires:
  - phase: 01-01
    provides: NumeraDbContext base + TenantConnectionInterceptor (app.current_tenant GUC), ITenantEntity marker + global query filter, inert ledger schema, DesignTimeDbContextFactory, docker-compose (postgres:18)
provides:
  - Tenant (Mandant) entity — self-scoped, Keycloak-org-id PK, TenantPlan (S/M/L/XL) entitlement seam
  - Membership entity (ITenantEntity) — user<->tenant mirror of Keycloak org, MembershipRole (Owner/Employee/TaxAdvisor) seam, unique (TenantId, UserId)
  - InitialPlatform EF migration creating tenants, membership + inert ledger (accounts, journal_entries, postings)
  - FORCE ROW LEVEL SECURITY + tenant_isolation (USING + WITH CHECK) on every tenant table — DB-enforced isolation from the first migration
  - tenant_id-leading composite index on every tenant table
  - scripts/db-roles.sql — numera_migrator (owns DDL) + numera_app (DML only, NO BYPASSRLS/SUPERUSER)
  - Sql/rls_policies.sql canonical RLS reference
  - NumeraDbContext snake_case schema convention + reflective module-entity discovery
affects: [01-03-audit-append-only, 01-04-entitlements, 01-05-auth-tenancy-middleware, 01-06-worker-hangfire, invoicing-posting-milestone]

# Tech tracking
tech-stack:
  added:
    - Microsoft.EntityFrameworkCore.Design 10.0.* (Api startup project, PrivateAssets=all)
  patterns:
    - "RLS via migrationBuilder.Sql: ENABLE + FORCE + CREATE POLICY tenant_isolation (USING + WITH CHECK) on every tenant table in the initial migration"
    - "tenants self-scoped on id (the row IS the tenant); tenant-scoped tables scoped on tenant_id vs app.current_tenant GUC"
    - "Least-privilege role split: numera_migrator owns DDL, numera_app runs DML with no BYPASSRLS — FORCE RLS makes ownership non-bypassing"
    - "tenant_id-leading composite index on every tenant table (RESEARCH Pitfall 2: ~100x slower RLS without it)"
    - "snake_case physical schema convention applied in OnModelCreating"
    - "Reflective module-entity discovery: probe DbContext-assembly directory for Numera.Modules.*.dll so a shared migration covers module tables without a circular reference"

key-files:
  created:
    - src/platform/Numera.Platform.Db/Entities/Tenant.cs
    - src/platform/Numera.Platform.Db/Entities/Membership.cs
    - src/platform/Numera.Platform.Db/Migrations/20260710021521_InitialPlatform.cs
    - src/platform/Numera.Platform.Db/Migrations/20260710021521_InitialPlatform.Designer.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql
    - scripts/db-roles.sql
  modified:
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs
    - src/Numera.Api/Numera.Api.csproj
    - docker-compose.yml

key-decisions:
  - "tenants is self-scoped by RLS on id (not tenant_id) and is NOT an ITenantEntity — the row IS the tenant"
  - "FORCE ROW LEVEL SECURITY on every table so even the owning migrator role is subject to policy; numera_app holds no BYPASSRLS, making RLS the unconditional primary control"
  - "Shared migration lives in Platform.Db but must include ledger tables owned by Modules.Ledger (which references Platform.Db) — resolved by reflectively loading Numera.Modules.*.dll from the DbContext-assembly directory instead of a compile-time reference (which would be circular)"
  - "Api is the ef startup project so its dependency closure deploys the module DLLs next to Platform.Db for design-time discovery; added EF Core Design there"
  - "Applied snake_case naming convention in OnModelCreating so the hand-written RLS SQL (tenant_id, accounts, journal_entries, postings) matches the physical schema"
  - "Fixed docker-compose postgres:18 volume mount to /var/lib/postgresql (pg18 refuses .../data mount)"

patterns-established:
  - "Every future tenant table: implement ITenantEntity -> auto tenant_id-leading index + query filter + (add its ENABLE/FORCE RLS + tenant_isolation policy to a migration)"
  - "Runtime connects as numera_app (no bypass); dotnet ef database update connects as numera_migrator"

# Metrics
duration: ~35min
completed: 2026-07-10
---

# Phase 1 Plan 02: Tenant/Membership Model + DB-Enforced RLS Isolation Summary

**Tenant (Mandant) and Membership model plus an initial EF Core 10 migration that turns on FORCE ROW LEVEL SECURITY with tenant_isolation (USING + WITH CHECK) policies and tenant_id-leading composite indexes on every tenant table, backed by a least-privilege numera_app role that holds no BYPASSRLS — verified isolating against a fresh postgres:18.**

## Performance

- **Duration:** ~35 min
- **Completed:** 2026-07-10T02:18Z
- **Tasks:** 2
- **Files created/modified:** 10

## Accomplishments
- **Tenant model:** `Tenant` (Mandant) with a UUIDv7 PK that IS the Keycloak organization id, a `TenantPlan` (S/M/L/XL) column feeding plan 01-04 entitlements, and `CreatedAt`. Deliberately NOT an `ITenantEntity` — it is self-scoped by RLS on `id`.
- **Membership model:** `Membership : ITenantEntity` mirroring Keycloak org membership — `TenantId`, `UserId` (Keycloak sub), `MembershipRole` (Owner/Employee/TaxAdvisor — Phase 8 seam), with a unique tenant-leading `(TenantId, UserId)` index.
- **Initial migration** `InitialPlatform` creates `tenants`, `membership`, and the inert ledger tables (`accounts`, `journal_entries`, `postings`) discovered from Modules.Ledger, all in snake_case.
- **DB-enforced isolation:** the migration applies `ENABLE` + `FORCE ROW LEVEL SECURITY` and a `tenant_isolation` policy (USING + WITH CHECK) to all five tenant tables — tenant-scoped tables on `tenant_id = current_setting('app.current_tenant')::uuid`, `tenants` self-scoped on `id`. A matching `Down` drops policies and disables RLS.
- **Composite indexes:** every tenant table has a `tenant_id`-leading composite index (RESEARCH Pitfall 2).
- **Least-privilege roles:** `scripts/db-roles.sql` defines `numera_migrator` (owns schema/DDL) and `numera_app` (DML only, explicitly `NOSUPERUSER NOBYPASSRLS`).
- **End-to-end verified against a fresh postgres:18:** migration applied cleanly; `pg_class` shows `relrowsecurity` AND `relforcerowsecurity` true on all five tables; querying `membership` as `numera_app` with the tenant-1 GUC returns 1 row, with the tenant-2 GUC returns 0 rows; a cross-tenant INSERT is rejected by `WITH CHECK` ("new row violates row-level security policy"); both roles confirmed `rolbypassrls=false`, `rolsuper=false`.

## Task Commits

1. **Task 1: Tenant + Membership entities and least-privilege DB roles** - `fdd39f3` (feat)
2. **Task 2: Initial migration with RLS policies and composite indexes** - `be70b84` (feat)

_Note: NumeraDbContext and Api.csproj edits span both tasks conceptually but were grouped with Task 2 (the migration work that required them: snake_case naming, module-entity discovery, EF Design tool package)._

## Files Created/Modified
- `src/platform/Numera.Platform.Db/Entities/Tenant.cs` - Mandant, self-scoped, plan seam
- `src/platform/Numera.Platform.Db/Entities/Membership.cs` - user<->tenant mirror of Keycloak org
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs` - DbSets, Tenant/Membership config, reflective module-entity discovery + tenant-leading indexes, snake_case naming
- `src/platform/Numera.Platform.Db/Migrations/20260710021521_InitialPlatform.cs` - table creation + RLS enable/force/policy SQL
- `src/platform/Numera.Platform.Db/Migrations/*.Designer.cs`, `NumeraDbContextModelSnapshot.cs` - EF model snapshot
- `src/platform/Numera.Platform.Db/Sql/rls_policies.sql` - canonical RLS reference copy
- `scripts/db-roles.sql` - numera_migrator + numera_app least-privilege roles
- `src/Numera.Api/Numera.Api.csproj` - references Modules.Ledger + EF Core Design (ef startup project)
- `docker-compose.yml` - postgres:18 volume mount fix

## Decisions Made
- **`tenants` is self-scoped, not tenant-scoped:** the row IS the tenant, so its policy uses `id = current_setting('app.current_tenant')::uuid` and it is not an `ITenantEntity`.
- **FORCE RLS everywhere + no-bypass app role:** RLS is `FORCE`d so even the schema-owning migrator is subject to it, and `numera_app` (the runtime role) is `NOBYPASSRLS`, making RLS the unconditional primary isolation control. This was concretely proven when the superuser bootstrap role (`numera`, `rolbypassrls=t`) saw across tenants but `numera_app` did not.
- **Shared migration includes module tables without a circular reference:** Modules.Ledger references Platform.Db, so Platform.Db cannot reference it. Rather than move the migration or the marker interface, `NumeraDbContext` reflectively loads `Numera.Modules.*.dll` from its own assembly directory (the module DLLs are deployed there via the Api startup project) and registers any `ITenantEntity` it finds.
- **snake_case schema convention** applied in `OnModelCreating` so the hand-written RLS SQL lines up with real column/table names.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed postgres:18 Docker volume mount path**
- **Found during:** Task 2 (bringing up postgres for the migration)
- **Issue:** The compose file (from plan 01-01) mounted the volume at `/var/lib/postgresql/data`. postgres:18 stores data in a major-version subdirectory and refuses to start with data at that path, leaving the container permanently `unhealthy` — blocking `dotnet ef database update`.
- **Fix:** Changed the mount to `/var/lib/postgresql` (per docker-library/postgres#37, PR #1259) and recreated the container with a fresh volume.
- **Files modified:** docker-compose.yml
- **Verification:** Container reports `healthy`; migration applied.
- **Committed in:** `be70b84` (Task 2 commit)

**2. [Rule 3 - Blocking] Added Microsoft.EntityFrameworkCore.Design to Api startup project**
- **Found during:** Task 2 (`dotnet ef migrations add`)
- **Issue:** The EF tools require the startup project to reference `Microsoft.EntityFrameworkCore.Design`; the Api did not, so migration scaffolding aborted.
- **Fix:** Added the package (PrivateAssets=all) to Numera.Api.csproj.
- **Files modified:** src/Numera.Api/Numera.Api.csproj
- **Verification:** `dotnet ef migrations add` / `database update` succeed.
- **Committed in:** `be70b84` (Task 2 commit)

**3. [Rule 3 - Blocking] Made module (ledger) tables visible to the shared migration**
- **Found during:** Task 2 (first migration only contained tenants + membership)
- **Issue:** The plan requires the migration to cover the inert ledger tables, but those types live in Modules.Ledger which references Platform.Db (a compile-time reference back would be circular), and .NET does not eagerly load the module assembly at design time — so EF never saw the ledger entities.
- **Fix:** Added `EnsureModuleAssembliesLoaded()` in `NumeraDbContext` that (a) walks the referenced-assembly graph and (b) probes the DbContext-assembly directory for `Numera.Modules.*.dll` and load-from's them; ran `dotnet ef` with the Api as `--startup-project` so those DLLs are deployed alongside Platform.Db. Also added explicit ledger table names and a snake_case pass so names match the RLS SQL.
- **Files modified:** src/platform/Numera.Platform.Db/NumeraDbContext.cs, src/Numera.Api/Numera.Api.csproj
- **Verification:** Regenerated migration contains accounts, journal_entries, postings with tenant_id-leading indexes; applies to fresh DB.
- **Committed in:** `be70b84` (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 3 - blocking).
**Impact on plan:** All three were prerequisites to satisfy the plan's own must-haves (migration applies to fresh postgres:18; RLS on every table; ledger tables covered). No architectural change to the plan's intent, no scope creep. The module-discovery approach avoids the only real structural question (circular reference) without altering plan 01-01's reference direction.

## Issues Encountered
- **Local `dotnet` PATH resolution:** the shell defaulted to the machine-wide SDK 8.0.303 for SDK subcommands even with `--version` reporting 10.0.301. Resolved by prepending the user-local `.dotnet` dir to PATH, setting `DOTNET_ROOT`/`DOTNET_MULTILEVEL_LOOKUP=0`, and running `hash -r` so child EF-tool processes resolve SDK 10.0.301.
- **Superuser bypass observation:** an early isolation test using the `numera` bootstrap role (a superuser) appeared to leak across tenants. This was expected superuser RLS bypass, not a policy bug — re-running as `numera_app` confirmed correct isolation. It concretely validates the least-privilege role split.

## User Setup Required
None for code. Operationally, before running the app or migrations against a new database:
1. Run `scripts/db-roles.sql` once as a superuser to create `numera_migrator` / `numera_app` (replace the dev placeholder passwords with real secrets in prod).
2. Point `dotnet ef database update` at `numera_migrator` and the app's `ConnectionStrings` at `numera_app`.

## Next Phase Readiness
- **Plan 01-03 (audit/append-only):** can add its audit tables to a new migration and revoke UPDATE/DELETE from `numera_app` on them (db-roles.sql notes this exception is deferred to 01-03).
- **Plan 01-04 (entitlements):** the `TenantPlan` column is the ready seam.
- **Plan 01-05 (auth/tenancy middleware):** will resolve the tenant from the Keycloak org and call `ICurrentTenant.SetTenant`, which the interceptor turns into the `app.current_tenant` GUC that these RLS policies read.
- **Blocker cleared:** the plan 01-01 docker blocker is resolved — postgres:18 comes up healthy with the corrected mount, Testcontainers/integration tests are unblocked.
- **Concern:** the `postings` FK index `ix_postings_journal_entry_id` is not tenant-leading (EF auto-adds it for the FK); the tenant-leading `ix_postings_tenant_id_id` exists alongside it and covers RLS access, so no action needed unless FK-join plans regress.

## Self-Check: PASSED

All 10 spot-checked artifacts exist on disk; both task commits (`fdd39f3`, `be70b84`) exist in git history. The migration was applied to a fresh postgres:18 and verified live: RLS enabled+forced on all 5 tables, tenant_isolation policies present, `numera_app` isolates correctly (1 row for own tenant, 0 for another), WITH CHECK blocks cross-tenant insert, and both DB roles are `rolbypassrls=false`/`rolsuper=false`.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
