---
phase: 02-stammdaten
plan: 05
subsystem: api
tags: [minimal-api, fluentvalidation, catalog, artikelstamm, audit, rls, rfc7807, catl-02]

# Dependency graph
requires:
  - phase: 02-03
    provides: Numera.Modules.Catalog (CatalogItem + owned pricing, CatalogItemKind, UnitOfMeasure UN/ECE Rec 20 set) + _Catalog RLS migration with partial-unique item number
  - phase: 02-04
    provides: FluentValidation DI (AddValidatorsFromAssemblyContaining<Program>) scanning the Api assembly + the atomic mutate->audit endpoint pattern to mirror
  - phase: 02-01
    provides: named "Tenant" + "NotArchived" query filters and IArchivable soft-delete
  - phase: 01-04
    provides: IAuditWriter append-in-caller's-transaction audit seam
provides:
  - Catalog management HTTP API — /api/catalog-items CRUD + archive/unarchive (no hard delete)
  - CATL-02 seam — CatalogLineItem DTO + picker endpoint (?picker=true) returning the stable line-item projection Phase 3's invoice line editor snapshots
  - Graceful 409 on duplicate article number (partial-unique index -> Postgres 23505 -> ValidationProblem, never a 500)
  - Atomic mutate->audit write pattern (single SaveChanges) for every catalog mutation
