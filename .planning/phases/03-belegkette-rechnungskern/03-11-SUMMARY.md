---
phase: 03-belegkette-rechnungskern
plan: 11
subsystem: api
tags: [ef-core, postgres, finalize, gobd, testcontainers, internals-visible-to, jsonb]

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern (03-05/03-06)
    provides: FinalizeCoreAsync (shared finalize/Storno/Gutschrift core), the §14 gate, NumberingService, VatCalculationService
  - phase: 03-belegkette-rechnungskern (03-08)
    provides: SalesTestData harness + SalesFinalize/Storno/NumberingConcurrency suites on real Postgres
  - phase: 03-belegkette-rechnungskern (03-09)
    provides: DocumentDetailPage §14 issuer/recipient cards (forward-compatible, waiting for the DTO fields)
provides:
  - "FinalizeCoreAsync persists the BG-23 breakdown via db.Add — finalize/Storno/Gutschrift no longer throw DbUpdateConcurrencyException"
  - "SalesDocumentDetail + ToDetail expose IssuerSnapshot/RecipientSnapshot raw jsonb (wire: issuerSnapshot/recipientSnapshot)"
  - "Finalize integration suites now drive the REAL production FinalizeCoreAsync (internal + InternalsVisibleTo + Api ProjectReference) — GAP-1 is regression-guarded"
affects: [phase-04-erechnung, finalize, storno, gutschrift, open-items]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "InternalsVisibleTo to let the integration test project drive an internal production core directly (no HTTP host/auth wiring)"
    - "NoOpAuditWriter test double implementing IAuditWriter to exercise the real core without the ICurrentTenant/ICurrentUser audit seams"

key-files:
  created: []
  modified:
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
    - src/Numera.Api/Contracts/SalesDocumentContracts.cs
    - src/Numera.Api/Numera.Api.csproj
    - tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj
    - tests/Numera.IntegrationTests/SalesTestData.cs

key-decisions:
  - "Direct invocation of the internal FinalizeCoreAsync (via InternalsVisibleTo) chosen over WebApplicationFactory — the GAP-1 bug locus is the core, not the HTTP/§14-gate wrappers"
  - "Snapshots projected as raw jsonb strings (no re-serialization); System.Text.Json camelCase emits issuerSnapshot/recipientSnapshot, matching the already-built frontend parseSnapshot"

patterns-established:
  - "A test that drives the shipped code path (not a parallel copy) genuinely regression-guards it — proven by reverting the fix and watching the suite fail"

# Metrics
duration: 16min
completed: 2026-07-13
---

# Phase 3 Plan 11: Finalize Gap Closure Summary

**One-line db.Add finalize fix + two-field §14 DTO extension, with the finalize integration suites rewired to drive the REAL production FinalizeCoreAsync so GAP 1 is regression-guarded on real Postgres.**

## Performance

- **Duration:** ~16 min
- **Started:** 2026-07-13T11:54Z
- **Completed:** 2026-07-13T12:10Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments
- **GAP 1 fixed:** FinalizeCoreAsync now persists the BG-23 tax breakdown via `db.Add(new SalesDocumentTaxBreakdown{ DocumentId = doc.Id, ... })` instead of the collection-navigation `doc.TaxBreakdown.Add`, which EF marked Modified → 0-row UPDATE → DbUpdateConcurrencyException. Finalize/Storno/Gutschrift-finalize all share this core and are fixed for free.
- **GAP 2 fixed:** SalesDocumentDetail and ToDetail now carry `IssuerSnapshot`/`RecipientSnapshot` (raw jsonb strings), so the already-built §14 issuer/recipient cards render frozen legal data with zero frontend edits.
- **GAP 3 fixed:** SalesTestData.ApplyFinalizeAsync no longer reimplements finalize — it delegates to the real `Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync` (made `internal`, exposed via `[InternalsVisibleTo("Numera.IntegrationTests")]` + a Numera.Api ProjectReference). The parallel copy and its misleading docstring are gone.
- **Regression guard proven** end-to-end (see below) and full integration suite green (59/59) against real Postgres exercising the shipped core.

## Task Commits

1. **Task 1: Fix finalize crash (GAP 1) + expose §14 snapshots (GAP 2)** - `90fe03a` (fix)
2. **Task 2: Rewire finalize integration tests to the REAL FinalizeCoreAsync (GAP 3)** - `d781d91` (test)
3. **Task 3: Run suite green + prove the regression guard** - verification only, no source change (the temporary revert was undone; final `git diff` empty)

