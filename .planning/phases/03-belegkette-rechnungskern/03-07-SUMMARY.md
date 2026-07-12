---
phase: 03-belegkette-rechnungskern
plan: 07
subsystem: ui
tags: [react, rhf, zod, tanstack-table, tanstack-query, i18n, sales-documents, catalog-picker, vitest]

# Dependency graph
requires:
  - phase: 03-04
    provides: "/api/documents draft CRUD + convert + list/detail HTTP surface the client consumes"
  - phase: 02-07
    provides: "catalog frontend UI stack (shared DataTable, RHF+zod, shadcn, namespace-aware i18n) mirrored here"
  - phase: 02-05
    provides: "CATL-02 lookupCatalogItems picker seam snapshotted onto document lines"
provides:
  - "documents BFF client (lib/api/documents.ts): typed /api/documents CRUD + convert with DocumentType/DocumentStatus enum mirrors"
  - "documentSchema.ts: RHF/zod line-array schema + computePreviewTotals (per-category round-then-sum, half-away-from-zero)"
  - "DocumentListPage: server-side paged/filterable (type+status) shared DataTable"
  - "DocumentFormPage: RHF useFieldArray draft editor with catalog picker + live preview totals + read-only non-Draft mode"
  - "documents DE/EN i18n namespace + /documents routes + Belege nav"
affects: [03-09, frontend-document-detail, finalize-ui, storno-ui]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "computePreviewTotals mirrors the server VatCalculationService (bucket by category+effective rate, tax only S, round each bucket 2dp away-from-zero, sum) — a PREVIEW; server recomputes authoritatively on save"
    - "roundAway with magnitude-scaled epsilon so a true midpoint (0.285) rounds up despite binary float error, matching decimal MidpointRounding.AwayFromZero"
    - "Catalog picker snapshots CatalogLineItem onto a fresh line (catalogItemId provenance only) — the CATL-02 seam consumed at last"
    - "Edit mode is read-only when the loaded document is not Draft (notice + link to detail), mirroring the server 409 non-draft guard"
    - "documents.ts re-exports lookupCatalogItems / TaxCategory / extractValidationErrors so the editor imports one module"

key-files:
  created:
    - "web/src/lib/api/documents.ts"
    - "web/src/features/documents/documentSchema.ts"
    - "web/src/features/documents/documentSchema.test.ts"
    - "web/src/features/documents/DocumentListPage.tsx"
    - "web/src/features/documents/DocumentFormPage.tsx"
    - "web/src/i18n/locales/de/documents.json"
    - "web/src/i18n/locales/en/documents.json"
  modified:
    - "web/src/i18n/index.ts"
    - "web/src/App.tsx"

key-decisions:
  - "computePreviewTotals is an explicit client-side ESTIMATE; the server stays authoritative on save (drafts show TotalGross 0 until finalize) — a code comment marks this"
  - "roundAway corrects binary float error with a magnitude-scaled epsilon so client preview matches server AwayFromZero rounding (0.285 -> 0.29)"
  - "partnerId is a plain text field for v1 (no async partner select) per plan scope; documentType/documentDate/notes/buyerReference complete the header"
  - "DocumentType/DocumentStatus cross the wire as NUMBERS (the Catalog/Sales convention); i18n type/status labels keyed by the numeric ordinal"
  - "/documents/:id detail route deliberately NOT added here (03-09 owns it); the list + edit links point at it forward"

# Metrics
duration: 13min
completed: 2026-07-12
---

# Phase 3 Plan 07: Document Frontend (Draft Lifecycle UI) Summary

**Bilingual /documents draft UI: a server-side paged/filterable list plus an RHF+zod useFieldArray editor with a CATL-02 catalog picker that snapshots lines and a live per-category-round-then-sum total preview, over the 03-04 API with ZERO new deps.**

## Performance

- **Duration:** ~13 min
- **Started:** 2026-07-12T23:50:00Z
- **Completed:** 2026-07-12T23:59:40Z
- **Tasks:** 2
- **Files modified:** 9 (7 created, 2 modified)