affects: [02-06, phase-03-invoicing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Picker/lookup as a query flag (?picker=true) on the list route -> capped CatalogLineItem[] for Phase-3 line import"
    - "Duplicate-key -> 409: catch DbUpdateException where inner PostgresException.SqlState == UniqueViolation, return RFC 7807 ValidationProblem(statusCode 409)"
    - "CatalogAuditEvent : IAuditEvent adapter supplies action/before/after JSON; writer stamps tenant+actor"
    - "Blank UnitCode defaulted via UnitOfMeasure.DefaultFor(Kind) at the endpoint; validator only rejects a non-empty non-curated code"

key-files:
  created:
    - src/Numera.Api/Contracts/CatalogContracts.cs
    - src/Numera.Api/Validators/CatalogValidators.cs
    - src/Numera.Api/Endpoints/CatalogEndpoints.cs
  modified:
    - src/Numera.Api/Program.cs

key-decisions:
  - "Picker is a flag on the list route (GET /api/catalog-items?picker=true) returning a capped (Take 20) CatalogLineItem[] rather than a separate /lookup route — one route, one contract, additive"
  - "CatalogLineItem documented as a SNAPSHOT source: Phase 3 copies number/name/unit/price/tax onto the invoice line at creation; the catalog is NOT the source of truth once a line exists (editing/archiving an item never mutates a posted line)"
  - "Duplicate (tenant_id, item_number) among non-archived rows surfaces from the partial-unique index as Postgres 23505; caught and returned as a 409 ValidationProblem on ItemNumber, never a 500"
  - "No ICurrentUser dependency in the catalog handlers — catalog has no activity timeline (unlike partners); audit still stamps the actor from ambient context"

patterns-established:
  - "Catalog validators live in the Api assembly and were auto-discovered by 02-04's AddValidatorsFromAssemblyContaining<Program>() scan — zero DI edits, only app.MapCatalogEndpoints() appended"

# Metrics
duration: 6min
completed: 2026-07-11
---

# Phase 2 Plan 05: Catalog Backend API Summary

**Minimal-API catalog (Artikelstamm) surface — /api/catalog-items CRUD + archive plus the CATL-02 picker (CatalogLineItem projection) — validated by FluentValidation, audited via an atomic mutate->audit SaveChanges, with graceful 409 on duplicate article numbers.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-07-11T11:59:28Z
- **Completed:** 2026-07-11T12:05:04Z
- **Tasks:** 2
- **Files modified:** 4 (3 created, 1 modified)

## Accomplishments
- Catalog list with server-side pagination, text search over ItemNumber+Name and an archived toggle, returning an `{items,page,pageSize,total}` envelope
- Create/read/update a catalog item with RFC 7807 ValidationProblem on invalid input; unit code constrained to the curated UN/ECE Rec 20 set (BT-130); net/cost prices are exact decimals, VAT rate range-checked
- Archive/unarchive endpoints (POST) with NO catalog hard-delete route; every mutation writes an AuditEvent in one SaveChanges
- **CATL-02 seam:** `GET /api/catalog-items?picker=true` returns a capped `CatalogLineItem[]` (id, number, name, unit code, net price, tax category, VAT rate) — the stable shape Phase 3's invoice line editor imports and snapshots
- Duplicate article numbers return a clean 409 ValidationProblem (from the partial-unique index), never a 500
- No DI edit — the 02-04 assembly scan auto-registered the two catalog validators; only `app.MapCatalogEndpoints()` was appended

## Task Commits

Each task was committed atomically:

1. **Task 1: Catalog DTOs + CatalogLineItem seam + validators** - `aac2624` (feat)
2. **Task 2: Catalog CRUD + archive + picker endpoints** - `678043e` (feat)

**Plan metadata:** _(this docs commit)_

## Files Created/Modified
- `src/Numera.Api/Contracts/CatalogContracts.cs` - sealed-record DTOs (create/update/list/detail) + `CatalogLineItem` (the CATL-02 snapshot projection); decimal money, never float
- `src/Numera.Api/Validators/CatalogValidators.cs` - AbstractValidator rules: article-number/name lengths, UN/ECE Rec 20 unit code, NetPrice/CostPrice >= 0, VAT rate 0..100, ISO 4217 currency
- `src/Numera.Api/Endpoints/CatalogEndpoints.cs` - MapCatalogEndpoints: CRUD + archive + picker; CatalogAuditEvent adapter, JSON snapshot helper, duplicate-key -> 409 mapping
- `src/Numera.Api/Program.cs` - `app.MapCatalogEndpoints()` after `app.MapPartnerEndpoints()`

## Decisions Made
- **Picker shape:** a `?picker=true` flag on the list route (not a separate `/lookup` route) returns a capped `CatalogLineItem[]`. One route, one additive contract for Phase 3.
- **Snapshot semantics:** `CatalogLineItem` is documented as a copy source — Phase 3 snapshots number/name/unit/price/tax onto the line at creation; later catalog edits/archival never touch a posted line.
- **Duplicate handling:** the partial-unique `(tenant_id, item_number)` index throws Postgres 23505 -> caught as `DbUpdateException` with an inner `PostgresException { SqlState: UniqueViolation }` -> 409 ValidationProblem on `ItemNumber`.
- **No actor dependency:** catalog handlers take no `ICurrentUser` (no activity timeline like partners); the audit writer still stamps the actor from ambient context.

## Deviations from Plan

None - plan executed exactly as written. (The plan's `audit.RecordAsync(action,id,before,after,ct)` sketch was already known from 02-04 to be an `IAuditEvent` seam; the `CatalogAuditEvent` adapter mirrors 02-04's `PartnerAuditEvent` and was written correctly the first time, so it is a design continuation, not a deviation.)

## Issues Encountered
None. Build 0/0; unit 43/43; integration 25/25 (unchanged — no new tests, backend-only plan mirroring 02-04).

## Authentication Gates
None.

## User Setup Required
None - no external service configuration. FluentValidation is a code dependency; no env vars or dashboards.

## Next Phase Readiness
- **02-06 (frontend, running in parallel):** the list endpoint's `{items,page,pageSize,total}` + `page/pageSize/q/archived` params are ready for TanStack Table manualPagination; `?picker=true` is ready for a catalog item picker component.
- **Phase 3 (invoicing):** `CatalogLineItem` is the stable CATL-02 import contract — an invoice line editor calls the picker, then snapshots the returned fields onto the line. Keep the shape additive-only.

## Self-Check: PASSED

- FOUND: src/Numera.Api/Contracts/CatalogContracts.cs
- FOUND: src/Numera.Api/Validators/CatalogValidators.cs
- FOUND: src/Numera.Api/Endpoints/CatalogEndpoints.cs
- FOUND commit: aac2624
- FOUND commit: 678043e

---
*Phase: 02-stammdaten*
*Completed: 2026-07-11*
