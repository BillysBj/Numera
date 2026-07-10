---
phase: 01-plattform-kern
plan: 01
subsystem: infra
tags: [dotnet10, ef-core-10, npgsql, postgres18, keycloak26, multi-tenancy, rls, ledger, docker-compose]

# Dependency graph
requires:
  - phase: 01-RESEARCH
    provides: RLS interceptor Pattern 1 (parameterized set_config + RESET), UUIDv7 PK guidance, decimal-money rule, inert-ledger directive
provides:
  - Numera.sln modular-monolith skeleton (.NET 10) wiring Api, Worker, Platform.Tenancy, Platform.Db, Modules.Ledger
  - ICurrentTenant / TenantContext scoped tenant abstraction
  - TenantConnectionInterceptor (parameterized set_config('app.current_tenant') on open, RESET on close — pool- and injection-safe)
  - NumeraDbContext base with interceptor registration + dynamic ITenantEntity global query filter
  - DesignTimeDbContextFactory for dotnet ef migrations
  - Inert double-entry ledger schema (Account, JournalEntry, Posting, IPostingSource)
  - docker-compose.yml (postgres:18 + keycloak:26.2 --features=organization)
affects: [01-02-rls-migrations, 01-05-auth-tenancy-middleware, 01-06-worker-hangfire, invoicing-posting-milestone]

# Tech tracking
tech-stack:
  added:
    - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.*
    - Microsoft.EntityFrameworkCore.Relational 10.0.*
    - Microsoft.Extensions.Hosting 10.0.*
    - .NET SDK 10.0.301 (provisioned user-local via dotnet-install.ps1)
  patterns:
    - "Tenant GUC via DbConnectionInterceptor: parameterized set_config on open + RESET on close"
    - "Defence-in-depth: EF global query filter mirrors DB RLS for every ITenantEntity"
    - "UUIDv7 PKs (Guid.CreateVersion7) mapping to PG18 uuidv7()"
    - "Money as decimal mapped numeric(19,4); never float/serial"
    - "Central Directory.Build.props (net10.0, Nullable, TreatWarningsAsErrors, LangVersion 14)"

key-files:
  created:
    - Numera.sln
    - src/Numera.Worker/Numera.Worker.csproj
    - src/Numera.Worker/Program.cs
    - src/platform/Numera.Platform.Tenancy/ICurrentTenant.cs
    - src/platform/Numera.Platform.Tenancy/TenantContext.cs
    - src/platform/Numera.Platform.Tenancy/TenantConnectionInterceptor.cs
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs
    - src/platform/Numera.Platform.Db/ITenantEntity.cs
    - src/platform/Numera.Platform.Db/DesignTimeDbContextFactory.cs
    - src/modules/Numera.Modules.Ledger/Account.cs
    - src/modules/Numera.Modules.Ledger/JournalEntry.cs
    - src/modules/Numera.Modules.Ledger/Posting.cs
    - src/modules/Numera.Modules.Ledger/IPostingSource.cs
  modified:
    - src/Numera.Api/Numera.Api.csproj (already present from WIP; referenced by sln)

key-decisions:
  - "Provisioned .NET SDK 10.0.301 user-local (~/.dotnet) because the machine only had SDK 8.0.303; global.json pins 10.0.301"
  - "Forced classic Numera.sln format (--format sln) because .NET 10 defaults to .slnx but the plan names Numera.sln as a required artifact"
  - "Interceptor lives in Platform.Tenancy referencing EntityFrameworkCore.Relational (DbConnectionInterceptor lives there)"
  - "Global query filter built dynamically over all ITenantEntity types via reflection, so modules get tenant filtering with zero per-entity config"
  - "Money project (plan 01-03) deliberately excluded from Numera.sln — owned by a parallel executor, outside this plan's files_modified"

patterns-established:
  - "Pattern 1 (RESEARCH.md): pool-safe, injection-safe tenant GUC interceptor"
  - "ITenantEntity marker drives both RLS (later) and EF query filter"

# Metrics
duration: ~35min (incl. .NET 10 SDK provisioning)
completed: 2026-07-10
---

# Phase 1 Plan 01: Plattform-Kern (Solution + Tenancy/DB Kernel + Inert Ledger) Summary

