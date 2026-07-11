---
phase: 02-stammdaten
plan: 02
subsystem: crm
tags: [crm, business-partner, rls, ef-core-10, owned-types, vat-id, multi-tenancy, migration]

# Dependency graph
requires:
  - phase: 02-stammdaten
    plan: 01
    provides: Named "Tenant" + "NotArchived" query filters + IArchivable marker
  - phase: 01-plattform-kern
    provides: NumeraDbContext reflective ITenantEntity discovery + RLS + Testcontainers PostgresFixture
provides:
  - Numera.Modules.Crm module (BusinessPartner + owned Address + partner_contacts/notes/activities)
  - Offline USt-IdNr validator (VatId.IsValidDe / IsPlausibleEu / Normalize)
  - _Crm migration with per-table RLS (ENABLE+FORCE+tenant_isolation) for all 4 CRM tables
  - Partial-unique customer/supplier number indexes (per tenant, non-archived)
  - StammdatenRlsTests — per-table cross-tenant RLS CI hard gate
  - Owned-type-safe snake-case naming in NumeraDbContext (navigation-prefixed columns)
affects: [02-stammdaten later waves, CRM-01/02/03, Phase-3 invoicing (EN 16931 buyer), Phase-5 XRechnung, Phase-6 payments/DATEV]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "EF owned value types (Address) embedded twice in one row (billing + shipping) with navigation-prefixed snake_case columns"
    - "Per-table hand-written RLS SQL in the shared migration (reflective discovery never creates policies)"
    - "Partial unique indexes for per-tenant business numbers scoped to non-archived rows"
    - "Offline pure-function VAT-ID validation (ISO 7064 MOD 11,10), never blocking on VIES"

key-files:
  created:
    - src/modules/Numera.Modules.Crm/Numera.Modules.Crm.csproj
    - src/modules/Numera.Modules.Crm/BusinessPartner.cs
    - src/modules/Numera.Modules.Crm/Address.cs
    - src/modules/Numera.Modules.Crm/PartnerContact.cs
    - src/modules/Numera.Modules.Crm/PartnerNote.cs
    - src/modules/Numera.Modules.Crm/PartnerActivity.cs
    - src/modules/Numera.Modules.Crm/PartnerEnums.cs
    - src/modules/Numera.Modules.Crm/VatId.cs
    - src/platform/Numera.Platform.Db/Migrations/20260711022645_Crm.cs
    - tests/Numera.IntegrationTests/StammdatenRlsTests.cs
    - tests/Numera.Platform.Tests/Crm/VatIdTests.cs
  modified:
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql
    - src/Numera.Api/Numera.Api.csproj
    - tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj
    - Numera.sln

key-decisions:
  - "ONE BusinessPartner entity with IsCustomer/IsSupplier flags (RESEARCH.md Q1), not separate Customer/Supplier entities"
  - "Addresses as EF owned value types embedded in the partners row (no separate table/RLS/join); shipping optional/nullable"
  - "RLS policies hand-written per CRM table via migrationBuilder.Sql — reflective entity discovery never emits them (the #1 silent-leak trap)"
  - "Customer/supplier numbers unique per tenant only among non-archived rows (partial unique index), so archived numbers are reusable"
  - "VAT-ID validation is offline-only (DE checksum + EU shape); VIES never gates a save"

patterns-established:
  - "Reflective snake-case naming is now owned-type-safe: PK properties of owned types keep the shared owner column, non-key columns are navigation-prefixed before snake-casing"

# Metrics
duration: 50min
completed: 2026-07-11
---

# Phase 2 Plan 02: CRM Data Layer (BusinessPartner + RLS + VAT-ID) Summary

**Numera.Modules.Crm: one `BusinessPartner` (role flags, owned billing/shipping Address, EN 16931 buyer + B2G/bank/DATEV seams) with contacts/notes/activities, an offline USt-IdNr validator, and a `_Crm` migration whose 4 tables are each RLS-isolated on real Postgres.**

## Performance
- **Duration:** ~50 min (includes diagnosing two owned-type EF/naming bugs)
- **Tasks:** 3
- **Files:** 18 (11 created, 7 modified)