## Files Created/Modified
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` - breakdown persisted via `db.Add`; ToDetail projects `d.IssuerSnapshot`/`d.RecipientSnapshot`; FinalizeCoreAsync made `internal`
- `src/Numera.Api/Contracts/SalesDocumentContracts.cs` - SalesDocumentDetail record gains `string? IssuerSnapshot` + `string? RecipientSnapshot`
- `src/Numera.Api/Numera.Api.csproj` - `<InternalsVisibleTo Include="Numera.IntegrationTests" />`
- `tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj` - ProjectReference to Numera.Api
- `tests/Numera.IntegrationTests/SalesTestData.cs` - ApplyFinalizeAsync delegates to the real core via a NoOpAuditWriter; parallel finalize body + BuildOpenItem + Json field removed; docstring corrected

## Regression Guard: Observed Before/After

Proof that the tests now guard the real code path (the whole point of GAP 3), all against real Postgres (Testcontainers postgres:18):

| State of FinalizeCoreAsync breakdown persist | SalesFinalizeTests result |
| --- | --- |
| `db.Add(new SalesDocumentTaxBreakdown …)` (Task-1 fix, shipped) | **8/8 pass** (full suite 59/59 green) |
| Temporarily reverted to `doc.TaxBreakdown.Add(…)` (the GAP-1 pattern) | **6/8 FAIL** with `DbUpdateConcurrencyException: … expected to affect 1 row(s), but actually affected 0 row(s)` — thrown from `SalesDocumentEndpoints.FinalizeCoreAsync … SaveChangesAsync`. The 2 passing cases are the §14-gate rejections that never reach the breakdown persist. |
| Fix restored | **59/59 green** again; final `git diff --stat` empty (revert fully undone, source left in the fixed state) |

The failure surfaces from the REAL `Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync` in the stack trace — confirming the suite drives the shipped core, not a copy.

## Decisions Made
- Direct invocation of the internal `FinalizeCoreAsync` (via `InternalsVisibleTo`) over `WebApplicationFactory` — the bug locus is the core, not the HTTP or §14-gate wrappers, so no host/auth/session wiring is needed.
- `NoOpAuditWriter : IAuditWriter` (RecordAsync → Task.CompletedTask) lets the tests exercise the real core without constructing the audit ICurrentTenant/ICurrentUser seams; the suites assert on breakdown/numbering/open-item/immutability, not audit rows.
- Snapshots projected as raw jsonb strings (no re-serialization); camelCase JSON emits `issuerSnapshot`/`recipientSnapshot`, exactly what the frontend `parseSnapshot` already reads.

## Deviations from Plan

None - plan executed exactly as written. The temporary breakdown-add revert in Task 3 was a deliberate, plan-mandated step to demonstrate the regression guard; it was undone before finishing (final `git diff` empty, source in the `db.Add` state).

## Issues Encountered
- Removing the parallel finalize body left the harness `Json` field and `BuildOpenItem` helper orphaned (would trigger CS0414 / unused-member warnings and break the 0-warning gate). Removed both plus the now-unused `System.Text.Json` using; build stayed 0-warning.

## User Setup Required
None - no external service configuration required. (Two non-blocking human verifications remain from 03-VERIFICATION.md: finalize a draft via the live UI, and visually confirm the §14 cards render frozen data — both now unblocked by this plan.)

## Next Phase Readiness
- The Phase-3 centerpiece (Unveränderbarkeit + Nummernvergabe — "das Herz von v1") is now functional end-to-end and regression-guarded: a finalized document gets a race-safe RE-YYYY-##### number, a persisted BG-23 breakdown per (category,rate), an open item with a due date, and becomes DB-immutable.
- All three confirmed 03-VERIFICATION.md gaps are closed. Phase 3 is ready for re-verification and for Phase 4 (E-Rechnung), which consumes finalized invoices.

## Self-Check: PASSED

- Commit `90fe03a` (Task 1): FOUND
- Commit `d781d91` (Task 2): FOUND
- `.planning/phases/03-belegkette-rechnungskern/03-11-SUMMARY.md`: FOUND
- Source left in fixed state: `SalesDocumentEndpoints.cs:746` contains `db.Add(new SalesDocumentTaxBreakdown` (navigation `.Add` gone)

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*
