---
phase: 09-v1-feinschliff-compliance
plan: 01
subsystem: api
tags: [dsgvo, gdpr, export, zip, streaming, rls, data-portability, owner-only, audit]

requires:
  - phase: 01-plattform-kern
    provides: RLS tenant isolation, IEntitlementService (DataExport S+), IAuditWriter
  - phase: 08-crm-ausbau-steuerberater-zugang
    provides: RequireOwner policy + read-only write-guard (TaxAdvisor default-deny)
provides:
  - "TenantExportService — streams the whole tenant dataset as a ZipArchive (per-table JSON + real blobs), bounded memory, AsNoTracking, no transaction"
  - "GET /api/export — Owner-only, DataExport-gated, audited (one data.exported row), streamed application/zip"
affects: [09-05, 09-06]

tech-stack:
  added: []
  patterns:
    - "Reflective per-table streaming export: enumerate EF model entity types, stream each data/{table}.json row-by-row via IAsyncEnumerable; a JsonTypeInfo modifier drops every byte[] so blobs live once under files/, never base64-inlined"
    - "Owner-only + DataExport gate; TaxAdvisor blocked by NOT being in the read allow-list"

key-files:
  created:
    - src/Numera.Api/Services/TenantExportService.cs
    - src/Numera.Api/Endpoints/ExportEndpoints.cs
    - tests/Numera.IntegrationTests/TenantExportTests.cs
  modified:
    - src/Numera.Api/Program.cs

key-decisions:
  - "Sync streamed JSON+blobs only — no migration, no Hangfire job, no CSV (locked 09-CONTEXT)"
  - "Tables enumerated reflectively from the EF model so a future table is exported automatically; RLS on the request connection scopes every query (no manual WHERE tenant_id)"
  - "The Owner-only + TaxAdvisor-deny enforcement is HTTP middleware (RequireOwner policy + 08-01 read-only write-guard) proven in phase 8; 09-01 tests cover the service-level guarantees + the DataExport gate predicate directly (no app-level HTTP harness exists in the suite)"

patterns-established:
  - "byte[]-dropping JSON modifier for blob-bearing entities in exports"

duration: ~50min
completed: 2026-08-02
---

# Phase 09-01: DSGVO tenant-data export

**An Owner streams their COMPLETE tenant dataset as a ZIP (manifest + per-table JSON + byte-exact blobs) from a single DataExport-gated, audited, non-mutating GET /api/export.**

## Performance

- **Duration:** ~50 min (service landed pre-cutoff; endpoint + wiring + tests + verify by orchestrator)
- **Tasks:** 3
- **Files created:** 3 | **modified:** 1

## Accomplishments
- `TenantExportService.WriteArchiveAsync` streams a `ZipArchive` entry-by-entry onto the caller's stream: `manifest.json` (schema version, tenant, per-table row counts), a DSGVO-Art.20 `README.txt` (honest "GoBD-/DSGVO-konform" wording, no certification claim), one `data/{table}.json` per RLS-scoped table (reflectively enumerated, `AsNoTracking`, `IAsyncEnumerable` streaming, no write transaction), and the real binary blobs under `files/` (renders, e-invoice XML, inbound originals, customer files, logo). A `JsonTypeInfo` modifier drops every `byte[]` property so bytes are stored exactly once under `files/`.
- `GET /api/export` — `RequireOwner` group, `Capability.DataExport` (S+) gate via a testable `HasCapabilityAsync` seam → 403 upgrade, appends one `data.exported` audit row (saved before streaming), then streams `application/zip` with an attachment filename. `Program.cs` wires the endpoint + scoped service.
- `TenantExportTests` (real postgres:18), 4/4: completeness (JSON-per-table + byte-exact blobs, blob bytes NOT duplicated in JSON), cross-tenant absence (partner GUID + tagged blob bytes never leak — RLS proof, criterion-4 evidence), service non-mutation (row counts unchanged incl. audit), DataExport gate 403.

## Task Commits

1. **Task 1: TenantExportService** — `f09f763` (feat; service authored pre-cutoff, committed with the plan)
2. **Task 2: GET /api/export + Program wiring** — `f09f763` (feat)
3. **Task 3: TenantExportTests** — `f09f763` (feat/test)

_(Wave 1 was interrupted by a usage limit; the service file existed uncommitted on disk, so all three tasks landed in one completing commit.)_

## Files Created/Modified
- `TenantExportService.cs` — the streaming ZIP export
- `ExportEndpoints.cs` — Owner-only, DataExport-gated, audited endpoint + gate seam
- `Program.cs` — `AddScoped<TenantExportService>()` + `app.MapExportEndpoints()`
- `TenantExportTests.cs` — the four proofs

## Decisions Made
See key-decisions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Test correctness] Cross-tenant assertion used a non-unique discriminator**
- **Found during:** Task 3
- **Issue:** The test asserted tenant A's export excludes tenant B's document number, but per-tenant numbering means BOTH tenants have `RE-2026-00001` — a false positive. 
- **Fix:** Assert on the tenant-unique partner GUID (+ tagged blob bytes) instead; dropped the document-number check.
- **Verification:** 4/4 green.
- **Committed in:** `f09f763`

**2. [Rule 1 - Testability] DataExport gate extracted to a static predicate**
- **Found during:** Task 3
- **Issue:** No HTTP harness exists to reach the endpoint's inline gate.
- **Fix:** `internal static HasCapabilityAsync` + `internal UpgradeRequired()` (matches 09-02 / RecurringInvoiceEndpoints), unit-tested directly.
- **Committed in:** `f09f763`

---

**Total deviations:** 2 auto-fixed (both test-correctness/testability). No scope creep.

## Issues Encountered
The executing agent hit the weekly usage limit after authoring the service; the orchestrator wrote the endpoint, wiring, and tests, then verified on real Postgres. The perennially-untracked generated `src/Numera.Api/Properties/launchSettings` was committed alongside to clear the working tree.

## Coverage boundary
The Owner-only (RequireOwner) and TaxAdvisor-deny paths are HTTP middleware proven in 08-01; they are not re-exercised here because the suite has no WebApplicationFactory. The service-level completeness/isolation/non-mutation and the DataExport gate ARE proven.

## Next Phase Readiness
- 09-05 can wire the Owner "Daten exportieren" download button to this endpoint. 09-06 will do the human-verify ZIP download.

---
*Phase: 09-v1-feinschliff-compliance*
*Completed: 2026-08-02*
