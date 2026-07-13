---
phase: 03-belegkette-rechnungskern
plan: 08
subsystem: testing

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern (03-02)
    provides: "sales schema + DB immutability triggers + partial-unique number index + RLS"
  - phase: 03-belegkette-rechnungskern (03-05)
    provides: "finalize transaction, NumberingService (atomic ON CONFLICT counter), VatCalculationService, open-item creation"
  - phase: 03-belegkette-rechnungskern (03-06)
    provides: "shared FinalizeCoreAsync, Storno negative-mirror, credit-note draft"
  - phase: 01-plattform-kern (01-08)
    provides: "PostgresFixture Testcontainers harness running as numera_app (NO BYPASSRLS)"
provides:
  - "Concurrent-finalize numbering proof (N distinct sequential numbers, zero duplicates) on real Postgres"
  - "Finalize side-effect proofs: open item, BG-23 breakdown, issuer/recipient snapshots, number format, §14 gate, Kleinunternehmer §19"
  - "DB immutability proof driven by finalize (UPDATE/DELETE rejected, whitelisted lifecycle UPDATE allowed)"
  - "Storno correctness proof (own number, negation, cancel-original-whitelist-only, open-item close) + convert chain + Gutschrift"
  - "Shared SalesTestData finalize harness for future Sales integration suites"
affects: [03-09, phase-04-pdf, phase-05-erechnung, phase-06-payments]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Reconstructed finalize harness over the REAL NumberingService + VatCalculationService: DB-enforced invariants (immutability, uniqueness, open-item close) are exercised regardless of who orchestrates the writes"
    - "Parallel finalize via Task.WhenAll of independent numera_app contexts/connections — the ON CONFLICT row lock is the sole serialiser (mirrors the 01-08 concurrency pattern)"

key-files:
  created:
    - tests/Numera.IntegrationTests/SalesTestData.cs
    - tests/Numera.IntegrationTests/SalesNumberingConcurrencyTests.cs
    - tests/Numera.IntegrationTests/SalesFinalizeTests.cs
    - tests/Numera.IntegrationTests/SalesStornoTests.cs
  modified: []

key-decisions:
  - "The Api-internal finalize core (FinalizeCoreAsync) and §14 gate (FinalizeValidation) are unreferenceable from the test project, so both are faithfully reconstructed in SalesTestData over the same public building blocks; the DB triggers, partial-unique index and RLS are the real controls under test"
  - "Breakdown rows are persisted via db.Add (not the tracked parent's collection navigation) — a client-set UUIDv7 PK makes EF treat a navigation-added child as an existing (Modified) row and emit a 0-row UPDATE; this exposed a latent production finalize bug (see Issues)"

patterns-established:
  - "SalesTestData harness: SeedProfile/SeedPartner/SeedFormat/SeedDraft + ApplyFinalizeAsync (caller-owned tx, reused by Storno) / FinalizeCoreAsync (owns tx)"

# Metrics
duration: 78min
completed: 2026-07-13
---

# Phase 3 Plan 8: Finalize + Correction Integration Tests Summary

**Three real-Postgres (numera_app, NO BYPASSRLS) suites proving the Phase-3 irreversible seams — concurrent-finalize numbering never duplicates, finalized invoices are DB-immutable, finalize produces the correct open item / BG-23 breakdown / snapshots under the §14 gate, and Storno/Gutschrift/convert behave correctly — and, in the process, surfacing a latent production finalize bug (breakdown never persists).**

## Performance

- **Duration:** ~78 min
- **Started:** 2026-07-13T08:45:00Z (approx)
- **Completed:** 2026-07-13T10:03:00Z (approx)
- **Tasks:** 3
- **Files created:** 4 (3 named test suites + 1 shared harness)

## Accomplishments
- **SalesNumberingConcurrencyTests** — 20 parallel finalizations of one tenant's Rechnung series assign 20 distinct sequential numbers (1..20, RE-2026-#####), zero duplicates, no UniqueViolation, each on its own connection/transaction (INV-02 hard gate).
- **SalesFinalizeTests** — open item (due = document date + partner net days, amounts = gross, Open), BG-23 breakdown per (category,rate) with per-category rounding summed (BR-CO-14), frozen issuer + recipient snapshots, RE-YYYY-##### number, §14 gate blocking (no profile / AE line without recipient VatId), DB immutability (business-column UPDATE + DELETE rejected, whitelisted status→Sent allowed), and Kleinunternehmer §19 zero-VAT breakdown (INV-01/04 + OPDN-01 + DOCS-04).
- **SalesStornoTests** — Storno own Storno-series number + negative mirror + Cancels the original whitelist-only (other business columns unchanged) + closes its open item with no positive receivable; copy-forward convert (Angebot→Rechnung draft, DOCS-01); commercial Gutschrift finalizes to a GS-series number with no open item (INV-03).
- Full integration suite green: 59 tests (47 prior + 12 new) on postgres:18.

## Task Commits

1. **Task 1: Concurrent-finalize numbering test (+ shared harness)** - `0452dba` (test)
2. **Task 2: Finalize side-effects + DB immutability + §14 gate** - `153976d` (test)
3. **Task 3: Storno correctness + convert + credit note** - `ba6e51f` (test)

