---
phase: 02-stammdaten
plan: 06
subsystem: ui
tags: [react, tailwindcss-v4, shadcn, tanstack-table, tanstack-query, react-hook-form, zod, i18next, bff, pwa]

# Dependency graph
requires:
  - phase: 02-stammdaten (02-04)
    provides: "Partner management HTTP API — /api/partners CRUD + archive/unarchive, contacts, notes, activity timeline; {items,page,pageSize,total} list envelope; FluentValidation rules"
  - phase: 01-plattform-kern (01-07)
    provides: "React 19 + Vite 7 PWA shell, same-origin cookie BFF fetch (credentials:'include'), react-i18next (DE default/EN), /api NetworkOnly PWA posture"
provides:
  - "Frontend UI stack: Tailwind v4 (@tailwindcss/vite) + shadcn/ui primitives + TanStack Table v8 + react-hook-form + zod, building green"
  - "Shared server-side DataTable (manualPagination/Sorting/Filtering, rowCount=total) reused by the catalog frontend (02-07)"
  - "Typed partner BFF client lib/api/partners.ts (CRUD + archive + contacts + notes + activities)"
  - "Partner management UI: server-side list (search/role/archived), RHF+zod create/edit form, detail page (contacts, notes CRM-03, activity timeline CRM-02, archive)"
  - "partners i18n namespace (DE default + EN)"
affects: [02-07-catalog-frontend, catalog-ui, invoicing-ui]

# Tech tracking
tech-stack:
  added: ["tailwindcss@4", "@tailwindcss/vite", "@tanstack/react-table@8", "react-hook-form@7", "zod@4", "@hookform/resolvers@5", "class-variance-authority", "clsx", "tailwind-merge", "@types/node"]
  patterns: ["shadcn/ui primitives hand-authored without Radix (native select/checkbox/dialog) to stay dependency-light and headless-buildable", "server-side TanStack Table via a shared DataTable + parent-owned TanStack Query state", "zod schema mirrors server FluentValidation (client UX only, server authoritative); 400 ValidationProblem mapped onto RHF fields", "numeric enum wire maps (C# enums serialize as numbers — no JsonStringEnumConverter)"]

key-files:
  created: [
    "web/src/lib/utils.ts",
    "web/components.json",
    "web/src/components/ui/ (button,input,label,textarea,checkbox,badge,select,table,card,dialog,form)",
    "web/src/components/DataTable.tsx",
    "web/src/lib/api/partners.ts",
    "web/src/features/partners/PartnerListPage.tsx",
    "web/src/features/partners/PartnerFormPage.tsx",
    "web/src/features/partners/PartnerDetailPage.tsx",
    "web/src/features/partners/partnerSchema.ts",
    "web/src/features/partners/partnerSchema.test.ts",
    "web/src/i18n/locales/de/partners.json",
    "web/src/i18n/locales/en/partners.json"
  ]
  modified: [
    "web/package.json",
    "web/vite.config.ts (tailwind plugin + @/ alias; PWA /api rules untouched)",
    "web/tsconfig.app.json (@/ paths)",
    "web/tsconfig.node.json (node types)",
    "web/src/index.css (tailwind import + shadcn tokens above legacy shell styles)",
    "web/src/i18n/index.ts (partners namespace)",
    "web/src/App.tsx (partner routes + nav)"
  ]

key-decisions:
  - "shadcn/ui primitives hand-authored (no Radix): native-element select/checkbox/dialog keep the build headless-reproducible and dependency-light while matching the shadcn API surface for 02-07 reuse"
  - "Partner enums cross the wire as NUMBERS (the API has no JsonStringEnumConverter) — client keeps const maps (PartnerLanguage De=1/En=2, TaxCategory S=0..O=6, PartnerActivityType PartnerCreated=1..NoteAdded=5)"
  - "zod schema mirrors 02-04 FluentValidation (role-required, 2-letter country, 3-letter currency, offline VAT-ID shape, Skonto 0..100) as UX-only; the server stays authoritative and its 400 ValidationProblem is mapped back onto form fields"
  - "Contacts are a separate API sub-resource, not part of the partner write DTO — the create form persists them via POST /contacts after create; the detail page is the authoritative contacts manager (add/edit/delete)"
  - "lib/api/partners.ts is a fresh file reusing the credentials:'include' posture; lib/api.ts (me/auth) left untouched to minimise shared-file churn"

patterns-established:
  - "Server-side lists: parent owns TanStack Query state (page/size/q/role/archived) keyed with keepPreviousData; shared DataTable runs manual* with rowCount=total"
  - "Forms: react-hook-form + zodResolver with a t-factory schema (localized messages) and extractValidationErrors() mapping server 400s to fields"
  - "New feature UI lives under web/src/features/<area>/ + web/src/lib/api/<area>.ts"

