---
phase: 02-stammdaten
plan: 03
subsystem: catalog
tags: [catalog, article, un-ece-rec20, rls, ef-core-10, money-precision, multi-tenancy, migration]

# Dependency graph
requires:
  - phase: 02-stammdaten
    plan: 01
    provides: Named "Tenant" + "NotArchived" query filters + IArchivable marker
  - phase: 02-stammdaten
    plan: 02
    provides: _Crm migration in the model snapshot (this migration chains after it) + owned-type-safe snake-caser
  - phase: 01-plattform-kern
    provides: NumeraDbContext reflective ITenantEntity discovery + RLS + Testcontainers PostgresFixture
provides:
  - Numera.Modules.Catalog module (single CatalogItem entity)
  - UN/ECE Rec 20 unit-code helper (UnitOfMeasure.Codes / IsValid / DefaultFor)
  - _Catalog migration with per-table RLS (ENABLE+FORCE+tenant_isolation) for catalog_items
  - Partial-unique per-tenant article-number index (non-archived only)
  - CatalogRlsTests — cross-tenant RLS + per-tenant unique-number + archive-filter CI hard gate
affects: [02-stammdaten later waves, CATL-01/02, Phase-3 invoicing line items, Phase-5 XRechnung BT-130, Phase-6 DATEV revenue accounts]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Store the UN/ECE Rec 20 code (BT-130) only; German display labels are a frontend concern (no full code table)"
    - "Money-precision prices as decimal [Precision(19,4)] -> numeric(19,4), never float"
    - "Per-table hand-written RLS SQL in the shared migration (reflective discovery never creates policies)"
    - "Partial unique index for a per-tenant business number scoped to non-archived rows"

key-files:
  created:
    - src/modules/Numera.Modules.Catalog/Numera.Modules.Catalog.csproj
    - src/modules/Numera.Modules.Catalog/CatalogItem.cs
    - src/modules/Numera.Modules.Catalog/CatalogEnums.cs
    - src/modules/Numera.Modules.Catalog/UnitOfMeasure.cs
    - src/platform/Numera.Platform.Db/Migrations/20260711094613_Catalog.cs
    - src/platform/Numera.Platform.Db/Migrations/20260711094613_Catalog.Designer.cs
    - tests/Numera.IntegrationTests/CatalogRlsTests.cs
    - tests/Numera.Platform.Tests/Catalog/UnitOfMeasureTests.cs
  modified:
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql
    - src/Numera.Api/Numera.Api.csproj
    - tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj
    - Numera.sln

key-decisions:
  - "One CatalogItem entity carries both Product and Service kinds (CatalogItemKind flag), not separate entities — kind drives the default unit (C62 vs HUR)"
  - "Net/cost prices are plain decimal [Precision(19,4)] columns (not an owned Money value object) — matches the plan's artifact contract and needs no owned-type mapping; still Money-precision numeric(19,4)"
  - "Store only the UN/ECE Rec 20 code (BT-130); the curated allowed set + German labels live in code/frontend, not a DB code table"
  - "Article number unique per tenant only among non-archived rows (partial unique index) — an archived item's number is reusable and a different tenant may reuse any number"
  - "catalog_items RLS policy hand-written via migrationBuilder.Sql — reflective entity discovery never emits policies (the #1 silent-leak trap)"

patterns-established:
  - "Second Phase-2 table lands in its own wave so the two `dotnet ef migrations add` runs chain cleanly through the model snapshot (no migration-ordering race, no PendingModelChangesWarning)"

# Metrics
duration: 20min
completed: 2026-07-11
---

# Phase 2 Plan 03: Catalog Data Layer (CatalogItem + RLS + UN/ECE Rec 20 units) Summary

**Numera.Modules.Catalog: one `CatalogItem` (article number, UN/ECE Rec 20 unit code, numeric(19,4) net/cost price, EN 16931 TaxCategory + VAT rate, archivable) with a `_Catalog` migration whose table is RLS-isolated per tenant on real Postgres and enforces a per-tenant-unique article number among non-archived rows.**

## Performance
- **Duration:** ~20 min
- **Tasks:** 3
- **Files:** 14 (8 created, 6 modified)