**.NET 10 modular-monolith skeleton with a pool-safe, injection-safe tenant-GUC connection interceptor, an ITenantEntity-driven DbContext base, an inert double-entry ledger schema, and postgres:18 + keycloak:26 docker infra.**

## Performance

- **Duration:** ~35 min (including provisioning the .NET 10 SDK)
- **Completed:** 2026-07-10
- **Tasks:** 3
- **Files created:** 16 (.cs/.csproj/.sln)

## Accomplishments
- Classic-format `Numera.sln` wiring five projects (Api, Worker, Platform.Tenancy, Platform.Db, Modules.Ledger); full solution builds `-c Release` on net10.0 with 0 warnings / 0 errors.
- `TenantConnectionInterceptor` sets `app.current_tenant` via a **parameterized** `SELECT set_config('app.current_tenant', @tenant, false)` on connection open and `RESET`s it on close — the injection- and pool-safe correction of the bytefish sample (RESEARCH.md Pattern 1), implemented for both sync and async paths.
- `NumeraDbContext` registers the interceptor via `AddInterceptors` and applies a reflection-built global query filter to every `ITenantEntity` (defence-in-depth mirror of RLS); `DesignTimeDbContextFactory` enables `dotnet ef`.
- Inert double-entry ledger: `Account`/`JournalEntry`/`Posting` (UUIDv7 PKs, tenant-scoped, `Amount` mapped `numeric(19,4)`, `Number` a plain string — no SEQUENCE), plus the `IPostingSource` seam for invoicing. No posting/balancing logic (sum-to-zero invariant documented only).
- `docker-compose.yml` (from WIP) validated via `docker compose config`: `postgres:18` and `quay.io/keycloak/keycloak:26.2 start-dev --features=organization`.

## Task Commits

1. **Task 1: Scaffold solution + worker host** - `f91f521` (feat)
2. **Task 2: Tenancy kernel + DbContext base + corrected interceptor** - `1eb4843` (feat)
3. **Task 3: Inert double-entry ledger schema** - `494d64d` (feat)

_Note: Task 1's global.json, Directory.Build.props, docker-compose.yml, and Numera.Api host were already committed in prior WIP `d5e3b22`; this run added the missing Numera.sln and Numera.Worker._

