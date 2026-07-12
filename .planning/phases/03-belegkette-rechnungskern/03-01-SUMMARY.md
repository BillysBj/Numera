---
phase: 03-belegkette-rechnungskern
plan: 01
subsystem: api
tags: [dotnet, ef-core, postgres, rls, fluentvalidation, company-profile, en16931, ustg]

# Dependency graph
requires:
  - phase: 01-plattform-kern
    provides: "NumeraDbContext reflective ITenantEntity discovery + snake-case naming, TenantConnectionInterceptor (app.current_tenant GUC), IAuditWriter/IAuditEvent, ICurrentTenant, hand-written RLS migration pattern, PostgresFixture (numera_app, NO BYPASSRLS)"
  - phase: 02-stammdaten
    provides: "Owned-Address entity pattern, mutate->audit->single-SaveChanges endpoint idiom, AddValidatorsFromAssemblyContaining<Program>() auto-registration, shared AddressDto contract, per-table RLS test pattern"
provides:
  - "Numera.Modules.Sales project (wired into sln + Api + both test projects)"
  - "CompanyProfile ITenantEntity: §14 UStG issuer master data (legal name, owned Address, VatId/TaxNumber, §19 Kleinunternehmer flag, bank/registry/contact seams)"
  - "_CompanyProfile migration #1 of Phase 3 with ENABLE+FORCE+tenant_isolation RLS and unique tenant_id index"
  - "GET + PUT /api/company-profile (upsert) with §14 validation (legal name + billing address + exactly-one tax id)"
  - "CompanyProfileRlsTests: cross-tenant isolation hard gate on real Postgres"
affects: [03-05-finalize, invoice-issuer-snapshot, e-rechnung, pdf-rendering]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Per-module local [Owned] value type (Sales.Address) duplicated rather than shared across modules — module isolation (RESEARCH.md Q4)"
    - "Single-row-per-tenant entity via [Index(nameof(TenantId), IsUnique = true)] which doubles as the tenant-leading access index (reflective discovery adds no second)"
    - "PUT upsert (create-on-first-write) for a settings singleton via mutate->audit->single-SaveChanges"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj
    - src/modules/Numera.Modules.Sales/CompanyProfile.cs
    - src/modules/Numera.Modules.Sales/Address.cs
    - src/platform/Numera.Platform.Db/Migrations/20260712145717_CompanyProfile.cs
    - src/Numera.Api/Contracts/CompanyProfileContracts.cs
    - src/Numera.Api/Validators/CompanyProfileValidators.cs
    - src/Numera.Api/Endpoints/CompanyProfileEndpoints.cs
    - tests/Numera.IntegrationTests/CompanyProfileRlsTests.cs
  modified:
    - src/Numera.Api/Numera.Api.csproj
    - tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj
    - Numera.sln
    - src/Numera.Api/Program.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql

key-decisions:
  - "Sales.Address is a LOCAL owned value type, deliberately duplicated from Crm.Address rather than shared — modules must not reference each other (RESEARCH.md Q4)"
  - "A tenant has exactly one company_profile, enforced by a UNIQUE index on tenant_id which is also the tenant-leading access index"
  - "PUT /api/company-profile is an upsert (creates on first write) so the settings form gets a create+edit surface from one verb; GET returns an empty editable shell (all nulls) when no profile exists"
  - "§14 tax identity requires EXACTLY ONE of VatId/TaxNumber (XOR); DE USt-IdNr shape-checked inline (DE + 9 digits), full ISO 7064 checksum NOT reused from Crm to keep Sales module-isolated"
  - "Reused the existing shared AddressDto (PartnerContracts) instead of defining a duplicate — same BG-5 shape, same namespace"

patterns-established:
  - "Settings singleton: unique-tenant-index entity + GET(empty-shell)/PUT(upsert) endpoint pair"
  - "Migration #1 of a phase lands alone in its wave so the snapshot chains cleanly"

# Metrics
duration: 9min
completed: 2026-07-12
---

# Phase 3 Plan 01: CompanyProfile (Issuer Master Data) Summary

**§14 UStG issuer master data as a tenant-isolated `company_profile` singleton (legal name, owned Address, USt-IdNr/Steuernummer, §19 flag, bank/registry seams) with a new Numera.Modules.Sales project, hand-written RLS migration, and a GET/PUT-upsert settings API validating exactly-one-tax-id.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-07-12T14:53:47Z
- **Completed:** 2026-07-12T15:02:56Z
- **Tasks:** 3
- **Files modified:** 15 (8 created, 7 modified)