## Accomplishments
- Created the `Numera.Modules.Catalog` project: `CatalogItem` (`ITenantEntity` + `IArchivable`) with article number, name/description, `CatalogItemKind`, UN/ECE Rec 20 `UnitCode`, `numeric(19,4)` `NetPrice`/`CostPrice`, ISO-4217 `Currency`, EN 16931 `TaxCategory` + `VatRatePercent`, and a nullable `RevenueAccount` DATEV seam.
- Added `UnitOfMeasure`: the curated UN/ECE Rec 20 code list (`C62`, `H87`, `HUR`, `DAY`, `MON`, `KGM`, `MTR`, `MTK`, `LTR`, `KWH`) with `IsValid` and per-kind `DefaultFor` (C62 for Product, HUR for Service) — codes only, no label table.
- Generated the `_Catalog` migration (chained after `_Crm` in the model snapshot) and hand-wrote `ENABLE`+`FORCE ROW LEVEL SECURITY` + a `tenant_isolation` policy for `catalog_items`, plus the partial-unique `ux_catalog_items_tenant_item_number` index; `Down()` fully reverses.
- Wired `Numera.Modules.Catalog` into `Numera.Api` and both test projects and added it to `Numera.sln` (so CI builds it — the Phase-1 silently-dropped-project gap).
- Proved isolation: `CatalogRlsTests` asserts cross-tenant read=0, `WITH CHECK` insert rejection, fail-closed reads without a tenant, per-tenant article-number uniqueness (same number reusable across tenants and after archival), and `NotArchived`/`Tenant` named-filter composition — all green on real postgres:18.

## Task Commits
1. **Task 1: Catalog project, CatalogItem, unit codes** — `86f7c09` (feat)
2. **Task 2: _Catalog migration + per-table RLS + partial-unique article number** — `9c8139a` (feat)
3. **Task 3: RLS integration gate + unit-code unit tests** — `a51225c` (test)

## Deviations from Plan

None — plan executed exactly as written. The owned-type-safe snake-caser hardened in 02-02 meant no `NumeraDbContext` edit was needed, and per the plan the prices are plain `decimal [Precision(19,4)]` columns (not an owned `Money` value object), so no new mapping bugs surfaced. Note: the reflective `(tenant_id, id)` index was NOT emitted because `CatalogItem` already declares a tenant-leading `[Index(TenantId, Name)]` — the `RegisterModuleTenantEntities` dedup logic correctly skips the redundant index, and the RLS predicate on `tenant_id` still hits `ix_catalog_items_tenant_id_name`.

## Verification Results
- `dotnet build Numera.sln` — 0 warnings, 0 errors.
- `dotnet ef migrations has-pending-model-changes` — "No changes have been made to the model since the last migration."
- `dotnet ef migrations list` — `Catalog` listed after `Crm`; model snapshot contains `catalog_items`.
- Migration grep: 1 × `FORCE ROW LEVEL SECURITY` + 1 × `CREATE POLICY tenant_isolation ON catalog_items` + the partial-unique `ux_catalog_items_tenant_item_number` index (all present).
- `dotnet test tests/Numera.IntegrationTests` — 25/25 passed (7 new Catalog RLS/uniqueness/archive assertions + 18 pre-existing, unaffected) on real postgres:18 as `numera_app` (NO BYPASSRLS); the fresh Testcontainers DB applies `_Catalog` chained after `_Crm` with no PendingModelChangesWarning.
- `dotnet test tests/Numera.Platform.Tests` — 43/43 passed (12 new UnitOfMeasure + 31 pre-existing).

## Issues Encountered
- Git Bash resolved SDK 8.0.303; prepended the user-local `/c/Users/Admin/AppData/Local/Microsoft/dotnet` to PATH + set `DOTNET_ROOT`/`DOTNET_MULTILEVEL_LOOKUP=0` (per STATE.md caveat). The `$LOCALAPPDATA` shell variable was empty under Git Bash, so the absolute path was used directly.

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- Catalog schema exists and is tenant-isolated at the DB layer; `NetPrice`/`TaxCategory`/`VatRatePercent`/`UnitCode` are present so Phase-3 invoicing line items and Phase-5 XRechnung BT-130 can consume them without an RLS-touching migration.
- `UnitOfMeasure` is available for the catalog create/update UI (Phase-2 later waves) as the allowed-code source and default provider.
- CATL-01/02 (catalog CRUD + UI) have their data foundation.

## Self-Check: PASSED
