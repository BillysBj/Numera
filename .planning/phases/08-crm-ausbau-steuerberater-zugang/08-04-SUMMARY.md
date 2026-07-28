---
phase: 08-crm-ausbau-steuerberater-zugang
plan: 04
subsystem: crm
tags: [dotnet, react, postgres, bytea, rls, append-only, kundenakte]

requires:
  - phase: 08-01
    provides: TaxAdvisor write guard and /api/partners read denial
  - phase: 08-03
    provides: PartnerTasks migration baseline and Aufgaben partner-detail section
provides:
  - Append-only, tenant-isolated customer_files bytea storage
  - Authenticated upload, metadata list, and byte-identical download endpoints
  - Kundenakte file UI with client validation and no delete/edit affordance
affects: [crm, partner-detail, database, compliance]

tech-stack:
  added: []
  patterns: [append-only trigger plus privilege restriction, metadata-only bytea listing]

key-files:
  created:
    - src/modules/Numera.Modules.Crm/CustomerFile.cs
    - src/Numera.Api/Endpoints/CustomerFileEndpoints.cs
    - tests/Numera.IntegrationTests/CustomerFileTests.cs
    - web/src/features/partners/files.ts
  modified:
    - src/Numera.Api/Program.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql
    - web/src/features/partners/PartnerDetailPage.tsx
    - web/src/i18n/index.ts

key-decisions:
  - "Customer file content remains in PostgreSQL bytea with a 20 MB limit and the locked MIME allow-list."
  - "Customer files expose only create/list/download; database privileges and a trigger independently block update/delete."

patterns-established:
  - "Kundenakte list queries project metadata before materialization and never select the bytea column."
  - "Immutable partner children use client-created UUIDv7 keys and are persisted with db.Add."

duration: 20min
completed: 2026-07-28
---

# Phase 8 Plan 04: Append-only Kundenakte Files Summary

**Tenant-isolated PostgreSQL bytea attachments with audited upload, metadata-only listing, download, and database-enforced append-only behavior**

## Performance

- **Duration:** 20 min
- **Completed:** 2026-07-28
- **Tasks:** 3
- **Files modified:** 14 (including generated migration artifacts and this summary)

## Accomplishments

- Added `CustomerFile` and a migration chained after `20260728202315_PartnerTasks`, including RLS, runtime-role privilege restrictions, and an UPDATE/DELETE rejection trigger.
- Added authenticated, audited multipart upload plus metadata-only list and byte-preserving download routes under `/api/partners/{partnerId}/files`.
- Added the German-authoritative Kundenakte UI after Aufgaben, with MIME/20 MB client validation, localized status/error text, download links, and no edit/delete control.
- Proved byte round trips, projection behavior, RLS isolation/WITH CHECK, and update/delete trigger enforcement on PostgreSQL 18.

## Files Created/Modified

- `src/modules/Numera.Modules.Crm/CustomerFile.cs` - Append-only tenant entity and bytea payload.
- `src/platform/Numera.Platform.Db/Migrations/20260728203515_CustomerFiles.cs` - Table, RLS, grants/revokes, and immutable trigger.
- `src/platform/Numera.Platform.Db/Migrations/20260728203515_CustomerFiles.Designer.cs` - Generated migration model.
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs` - Generated latest EF model.
- `src/platform/Numera.Platform.Db/Sql/rls_policies.sql` - Canonical customer_files RLS and immutability documentation.
- `src/Numera.Api/Endpoints/CustomerFileEndpoints.cs` - Upload/list/download endpoint group.
- `src/Numera.Api/Program.cs` - Endpoint registration.
- `tests/Numera.IntegrationTests/CustomerFileTests.cs` - PostgreSQL storage, projection, RLS, and trigger tests.
- `web/src/features/partners/files.ts` - Typed fetch and TanStack Query hooks.
- `web/src/features/partners/PartnerDetailPage.tsx` - Kundenakte upload/list/download section.
- `web/src/i18n/locales/de/files.json` - Authoritative German strings.
- `web/src/i18n/locales/en/files.json` - English strings.
- `web/src/i18n/index.ts` - Additive files namespace registration.

## Decisions Made

None beyond the locked plan. Upload auditing records metadata only and never copies file bytes into the audit trail.

## Deviations from Plan

None. Generated Designer and model snapshot files are the required EF migration artifacts.

## Issues Encountered

The locally installed EF CLI is 10.0.3 while the runtime is 10.0.10; scaffolding emitted an informational version notice but completed successfully, and the solution compiled with zero warnings.

## Verification

- `dotnet build Numera.sln -c Debug`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter CustomerFile --no-build`: 4/4 passed.
- `dotnet test tests/Numera.IntegrationTests --no-build`: 137/137 passed.
- `npm --prefix web run build`: passed.
- `npm --prefix web run lint`: passed.
- `npm --prefix web run test`: 7 files, 56/56 tests passed.

## User Setup Required

None.

## Next Phase Readiness

CRM-05 is complete with no external object storage or mutable file path. The existing `/api/partners/*` role guard keeps TaxAdvisor access denied.

---
*Phase: 08-crm-ausbau-steuerberater-zugang*
*Completed: 2026-07-28*