## Accomplishments
- New `Numera.Modules.Sales` project created and wired once into the solution, the Api host, and both test projects (design-time entity discovery + CI sln coverage)
- `CompanyProfile` ITenantEntity carrying the §14 issuer data the lean `tenants` table lacks — the issuer source plan 03-05 will snapshot onto finalized invoices
- `_CompanyProfile` migration (Phase 3 migration #1) with hand-written ENABLE+FORCE+`tenant_isolation` RLS and a unique `tenant_id` index; clean snapshot, no PendingModelChangesWarning
- `GET` + `PUT` (upsert) `/api/company-profile` with §14 validation (legal name + billing address + exactly-one tax id) and an audited mutate→audit→single-SaveChanges write
- `CompanyProfileRlsTests` proves cross-tenant isolation on real postgres:18 as `numera_app` (3 tests green); full suite unbroken (28 integration + 43 platform)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create Numera.Modules.Sales + CompanyProfile entity + owned Address; wire references** - `c9876df` (feat)
2. **Task 2: Generate _CompanyProfile migration with RLS + unique tenant index** - `8090fe3` (feat)
3. **Task 3: /api/company-profile GET + PUT (upsert) + validators + RLS test** - `2428a0f` (feat)

**Plan metadata:** _(this docs commit)_

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj` - New Sales module (net10.0, Db + Money refs)
- `src/modules/Numera.Modules.Sales/CompanyProfile.cs` - §14 issuer ITenantEntity, unique tenant_id index, owned Address
- `src/modules/Numera.Modules.Sales/Address.cs` - Local [Owned] BG-5 address value type (no cross-module dep)
- `src/platform/Numera.Platform.Db/Migrations/20260712145717_CompanyProfile.cs` - Table + unique index + hand-written RLS (Up/Down)
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs` - Snapshot now includes company_profile
- `src/platform/Numera.Platform.Db/Sql/rls_policies.sql` - company_profile RLS block added to the master copy
- `src/Numera.Api/Contracts/CompanyProfileContracts.cs` - CompanyProfileDto + UpdateCompanyProfileRequest (reuses shared AddressDto)
- `src/Numera.Api/Validators/CompanyProfileValidators.cs` - §14 rules (legal name, address, XOR tax id, DE USt-IdNr shape)
- `src/Numera.Api/Endpoints/CompanyProfileEndpoints.cs` - GET (empty-shell) + PUT (upsert) + audit event
- `src/Numera.Api/Program.cs` - Registered app.MapCompanyProfileEndpoints()
- `tests/Numera.IntegrationTests/CompanyProfileRlsTests.cs` - Cross-tenant RLS proof (3 tests)
- `src/Numera.Api/Numera.Api.csproj`, both test `.csproj`, `Numera.sln` - Sales project references wired

## Decisions Made
- **Local owned Address in Sales** (not shared from Crm): modules must not reference each other (RESEARCH.md Q4). Duplicated a small value type over a cross-module dependency.
- **Exactly-one company_profile per tenant**: unique index on `tenant_id`, which also serves as the tenant-leading access index (so reflective discovery adds no second index).
- **PUT is an upsert**: creates on first write, updates thereafter — one verb serves the settings form's create+edit; GET returns an all-nulls editable shell when no profile exists.
- **§14 XOR tax id**: exactly one of VatId/TaxNumber via a FluentValidation `Must`; DE USt-IdNr shape-checked inline (`^DE[1-9]\d{8}$`), full ISO 7064 checksum NOT reused from Crm to keep Sales module-isolated.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Duplicate `AddressDto` type collision**
- **Found during:** Task 3 (Contracts)
- **Issue:** The plan specified a nested `AddressDto` on the company-profile contracts, but `AddressDto` already exists in `PartnerContracts.cs` in the same `Numera.Api.Contracts` namespace with an identical shape → CS0101 duplicate-definition build error.
- **Fix:** Removed the duplicate declaration and reused the existing shared `AddressDto` (identical BG-5 fields, same namespace); documented the reuse in a remark.
- **Files modified:** src/Numera.Api/Contracts/CompanyProfileContracts.cs
- **Verification:** `dotnet build Numera.sln` green (0 errors).
- **Committed in:** 2428a0f (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The reuse is strictly better (no duplicate type, one shared contract). No scope creep.

## Issues Encountered
None - planned work proceeded as written apart from the single deviation above.

## User Setup Required
None - no external service configuration required. (Docker postgres:18 + Keycloak stack already running from prior phases; tests use Testcontainers.)

## Next Phase Readiness
- The §14 issuer data source exists, is RLS-isolated, and is editable via `/api/company-profile` — ready for plan 03-05 (finalize) to snapshot issuer identity onto documents (INV-01).
- `Numera.Modules.Sales` is now the home for the invoice/document entities the rest of Phase 3 adds; its project references are wired once.
- Migration #1 of Phase 3 landed alone in its wave, so subsequent Phase-3 migrations chain cleanly through the snapshot.

## Self-Check: PASSED

All 8 created source/test files + SUMMARY.md exist on disk; all 3 task commits (c9876df, 8090fe3, 2428a0f) present in history. Build green (0 errors), full test suite green (28 integration + 43 platform), migration RLS markers verified, no PendingModelChangesWarning.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-12*