## Accomplishments
- Created the `Numera.Modules.Crm` project: `BusinessPartner` (`ITenantEntity` + `IArchivable`) with role flags, owned `Address` (billing required, shipping optional), EN 16931 buyer fields (BT-44..BT-57), and cheap nullable B2G/bank/DATEV seams (`Iban`, `LeitwegId`, `DebtorAccount`, …).
- Added child entities `PartnerContact`, `PartnerNote`, `PartnerActivity` (all `ITenantEntity`, self-describing via data annotations so `NumeraDbContext` needs no per-entity config).
- Implemented `VatId`: offline `IsValidDe` (regex `^DE[1-9]\d{8}$` + ISO 7064 MOD 11,10 checksum), `IsPlausibleEu` (country-prefix + length), and `Normalize` — a pure function set that never touches VIES.
- Generated the `_Crm` migration and hand-wrote `ENABLE`+`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy for all 4 CRM tables, plus partial-unique customer/supplier number indexes; `Down()` fully reverses.
- Wired `Numera.Modules.Crm` into `Numera.Api` and both test projects, and added it to `Numera.sln` (so CI builds it).
- Proved isolation: `StammdatenRlsTests` asserts cross-tenant read=0 and `WITH CHECK` insert rejection for every CRM table, fail-closed reads without a tenant, and `NotArchived`/`Tenant` named-filter composition — all green on real postgres:18.

## Task Commits
1. **Task 1: CRM project, entities, VAT-ID validator** — `05f3b0a` (feat)
2. **Task 2: _Crm migration + per-table RLS + partial-unique indexes** — `9a7b3f1` (feat)
3. **Task 3: RLS integration gate + VAT-ID unit tests** — `6264f7b` (test)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Owned-type PK collapsed onto a conflicting second primary key**
- **Found during:** Task 1 (first `dotnet ef migrations list` after adding entities).
- **Issue:** The reflective `ApplySnakeCaseNaming` in `NumeraDbContext` renamed the owned `Address` primary-key property away from the owner's shared `id` column, so EF tried to emit a second, conflicting `pk_partners` on `business_partner_id`. Owned types were new to the project (Ledger/Audit have none), so this path had never been exercised.
- **Fix:** Skip snake-casing the PK properties of owned entity types so they keep sharing the owner's (already snake-cased) key column.
- **Files modified:** `src/platform/Numera.Platform.Db/NumeraDbContext.cs`
- **Commit:** `05f3b0a`

**2. [Rule 1 - Bug] Billing and shipping Address collapsed onto the same columns**
- **Found during:** Task 2 (inspecting the first generated migration + snapshot).
- **Issue:** With the PK fix in place, both owned `Address` instances (billing + shipping) had their value columns snake-cased from the same bare names (`street`, `city`, …), so both mapped to identical columns — the migration emitted only ONE unprefixed address set and silently dropped `ShippingAddress` (a data-corruption-class bug: billing and shipping would overwrite each other).
- **Fix:** In `ApplySnakeCaseNaming`, prefix owned-type value columns with the owning navigation name (`BillingAddress`/`ShippingAddress`) before snake-casing, yielding distinct `billing_address_*` / `shipping_address_*` columns (shipping nullable). Removed and regenerated the migration.
- **Files modified:** `src/platform/Numera.Platform.Db/NumeraDbContext.cs`
- **Commit:** `9a7b3f1`

Both fixes make the reflective snake-caser owned-type-safe for all future Phase-2 waves (e.g. catalog) — not just this plan.

## Verification Results
- `dotnet build Numera.sln` — 0 warnings, 0 errors.
- `dotnet ef migrations has-pending-model-changes` — "No changes have been made to the model since the last migration."
- `dotnet test tests/Numera.IntegrationTests` — 18/18 passed (10 new CRM RLS assertions + 8 pre-existing, unaffected) on real postgres:18 as `numera_app` (NO BYPASSRLS).
- `dotnet test tests/Numera.Platform.Tests` — 31/31 passed (15 new VAT-ID + 16 pre-existing).
- Migration applies four `tenant_isolation` policies (ENABLE+FORCE) via the idiomatic per-table loop (same form as `InitialPlatform`); the real proof is the per-table isolation gate rather than literal SQL repetition.

## Issues Encountered
- Git Bash resolved SDK 8.0.303; prepended the user-local `/c/Users/Admin/AppData/Local/Microsoft/dotnet` to PATH + set `DOTNET_ROOT`/`DOTNET_MULTILEVEL_LOOKUP=0` (per STATE.md caveat).
- `dotnet ef migrations remove` refused (it probes the running dev DB and reported the applied `AuditEvents`); removed the flawed first migration by deleting its two files and restoring the snapshot from git, then regenerated.

## User Setup Required
None — no external service configuration required.

## Next Phase Readiness
- CRM schema exists and is tenant-isolated at the DB layer; the EN 16931 buyer fields and B2G/bank/DATEV nullable seams are present so Phase-3 invoicing / Phase-5 XRechnung / Phase-6 DATEV won't need an RLS-touching migration.
- `VatId` is available for the partner create/update UI (Phase-2 later waves) as an offline validator.
- The snake-caser is now owned-type-safe, so subsequent waves can embed owned value objects freely.

## Self-Check: PASSED