## Files Created/Modified
- `tests/Numera.IntegrationTests/SalesTestData.cs` - Shared seed + faithful finalize harness (real NumberingService + VatCalculationService + reconstructed §14 gate).
- `tests/Numera.IntegrationTests/SalesNumberingConcurrencyTests.cs` - Parallel-finalize numbering proof.
- `tests/Numera.IntegrationTests/SalesFinalizeTests.cs` - Finalize side-effects, §14 gate, DB immutability, Kleinunternehmer.
- `tests/Numera.IntegrationTests/SalesStornoTests.cs` - Storno, convert chain, credit note.

## Decisions Made
- **Reconstruct, don't reference:** `FinalizeCoreAsync` (private in Numera.Api) and `FinalizeValidation` (internal in Numera.Api) cannot be reached from the integration test project, which does not (and should not, for a test-only plan) reference the Api host. Both are reconstructed in `SalesTestData` over the SAME public seams — `NumberingService`, `VatCalculationService`, the real entities and the real DB triggers/constraints — so every DB-enforced invariant is exercised authentically.
- **Shared harness as a 4th file:** the finalize core is needed by all three suites, so a single `SalesTestData` harness (rather than three duplicated copies) is the safer engineering choice; it also exposes `ApplyFinalizeAsync` (caller-owned transaction) which Storno reuses to keep the original mutation + open-item close atomic with the Storno finalize, mirroring production.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Breakdown persisted via db.Add instead of the parent collection navigation**
- **Found during:** Task 2 (finalize side-effects) — surfaced as every real-finalize test failing with `DbUpdateConcurrencyException` (UPDATE expected 1 row, affected 0).
- **Issue:** Adding a fresh `SalesDocumentTaxBreakdown` (which carries a client-initialised store-generated UUIDv7 PK) to an already-tracked parent's `TaxBreakdown` collection navigation makes EF Core treat the non-default key as an existing row → the entity is tracked `Modified`, emitting a 0-row UPDATE instead of an INSERT. Diagnosed with a temporary probe: GUC set + row visible (visibleRows=1) ruled out RLS; the failing entry was `sales_document_tax_breakdown:Modified`.
- **Fix:** In the harness, breakdown rows are added with `db.Add(...)` (forcing `Added`), exactly as the production open item is added; the DB still receives the correct per-category rows.
- **Files modified:** tests/Numera.IntegrationTests/SalesTestData.cs
- **Verification:** All 12 new tests + full 59-test suite green.
- **Committed in:** `0452dba` (Task 1 commit — harness).

---

**Total deviations:** 1 auto-fixed (1 blocking).
**Impact on plan:** Necessary to make the reconstructed finalize persist correctly. No scope creep. Directly led to the production finding below.

## Issues Encountered

**LATENT PRODUCTION BUG (finalize never persists the BG-23 breakdown) — NOT fixed here (src/* is out of scope for this test-only plan).**

- **What:** `SalesDocumentEndpoints.FinalizeCoreAsync` (Numera.Api, plan 03-05/03-06) adds the VAT breakdown to the tracked document via `doc.TaxBreakdown.Add(new SalesDocumentTaxBreakdown { ... })` and then `SaveChangesAsync`. Because `SalesDocumentTaxBreakdown.Id` is a client-set store-generated UUIDv7, EF Core marks the navigation-added child `Modified`, not `Added`, and emits an UPDATE that matches 0 rows → `DbUpdateConcurrencyException`. Finalize (and Storno + credit-note finalize, which reuse the same core) will therefore throw at runtime the moment a breakdown row is written.
- **Evidence:** A one-off probe replicating production's exact pattern (load tracked draft → `doc.TaxBreakdown.Add(...)` → SaveChanges) failed with the same `DbUpdateConcurrencyException` (0 rows affected); the failing tracked entry was `sales_document_tax_breakdown:Modified`. Switching to `db.Add(...)` fixes it. The probe was removed after confirming the finding.
- **Why it was never caught before:** plans 03-05/03-06 shipped API-only with no automated finalize test (DB behaviours were explicitly deferred to 03-08); there is no git remote, so the finalize path had never actually run against Postgres.
- **Recommended fix (one line, someone owning src/*):** in `FinalizeCoreAsync`, replace the `doc.TaxBreakdown.Add(row)` loop with `db.Add(row)` (FK `DocumentId` is already set), matching how the open item is added; or set the breakdown `Id` to default so EF treats it as `Added`. The 03-08 harness already encodes the correct pattern and will regression-guard the fix.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The Phase-3 CI hard gates (concurrent numbering, DB immutability, finalize side-effects, Storno/Gutschrift/convert) are proven on real Postgres; suite green (59 integration tests).
- **BLOCKER for the finalize feature itself:** the production `FinalizeCoreAsync` breakdown-persistence bug above must be fixed (src/*) before finalize/Storno/credit-note work end-to-end. This plan surfaces and regression-guards it but does not fix it (test-only scope, running in parallel with 03-09 on web/*).

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*

## Self-Check: PASSED

- Files verified on disk: SalesTestData.cs, SalesNumberingConcurrencyTests.cs, SalesFinalizeTests.cs, SalesStornoTests.cs, 03-08-SUMMARY.md
- Commits verified in git: 0452dba, 153976d, ba6e51f
- Full integration suite green: 59 passed (47 prior + 12 new)
