---
phase: 02-stammdaten
plan: 01
subsystem: database
tags: [ef-core-10, query-filters, soft-delete, multi-tenancy, rls]

# Dependency graph
requires:
  - phase: 01-plattform-kern
    provides: NumeraDbContext with unnamed tenant query filter + ITenantEntity + RLS isolation
provides:
  - IArchivable marker (DateTimeOffset? ArchivedAt) for soft-delete/archive
  - Named "Tenant" query filter on every ITenantEntity (was unnamed)
  - Named "NotArchived" query filter on every IArchivable entity
  - NumeraDbContext.TenantFilter / NotArchivedFilter public name constants
affects: [02-stammdaten later waves, any Phase-2 entity that is archivable, CRM, catalog]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "EF Core 10 NAMED query filters — tenancy and archival compose instead of overwriting"
    - "Reflective EF.Property predicate for filters over reflectively-discovered entity types"

key-files:
  created:
    - src/platform/Numera.Platform.Db/IArchivable.cs
  modified:
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs

key-decisions:
  - "Tenant + NotArchived are two independent NAMED filters, never combined with && — each can be disabled individually"
  - "Archival is app-level only; RLS deliberately does NOT filter archived rows (archived data is still the tenant's own data, RESEARCH.md Pattern 1)"
  - "NotArchived predicate built via reflective EF.Property<DateTimeOffset?> to match reflectively-discovered IArchivable entities (no compile-time reference)"

patterns-established:
  - "Named query filters: unnamed HasQueryFilter is banned in NumeraDbContext — a second unnamed filter would silently overwrite the first (data-leak-class bug)"
  - "IgnoreQueryFilters() no-arg (used by integration tests) disables ALL named filters, leaving RLS as the sole control"

# Metrics
duration: 3min
completed: 2026-07-11
---

# Phase 2 Plan 01: Named Query Filters + IArchivable Summary

**EF Core 10 named "Tenant" + "NotArchived" query filters that compose without overwriting, plus the IArchivable soft-delete marker — the query-filter foundation every Phase-2 table builds on.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-07-11T01:14:01Z
- **Completed:** 2026-07-11T01:17:05Z
- **Tasks:** 2
- **Files modified:** 2 (1 created, 1 modified)

## Accomplishments
- Added `IArchivable` marker (`DateTimeOffset? ArchivedAt`) documenting archive as an app-level-only concern (RLS does not filter archived rows).
- Converted the existing unnamed tenant query filter to a named `"Tenant"` filter, preserving the exact nullable-`Guid?` capture semantics (null current tenant → no rows).
- Added a second named `"NotArchived"` filter applied to every `IArchivable` entity, built reflectively via `EF.Property<DateTimeOffset?>` so it works over reflectively-discovered types.
- Exposed `NumeraDbContext.TenantFilter` / `NotArchivedFilter` name constants for selective `IgnoreQueryFilters([...])`.
- Verified Phase-1 suites unaffected: the integration tests call `IgnoreQueryFilters()` no-arg → all named filters off → RLS remains sole control.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add IArchivable marker** - `7cfcd61` (feat)
2. **Task 2: Convert to named query filters (Tenant + NotArchived)** - `0a4d09c` (feat)

**Plan metadata:** see final docs commit.

## Files Created/Modified
- `src/platform/Numera.Platform.Db/IArchivable.cs` - New marker interface `IArchivable` with `DateTimeOffset? ArchivedAt`; documents archival as app-level, orthogonal to RLS.
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs` - Added `TenantFilter`/`NotArchivedFilter` constants; `ApplyTenantQueryFilters` now registers the named `"Tenant"` filter; new `ApplyArchiveQueryFilters` registers the named `"NotArchived"` filter; both wired into `OnModelCreating`.

## Decisions Made
- Kept the two filters as separate named filters (never `&&`-combined) so tenancy and archival can be disabled independently — the whole point of the EF Core 10 named-filter API.
- Archive filter predicate uses reflective `EF.Property` (not a typed lambda) to mirror how the tenant filter is built, since archivable entities are discovered reflectively with no compile-time reference.
- Left RLS deliberately unaware of `ArchivedAt`: archived rows remain the tenant's own data (RESEARCH.md Pattern 1), so archival stays purely app-level.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Bash shell initially resolved SDK 8.0.303 instead of 10.0.301; resolved by prepending the user-local `%LOCALAPPDATA%/Microsoft/dotnet` (Unix-style path in Git Bash) to PATH and setting `DOTNET_ROOT` + `DOTNET_MULTILEVEL_LOOKUP=0`, as noted in STATE.md caveats. Not a code issue.

## Verification Results
- `dotnet build Numera.sln` — 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests` — 8/8 passed (RlsIsolationTests, AuditImmutabilityTests, PoolLeakTests unchanged).
- `dotnet test tests/Numera.Platform.Tests` — 16/16 passed.
- Grep confirms only named `HasQueryFilter(TenantFilter, ...)` and `HasQueryFilter(NotArchivedFilter, ...)` calls remain; no single-arg `HasQueryFilter` left.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- `IArchivable` is available for Phase-2 CRM/Catalog entities; adding it to a new entity now automatically applies the `"NotArchived"` filter with zero extra wiring.
- Named-filter foundation is in place, so subsequent Phase-2 waves (which depend on this wave-1 plan) can add archivable tenant entities without risk of overwriting the tenant filter.
- No blockers.

## Self-Check: PASSED

- FOUND: src/platform/Numera.Platform.Db/IArchivable.cs
- FOUND: src/platform/Numera.Platform.Db/NumeraDbContext.cs
- FOUND: .planning/phases/02-stammdaten/02-01-SUMMARY.md
- FOUND commit: 7cfcd61 (Task 1)
- FOUND commit: 0a4d09c (Task 2)

---
*Phase: 02-stammdaten*
*Completed: 2026-07-11*