## Files Created/Modified
- `Numera.sln` - Classic-format solution wiring all five in-plan projects
- `src/Numera.Worker/{Numera.Worker.csproj,Program.cs}` - Empty generic host (Hangfire lands in 01-06)
- `src/platform/Numera.Platform.Tenancy/ICurrentTenant.cs` - Ambient tenant abstraction
- `src/platform/Numera.Platform.Tenancy/TenantContext.cs` - Scoped, mutable tenant holder
- `src/platform/Numera.Platform.Tenancy/TenantConnectionInterceptor.cs` - Parameterized set_config + RESET (sync + async)
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs` - Interceptor registration + dynamic ITenantEntity query filter
- `src/platform/Numera.Platform.Db/ITenantEntity.cs` - Tenant marker interface
- `src/platform/Numera.Platform.Db/DesignTimeDbContextFactory.cs` - Design-time factory (no-op tenant)
- `src/modules/Numera.Modules.Ledger/{Account,JournalEntry,Posting,IPostingSource}.cs` - Inert ledger schema

## Decisions Made
- **Provisioned .NET 10 SDK user-local:** the machine had only SDK 8.0.303 while global.json pins 10.0.301. Used the official `dotnet-install.ps1` to install `10.0.301` into `~/.dotnet` (non-admin) rather than downgrade the plan's net10.0 target.
- **Forced classic `.sln`:** .NET 10 `dotnet new sln` defaults to `.slnx`; the plan names `Numera.sln` as a required artifact, so used `--format sln`.
- **Interceptor placement:** kept in Platform.Tenancy with an `EntityFrameworkCore.Relational` package reference (that's where `DbConnectionInterceptor` lives), so Platform.Db composes it via DI without a cyclic dependency.
- **Money project excluded from sln:** `Numera.Platform.Money` is plan 01-03 (parallel executor) and outside this plan's `files_modified`; left untouched and unreferenced.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Provisioned .NET 10 SDK**
- **Found during:** Execution start (environment check)
- **Issue:** Only .NET SDK 8.0.303 was installed; `dotnet build` failed because global.json pins 10.0.301, blocking every task's build verification.
- **Fix:** Installed SDK 10.0.301 user-local via the official `dotnet-install.ps1 -Channel 10.0` (matches the pinned version exactly).
- **Files modified:** None in repo (SDK installed to `~/.dotnet`).
- **Verification:** `dotnet --version` → 10.0.301; full-solution `dotnet build -c Release` succeeds.
- **Committed in:** N/A (environment, not code)

**2. [Rule 3 - Blocking] Fixed ConnectionClosing override signatures for EF Core 10**
- **Found during:** Task 2 (first Release build)
- **Issue:** The RESEARCH.md sample's `ConnectionClosing(DbConnection, ConnectionEventData)` does not exist in EF Core 10; the real overrides take a third `InterceptionResult` and return it (async returns `ValueTask<InterceptionResult>`). Build error CS0115.
- **Fix:** Changed both overrides to the correct EF Core 10 signatures returning `result`.
- **Files modified:** src/platform/Numera.Platform.Tenancy/TenantConnectionInterceptor.cs
- **Verification:** Solution builds `-c Release`, 0 warnings; RESET still runs on connection close.
- **Committed in:** `1eb4843` (Task 2 commit)

**3. [Rule 3 - Blocking] Added Microsoft.Extensions.Hosting to Worker**
- **Found during:** Task 1 (Release build)
- **Issue:** `Microsoft.NET.Sdk.Worker`'s implicit `Microsoft.Extensions.Hosting` global using did not resolve, error CS0234 on `Host.CreateApplicationBuilder`.
- **Fix:** Added explicit `Microsoft.Extensions.Hosting 10.0.*` package reference to Numera.Worker.csproj.
- **Files modified:** src/Numera.Worker/Numera.Worker.csproj
- **Verification:** Worker builds; host composes.
- **Committed in:** `f91f521` (Task 1 commit)

**4. [Rule 3 - Blocking] Removed stale net8.0 build artifacts**
- **Found during:** Execution start (per resume context known issue)
- **Issue:** A prior stale build left `bin/obj Release/net8.0` artifacts under Numera.Platform.Money.
- **Fix:** Deleted `bin/`+`obj/` under that project (source files left untouched — they belong to plan 01-03).
- **Files modified:** None tracked (bin/obj are gitignored).
- **Verification:** `git status` clean of artifacts; fresh build targets net10.0 for all in-plan projects.
- **Committed in:** N/A (untracked artifacts)

---

**Total deviations:** 4 auto-fixed (all Rule 3 - blocking).
**Impact on plan:** All were prerequisites to satisfy the plan's own `dotnet build -c Release` must-have. No scope creep; no architectural change; no files outside this plan's `files_modified` were altered.

## Issues Encountered
- **Docker daemon not running** in this environment, so `docker compose up -d postgres` and the `docker compose ps` healthcheck could not be executed. Mitigation: `docker compose config` validates successfully, confirming the compose file is structurally correct (postgres:18 + keycloak:26.2 with the organization feature). Actual container startup deferred to when a Docker daemon is available (needed anyway for plan 01-02 RLS migration/testcontainers work).

## User Setup Required
None for code. Operationally, a running Docker daemon is required to bring up `docker compose` (postgres:18 + keycloak:26.2) before plan 01-02's RLS migration and integration tests. The .NET 10 SDK is now installed user-local at `~/.dotnet`.

## Next Phase Readiness
- Compiling kernel is ready: plan 01-02 can add RLS policies + EF migrations against `NumeraDbContext`; the tenant GUC interceptor and `ITenantEntity` filter are already wired.
- Plan 01-05 can implement the tenancy middleware that calls `ICurrentTenant.SetTenant`.
- Plan 01-06 can add Hangfire to the empty Worker host.
- Ledger schema is a stable seam for the later invoicing posting milestone via `IPostingSource`.
- Concern: verify the Money project (01-03) and web/ (01-07) executors integrate cleanly; Money currently targets net8.0 and is not in Numera.sln.

## Self-Check: PASSED

All 7 spot-checked artifacts exist on disk; all 3 task commits (`f91f521`, `1eb4843`, `494d64d`) exist in git history. Full solution builds `dotnet build -c Release` with 0 warnings / 0 errors on net10.0.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