## Accomplishments
- Typed documents BFF client (`lib/api/documents.ts`) with `DocumentType`/`DocumentStatus` numeric enum mirrors, full CRUD + `convertSalesDocument`, and a re-export of the `lookupCatalogItems` CATL-02 seam.
- `documentSchema.ts` — a zod line-array schema mirroring the server FluentValidation rules plus `computePreviewTotals`, a per-category round-then-sum estimate matching the server's VatCalculationService (only category S taxed, each bucket rounded 2dp away-from-zero, summed).
- 17 vitest cases proving line/document validation and preview totals (single 19%, mixed 19/7 → 22.50, per-category-then-sum 0.40-vs-0.39 divergence, real 19% midpoint 1.50 → 0.285 → 0.29, non-standard categories untaxed).
- `DocumentListPage` — server-side shared `DataTable` with type + status filter selects, document-number search, EUR gross formatting, and a draft-only Edit action.
- `DocumentFormPage` — RHF `useFieldArray` editor with a catalog picker modal that snapshots `CatalogLineItem` onto a line, a live net/tax/gross preview, 400 field-error + 409 non-draft banner mapping, and a read-only mode for non-Draft documents.
- New `documents` DE/EN i18n namespace (DE default) registered in `i18n/index.ts`; `/documents`, `/documents/new`, `/documents/:id/edit` routes + a Belege nav link in `App.tsx`.

## Task Commits

Each task was committed atomically:

1. **Task 1: documents BFF client + zod schema + i18n namespace** - `2eff225` (feat)
2. **Task 2: DocumentListPage + DocumentFormPage + routes** - `5aaa8b9` (feat)

**Plan metadata:** (this commit)

## Files Created/Modified
- `web/src/lib/api/documents.ts` - Typed /api/documents client (CRUD + convert) + enum mirrors; re-exports lookupCatalogItems/TaxCategory/extractValidationErrors
- `web/src/features/documents/documentSchema.ts` - zod line-array schema, form defaults/mappers, computePreviewTotals + roundAway
- `web/src/features/documents/documentSchema.test.ts` - 17 vitest cases (validation + preview totals)
- `web/src/features/documents/DocumentListPage.tsx` - server-side paged/filterable document list
- `web/src/features/documents/DocumentFormPage.tsx` - RHF useFieldArray draft editor + catalog picker + preview + read-only mode
- `web/src/i18n/locales/de/documents.json`, `web/src/i18n/locales/en/documents.json` - documents namespace strings
- `web/src/i18n/index.ts` - registered the documents namespace (resources de+en, ns array)
- `web/src/App.tsx` - /documents routes + Belege nav link

## Decisions Made
- `computePreviewTotals` is an explicit client-side ESTIMATE marked in a comment; the server recomputes authoritatively on save (drafts carry TotalGross 0 until finalize).
- `roundAway` corrects binary float error with a magnitude-scaled epsilon so the client preview matches the server's decimal AwayFromZero rounding (surfaced by the 0.285 → 0.29 midpoint test).
- `partnerId` is a plain text field for v1 (no async partner select) per plan scope.
- `/documents/:id` detail route intentionally left to 03-09; list + edit links point at it forward.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- The first `roundAway` (naive `Math.round(|x|·10^dp)`) failed the 1.50 → 0.285 → 0.29 midpoint test because `0.285 × 100` is `28.4999…96` in binary float, rounding down to 0.28. Fixed by adding a magnitude-scaled epsilon before rounding; all 39 tests then passed. (Resolved within Task 1, part of commit `2eff225`.)
- Git Bash could not resolve `node`/`npm` (the Windows semicolon-PATH is unparseable by sh); built and tested by prepending the nodejs dir to a colon-separated PATH per call.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The DOCS-01 draft chain is now usable end-to-end from the UI: browse/filter documents, create/edit drafts with catalog-snapshotted or free-text lines, and see a live total preview.
- 03-09 should add the `/documents/:id` detail route (the list + edit links already target it), plus finalize/storno/credit-note actions and the convert UI (the `convertSalesDocument` client is ready).
- No blockers. Build green; vitest 39/39 green.

## Self-Check: PASSED

All 7 created files exist on disk; both task commits (`2eff225`, `5aaa8b9`) exist in the repo. Build green; vitest 39/39 green.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-12*