# Metrics
duration: 18min
completed: 2026-07-11
---

# Phase 2 Plan 6: Frontend UI stack + Partner management UI Summary

**Landed the Phase-2 frontend stack (Tailwind v4 + hand-authored shadcn/ui + TanStack Table v8 + RHF + zod) and the full partner UI — server-side paginated/searchable/filterable list, an RHF+zod create/edit form mirroring the server validators, and a detail page with contacts, notes (CRM-03) and the activity timeline (CRM-02) — over the 02-04 cookie-BFF API.**

## Performance

- **Duration:** ~18 min
- **Started:** 2026-07-11T14:02:00Z
- **Completed:** 2026-07-11T14:25:00Z
- **Tasks:** 3
- **Files modified:** 30 (web/* only)

## Accomplishments
- Installed + configured the shared UI stack: Tailwind v4 as a Vite plugin (no PostCSS/tailwind.config), `@/` path alias, shadcn design tokens, and 11 hand-authored `ui/*` primitives + a reusable server-side `DataTable`.
- Typed partner BFF client (`lib/api/partners.ts`) covering CRUD + archive/unarchive + contacts + notes + activities, with numeric enum maps matching the C# wire format and a `extractValidationErrors` helper for 400 ValidationProblem.
- Partner list page: TanStack Query (keepPreviousData) driving `DataTable` with `rowCount=total`, debounced search, role filter, archived toggle, and an inline archive/unarchive action.
- Partner create/edit form (RHF + zod) with nested billing/shipping addresses, a contacts field array, server-error-to-field mapping; detail page with contacts CRUD dialog, notes add/edit/delete, newest-first activity timeline, a Phase-3 "Belege" placeholder, and archive/unarchive with confirm.
- `partners` i18n namespace in DE (default) + EN; +8 schema unit tests. `npm run build` green, typecheck clean, 12/12 vitest passing.

## Task Commits

Each task was committed atomically:

1. **Task 1: Install + configure the UI stack** - `e2fe8c8` (feat)
2. **Task 2: Partner API client + partners i18n + list page** - `b324cf0` (feat)
3. **Task 3: Partner create/edit form + detail page** - `4023be5` (feat)

**Plan metadata:** (this SUMMARY + STATE) committed separately as `docs(02-06)`.

## Files Created/Modified
- `web/vite.config.ts` - Added `@tailwindcss/vite` plugin + `@/`→`src` alias; PWA `/api` NetworkOnly + navigateFallbackDenylist rules left byte-for-byte intact.
- `web/src/index.css` - `@import "tailwindcss"` + shadcn v4 design tokens, above the untouched legacy app-shell styles (Login/Dashboard unaffected).
- `web/src/components/ui/*` - button, input, label, textarea, checkbox, badge, select (native), table, card, dialog (native modal), form (RHF-integrated).
- `web/src/components/DataTable.tsx` - Shared server-side table wrapper (manualPagination/Sorting/Filtering, rowCount) — reused by 02-07.
- `web/src/lib/api/partners.ts` - Typed `/api/partners` client + numeric enum maps + `extractValidationErrors`.
- `web/src/features/partners/PartnerListPage.tsx` - Server-side list with search/role/archived and archive action.
- `web/src/features/partners/partnerSchema.ts` (+ `.test.ts`) - zod schema mirroring server validators; `toWriteRequest` form→DTO mapping; 8 unit tests.
- `web/src/features/partners/PartnerFormPage.tsx` - RHF+zod create/edit, nested addresses, contacts array, server-error mapping.
- `web/src/features/partners/PartnerDetailPage.tsx` - Master data, contacts CRUD, notes CRUD, activity timeline, Belege placeholder, archive.
- `web/src/i18n/index.ts` + `locales/{de,en}/partners.json` - Registered `partners` namespace (DE default + EN).
- `web/src/App.tsx` - `/partners`, `/partners/new`, `/partners/:id`, `/partners/:id/edit` routes + header nav link.

## Decisions Made
- **No Radix**: shadcn/ui primitives were hand-authored over native elements (select/checkbox/dialog) to keep the build reproducible headlessly and dependency-light, while preserving the shadcn API for 02-07 reuse.
- **Numeric enum wire format**: verified the API has no `JsonStringEnumConverter`, so enums serialize as numbers; the client uses const maps rather than string enums (also required because `erasableSyntaxOnly` forbids TS `enum`).
- **Contacts persistence split**: contacts are a separate API sub-resource, so the create form persists them via `POST /contacts` after create, and the detail page is the authoritative contacts manager.
- **`lib/api.ts` untouched**: partner client lives in a fresh `lib/api/partners.ts`, reusing the cookie-BFF fetch posture.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added `@types/node` for the Vite config alias**
- **Found during:** Task 1 (UI stack configuration)
- **Issue:** The `@/`→`src` alias uses `node:url` (`fileURLToPath`/`import.meta.url`) in `vite.config.ts`, but `@types/node` was absent, so `tsc -b` failed with TS2307/TS2339.
- **Fix:** `npm i -D @types/node` and added `"types": ["node"]` to `tsconfig.node.json`.
- **Files modified:** web/package.json, web/tsconfig.node.json
- **Verification:** `npx tsc -b` clean; `npm run build` green.
- **Committed in:** `e2fe8c8` (Task 1 commit)

**2. [Rule 1 - Bug] Shipping address validated even when the toggle was off**
- **Found during:** Task 3 (schema unit tests)
- **Issue:** `shippingAddress` used the required address sub-schema, so an unused (empty) shipping block failed validation even with `hasShippingAddress=false`, blocking every save without shipping.
- **Fix:** Made shipping fields field-level optional and enforced them only via the existing cross-field `.refine()` when `hasShippingAddress` is on.
- **Files modified:** web/src/features/partners/partnerSchema.ts
- **Verification:** 12/12 vitest pass (incl. minimal-valid-customer and shipping-only-when-toggled cases).
- **Committed in:** `4023be5` (Task 3 commit)

**3. [Rule 1 - Bug] `TextField` dropped the RHF ref**
- **Found during:** Task 3 (form wiring)
- **Issue:** The local `TextField` helper received `register()`'s `ref` as a plain prop; a non-forwardRef function component can't forward it, which would break field registration.
- **Fix:** Converted `TextField` to `forwardRef<HTMLInputElement>` passing the ref through to the underlying `Input`.
- **Files modified:** web/src/features/partners/PartnerFormPage.tsx
- **Verification:** typecheck clean; build green.
- **Committed in:** `4023be5` (Task 3 commit)

**4. [Rule 3 - Blocking] Union-typed contact save mutation**
- **Found during:** Task 3 (detail page)
- **Issue:** `saveContact.mutationFn` returned `Promise<void> | Promise<{id}>` (update vs create), which is not assignable to TanStack's `MutationFunction<void>`.
- **Fix:** Wrapped the branch in an `async` fn that `await`s and returns void.
- **Files modified:** web/src/features/partners/PartnerDetailPage.tsx
- **Verification:** `npx tsc -b` clean.
- **Committed in:** `4023be5` (Task 3 commit)

**Adaptation (not a deviation):** the plan's literal `npx shadcn@latest init/add` steps were replaced with hand-authored equivalents (RESEARCH Open Q5 explicitly flagged this tooling as volatile and instructed verifying steps at execution time); the deliverables (`components.json`, `lib/utils.ts` `cn()`, `ui/*`, `DataTable`) are identical.

---

**Total deviations:** 4 auto-fixed (2 blocking, 2 bugs)
**Impact on plan:** All fixes were necessary for a correct, building UI. No scope creep — deliverables match the plan's must-haves.

## Issues Encountered
- Transient Windows file lock on `package-lock.json` during one `npm i` (EPERM/UNKNOWN) — resolved by re-running the install.
- Vite emits a >500 kB chunk-size warning for the single app bundle (React + TanStack + RHF/zod). Benign; route-level code-splitting can be introduced later if needed.

## User Setup Required
None - no external service configuration required. (Live end-to-end against the running API was out of scope per the plan/environment notes; verification was build + typecheck + unit tests. The dev Vite proxy forwards `/api` to the backend for manual testing.)

## Next Phase Readiness
- Shared UI stack (Tailwind v4 + `ui/*` + `DataTable`) and the `features/<area>/` + `lib/api/<area>.ts` pattern are ready for the catalog frontend (02-07), which appends to `App.tsx`, `i18n/index.ts` and `index.css` after this plan.
- Partner management is usable end-to-end from the UI (list/search/filter, create/edit, notes, timeline, archive), satisfying the user-facing side of CRM-01/02/03.

---
*Phase: 02-stammdaten*
*Completed: 2026-07-11*

## Self-Check: PASSED

- All 11 spot-checked artifacts exist on disk (partner client, 3 pages, schema, DataTable, ui/table, components.json, utils, DE/EN partners locales).
- All 3 task commits present in git history: `e2fe8c8`, `b324cf0`, `4023be5`.
- `npx tsc -b` clean; `npm run build` green (PWA `/api` NetworkOnly rules intact); `vitest` 12/12 passing.
