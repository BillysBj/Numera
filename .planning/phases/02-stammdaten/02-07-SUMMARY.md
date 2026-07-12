---
phase: 02-stammdaten
plan: 07
subsystem: ui
tags: [react, tanstack-table, tanstack-query, react-hook-form, zod, i18next, bff, catalog, un-ece]

# Dependency graph
requires:
  - phase: 02-stammdaten (02-05)
    provides: "Catalog (Artikelstamm) HTTP API — /api/catalog-items CRUD + archive/unarchive, {items,page,pageSize,total} list envelope, ?picker=true CatalogLineItem lookup, duplicate-number → 409; UnitOfMeasure curated UN/ECE Rec 20 code set; FluentValidation rules"
  - phase: 02-stammdaten (02-06)
    provides: "Frontend UI stack (Tailwind v4 + hand-authored shadcn/ui + TanStack Table v8 + RHF + zod), shared server-side DataTable, features/<area>/ + lib/api/<area>.ts pattern, partners i18n namespace, extractValidationErrors"
provides:
  - "Catalog management UI: server-side paginated/searchable list (archived toggle), RHF+zod create/edit form with a UN/ECE Rec 20 unit dropdown, TaxCategory + VAT rate, archive/unarchive"
  - "Typed catalog BFF client lib/api/catalog.ts (CRUD + archive + lookupCatalogItems picker seam)"
  - "lookupCatalogItems(q) — the CATL-02 frontend seam Phase 3 invoice-line UI imports"
  - "catalog i18n namespace (DE default + EN)"
  - "UN/ECE Rec 20 code→German label map (web/src/features/catalog/units.ts) mirroring the server UnitOfMeasure set"
affects: [invoicing-ui, catl-02, phase-03-belege]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Reused the 02-06 UI stack verbatim (no new deps): DataTable, ui/* primitives, RHF+zod, TanStack Query"
    - "Shared DataTable made namespace-aware via an optional translationNs prop (default 'partners') so the catalog pager reads catalog strings — backward-compatible, no partner churn"
    - "Net price formatted for display only via Intl.NumberFormat (EUR) — the decimal wire value is never used in client arithmetic, so no float drift in the UI"
    - "Catalog form maps BOTH 400 ValidationProblem and the 409 duplicate-article-number Conflict onto RHF fields (a local extractor, since the shared partner extractor only handles 400)"

key-files:
  created:
    - "web/src/lib/api/catalog.ts"
    - "web/src/features/catalog/units.ts"
    - "web/src/features/catalog/CatalogListPage.tsx"
    - "web/src/features/catalog/CatalogFormPage.tsx"
    - "web/src/features/catalog/catalogSchema.ts"
    - "web/src/features/catalog/catalogSchema.test.ts"
    - "web/src/i18n/locales/de/catalog.json"
    - "web/src/i18n/locales/en/catalog.json"
  modified:
    - "web/src/components/DataTable.tsx (optional translationNs prop, default 'partners')"
    - "web/src/i18n/index.ts (catalog namespace registered)"
    - "web/src/App.tsx (/catalog, /catalog/new, /catalog/:id routes + nav link)"

key-decisions:
  - "Catalog enums cross the wire as NUMBERS (no JsonStringEnumConverter): CatalogItemKind Product=1/Service=2; TaxCategory S=0..O=6 reused from the partner client"
  - "/catalog/:id opens the CatalogFormPage in edit mode — catalog has no detail page (no notes/timeline like partners), so the list links straight to edit"
  - "Unit dropdown offers only the curated UN/ECE Rec 20 codes; changing the kind snaps the unit to the kind default (C62 product / HUR service), mirroring UnitOfMeasure.DefaultFor"
  - "DataTable gained an optional translationNs prop rather than duplicating the component — the catalog and partner lists share one server-side table, each supplying its own table.* strings"

patterns-established:
  - "lookupCatalogItems(q) is the fixed CATL-02 frontend boundary: Phase-3 invoice-line UI imports it (hits ?picker=true) and snapshots the returned CatalogLineItem onto a line"

# Metrics
duration: 24min
completed: 2026-07-12
---

# Phase 2 Plan 7: Catalog frontend (Artikelstamm UI) Summary

**Built the catalog management UI over the 02-05 API — a server-side paginated/searchable TanStack Table list with an archived toggle and an RHF+zod create/edit form with a UN/ECE Rec 20 unit dropdown, TaxCategory and VAT rate, plus archive/unarchive — and exported the `lookupCatalogItems` CATL-02 picker seam for Phase 3. Reused the 02-06 UI stack with zero new dependencies.**

## Performance

- **Duration:** ~24 min
- **Started:** 2026-07-12T09:14:17Z
- **Completed:** 2026-07-12T09:38:00Z
- **Tasks:** 2
- **Files:** 11 (8 created, 3 modified — web/* only)

## Accomplishments
- Typed catalog BFF client (`lib/api/catalog.ts`): `listCatalogItems`, `getCatalogItem`, `createCatalogItem`, `updateCatalogItem`, `archiveCatalogItem`, `unarchiveCatalogItem`, and `lookupCatalogItems` (the CATL-02 picker seam Phase 3 imports), with numeric enum maps matching the C# wire format.
- UN/ECE Rec 20 unit map (`units.ts`): code→German label + ordered code list + `defaultUnitFor`, mirroring the server `UnitOfMeasure.Codes`.
- Catalog list page: server-side TanStack Query (keepPreviousData) driving the shared `DataTable` with `rowCount=total`, debounced search, archived toggle, per-row edit/archive actions; net price formatted via `Intl.NumberFormat` (EUR).
- Catalog create/edit form (RHF + zod) mirroring the 02-05 FluentValidation: article number (≤64), name (≤200), kind, curated unit dropdown, non-negative net price, 3-letter currency, TaxCategory (S/AE/K/E/Z/G/O), optional VAT rate (0..100) and cost price; kind-driven default unit; 400/409 duplicate-number → field errors; archive/unarchive (no delete).
- `catalog` i18n namespace in DE (default) + EN; +10 schema unit tests. `npm run build` green, `vitest` 22/22 passing.

## Task Commits

Each task was committed atomically:

1. **Task 1: Catalog API client + units + i18n + list page** — `b3d84f1` (feat)
2. **Task 2: Catalog create/edit form + archive** — `2f8dac5` (feat)

**Plan metadata:** (this SUMMARY + STATE) committed separately as `docs(02-07)`.

## Files Created/Modified
- `web/src/lib/api/catalog.ts` — typed `/api/catalog-items` client + numeric enum maps + `lookupCatalogItems` CATL-02 seam; re-exports the shared `extractValidationErrors`.
- `web/src/features/catalog/units.ts` — curated UN/ECE Rec 20 code→German label map, ordered codes, `defaultUnitFor`, `unitLabel`, `isValidUnitCode`.
- `web/src/features/catalog/CatalogListPage.tsx` — server-side list (search + archived toggle) reusing `DataTable`.
- `web/src/features/catalog/CatalogFormPage.tsx` — RHF+zod create/edit, unit dropdown, TaxCategory/VAT/cost fields, archive/unarchive, 400/409 error mapping.
- `web/src/features/catalog/catalogSchema.ts` (+ `.test.ts`) — zod schema mirroring server validators; `toCatalogWriteRequest`; 10 unit tests.
- `web/src/i18n/index.ts` + `locales/{de,en}/catalog.json` — registered `catalog` namespace (DE default + EN).
- `web/src/components/DataTable.tsx` — optional `translationNs` prop (default `'partners'`), backward-compatible.
- `web/src/App.tsx` — `/catalog`, `/catalog/new`, `/catalog/:id` routes + header nav link.

## Decisions Made
- **Numeric enum wire format**: `CatalogItemKind` Product=1/Service=2; `TaxCategory` S=0..O=6 (the API has no `JsonStringEnumConverter`).
- **No catalog detail page**: `/catalog/:id` opens the form in edit mode — catalog items have no notes/timeline, so a partner-style detail page would be empty.
- **Kind-driven default unit**: switching kind snaps the unit to C62 (product) / HUR (service), mirroring `UnitOfMeasure.DefaultFor`.
- **Shared DataTable, not a fork**: added an optional `translationNs` prop so the catalog and partner lists share one server-side table.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Shared DataTable pager showed partner wording on the catalog list**
- **Found during:** Task 1 (list page)
- **Issue:** `DataTable` hardcoded `useTranslation('partners')`, so its pager `table.total` ("{{count}} Partner") and other pager strings would render partner wording on the catalog page.
- **Fix:** Added an optional `translationNs` prop (default `'partners'` — no partner churn); the catalog list passes `'catalog'`. Both namespaces expose identical `table.*` keys.
- **Files modified:** web/src/components/DataTable.tsx, web/src/features/catalog/CatalogListPage.tsx
- **Verification:** build green; catalog list pager reads catalog strings.
- **Committed in:** `b3d84f1` (Task 1 commit)

**Adaptation (not a deviation):** The plan assigned `App.tsx` to Task 1, but the `/catalog/new` and `/catalog/:id` routes import `CatalogFormPage` (created in Task 2). To keep each atomic commit building, Task 1 wired only the `/catalog` list route + nav link and Task 2 appended the two form routes. Final `App.tsx` matches the plan (all `/catalog*` routes + nav).

**Adaptation (not a deviation):** The 409 duplicate-article-number Conflict is mapped onto the `itemNumber` field via a local `extractFieldErrors` (the shared partner `extractValidationErrors` only accepts 400); it handles both 400 and 409 identically.

---

**Total deviations:** 1 auto-fixed (1 bug). No architectural changes; no scope creep.
**Impact on plan:** All must-haves met; the partner UI, routes and i18n namespaces from 02-06 are untouched.

## Issues Encountered
- None beyond the DataTable namespace bug above. Vite still emits the benign >500 kB single-bundle chunk-size warning (unchanged from 02-06).

## User Setup Required
None. Live end-to-end against the running API was out of scope per the plan/environment notes; verification was `npm run build` + `vitest`. The dev Vite proxy forwards `/api` to the backend for manual testing.

## Next Phase Readiness
- Phase 2 (Stammdaten) is complete: 7/7 plans. Partner + catalog management are usable end-to-end from the UI.
- `lookupCatalogItems(q)` is the fixed CATL-02 frontend boundary — Phase-3's invoice-line editor imports it and snapshots `CatalogLineItem` onto lines.
- PWA `/api` NetworkOnly posture untouched; build green, 22/22 vitest passing.

---
*Phase: 02-stammdaten*
*Completed: 2026-07-12*

## Self-Check: PASSED

- All 8 created artifacts exist on disk (catalog client, units, list + form pages, schema + test, DE/EN locales).
- Both task commits present in git history: `b3d84f1`, `2f8dac5`.
- `npm run build` green (PWA `/api` NetworkOnly intact); `vitest` 22/22 passing (10 new catalog tests).
