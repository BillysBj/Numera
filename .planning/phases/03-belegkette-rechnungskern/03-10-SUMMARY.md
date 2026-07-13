---
phase: 03-belegkette-rechnungskern
plan: 10
subsystem: ui
tags: [react, react-query, react-hook-form, zod, i18next, tanstack-table, bff]

# Dependency graph
requires:
  - phase: 03-01
    provides: "GET/PUT /api/company-profile (§14 issuer upsert) + CompanyProfileContracts + validators"
  - phase: 03-04
    provides: "GET /api/open-items (paged, due-date sorted, overdue flag) + OpenItemContracts"
  - phase: 03-09
    provides: "/documents/:id detail route (open-items rows link to it) + lib/api/documents.ts stack"
provides:
  - "OP-Übersicht (open-items receivables list) — server-side paged, status + overdue-only filters, rows link to the document detail"
  - "§14 company-profile settings form (RHF + zod upsert) with exactly-one-tax-id validation + server-error mapping"
  - "openItems + settings BFF clients (lib/api/openItems.ts, lib/api/companyProfile.ts)"
  - "openItems + settings i18n namespaces (DE/EN, DE authoritative)"
  - "/open-items + /settings routes + nav links (the sole wave-7 App.tsx + i18n/index.ts edits)"
affects: [phase-06-zahlungen, phase-04-e-rechnung, e-rechnung, payments, finalize]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Read-only server-side list via shared DataTable (manualPagination + translationNs) — mirrors CatalogListPage with no create/edit"
    - "RHF + zodResolver settings form over a GET-prefill / PUT-upsert API, mirroring CatalogFormPage; empty-shell GET normalised to null"
    - "zod .refine XOR mirroring a server FluentValidation Must (exactly-one-tax-id)"

key-files:
  created:
    - web/src/lib/api/openItems.ts
    - web/src/features/openItems/OpenItemsListPage.tsx
    - web/src/lib/api/companyProfile.ts
    - web/src/features/settings/companyProfileSchema.ts
    - web/src/features/settings/companyProfileSchema.test.ts
    - web/src/features/settings/CompanyProfileSettingsPage.tsx
    - web/src/i18n/locales/de/openItems.json
    - web/src/i18n/locales/en/openItems.json
    - web/src/i18n/locales/de/settings.json
    - web/src/i18n/locales/en/settings.json
  modified:
    - web/src/i18n/index.ts
    - web/src/App.tsx

key-decisions:
  - "getCompanyProfile normalises the server's all-nulls shell to null so the form distinguishes first-use from a persisted profile; the empty state is otherwise identical"
  - "Open-items list is read-only (no create/edit) — payments are Phase 6; the row links out to the 03-09 document detail rather than a dedicated OP detail"
  - "zod error message keys are namespace-relative (errors.*), resolved by the settings-scoped t — mirrors catalogSchema's form.errors.* convention"

# Metrics
duration: 12min
completed: 2026-07-13
---

# Phase 3 Plan 10: OP-Übersicht + §14 Company-Profile Settings Summary

**The two remaining Phase-3 consumer surfaces — a server-side OP-Übersicht (open-items receivables over GET /api/open-items, status + overdue filters, rows linking to the document detail) and a §14 issuer company-profile RHF + zod upsert form (exactly-one-tax-id + server-error mapping) — both bilingual and nav-reachable.**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-07-13
- **Completed:** 2026-07-13
- **Tasks:** 3
- **Files modified:** 12 (10 created, 2 modified)

## Accomplishments
- OP-Übersicht: the read frontend of OPDN-01 — a paged, due-date-sorted receivables list over the 03-04 `GET /api/open-items` with a status select + overdue-only checkbox, original/open EUR amounts, localised issue/due dates, a status Badge and a destructive "überfällig" Badge; document numbers link to the 03-09 detail page.
- §14 company-profile settings: an RHF + `zodResolver` upsert form over the 03-01 `GET/PUT /api/company-profile` with Stammdaten/Steuer/Zahlung/Bank Card sections, GET-prefill (empty state on first use), client-side exactly-one-tax-id zod refine, 400 ValidationProblem → field mapping, a §19 hint and a "frozen at finalize" note.
- Two typed BFF clients (`openItems.ts`, `companyProfile.ts`) mirroring the C# contracts (enums as numbers, `credentials:'include'`), plus a 8-case vitest suite locking the §14 client-side gates.
- Registered the `openItems` + `settings` i18n namespaces (DE authoritative) and added the `/open-items` + `/settings` routes + nav links — the sole wave-7 edits to `i18n/index.ts` and `App.tsx`, both additive.

## Task Commits

Each task was committed atomically:

1. **Task 1: openItems client + OpenItemsListPage + openItems i18n** - `9174db7` (feat)
2. **Task 2: companyProfile client + schema (+ test) + settings page + settings i18n** - `c03f3ed` (feat)
3. **Task 3: register namespaces + routes + nav links** - `07d7939` (feat)

## Files Created/Modified
- `web/src/lib/api/openItems.ts` - Typed BFF client for GET /api/open-items (listOpenItems, OpenItemStatus enum mirror)
- `web/src/features/openItems/OpenItemsListPage.tsx` - Server-side paged OP-Übersicht (shared DataTable, status + overdue filters, detail links, status/overdue Badges)
- `web/src/lib/api/companyProfile.ts` - Typed BFF client for GET/PUT /api/company-profile (getCompanyProfile empty-shell→null, updateCompanyProfile upsert)
- `web/src/features/settings/companyProfileSchema.ts` - zod schema mirroring the §14 server rules incl. exactly-one-tax-id refine + toUpdateCompanyProfileRequest mapper
- `web/src/features/settings/companyProfileSchema.test.ts` - 8 vitest cases (vatId-only / taxNumber-only pass, both/neither fail, required legalName + address, payment-terms range)
- `web/src/features/settings/CompanyProfileSettingsPage.tsx` - RHF + zodResolver §14 settings form (GET prefill, PUT upsert, 400 field mapping, §19 hint, frozen note)
- `web/src/i18n/locales/{de,en}/openItems.json` - Open-items namespace (nav, columns, status names, overdue, filters, table strings)
- `web/src/i18n/locales/{de,en}/settings.json` - Settings namespace (sections, field labels, hints, tax categories, §14 errors)
- `web/src/i18n/index.ts` - Registered openItems + settings resources + ns array (additive)
- `web/src/App.tsx` - /open-items + /settings routes + Offene Posten/Einstellungen nav links (append-only)

## Decisions Made
- `getCompanyProfile` normalises the server's all-nulls editable shell (no legalName, no address, no tax ids) to `null` so the form can distinguish a first-use create state from a persisted profile; the resulting empty form is otherwise identical either way.
- The OP-Übersicht is deliberately read-only — open items are created at finalize (03-05) and reduced by payments (Phase 6), neither exposed here — and links each row to the 03-09 document detail rather than introducing a dedicated OP detail page.
- zod validation-message keys are namespace-relative (`errors.*`, resolved by the `useTranslation('settings')`-scoped `t`), mirroring `catalogSchema`'s `form.errors.*` convention — the initial draft used a `settings.`-prefixed key that would not resolve under the scoped translator and was corrected before the Task-2 commit.

## Deviations from Plan

None - plan executed exactly as written. (The plan-frontmatter also listed no `logoRef` in the form fields; it is carried on the DTO/schema as an untouched optional passthrough, matching the 03-01 contract — not a deviation.)

## Issues Encountered
None. Build green (`tsc -b` + `vite build`), `tsc -b --noEmit` clean, full vitest suite 47/47 green (39 prior + 8 new).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 3's frontend surface is complete: draft editor (03-07), document detail + lifecycle (03-09), OP-Übersicht + §14 settings (03-10). A user can now fill in the issuer profile that finalize requires and see every finalized invoice's open item with its due date + overdue status.
- CARRIED-FORWARD BLOCKER (from 03-08, unchanged): the latent production finalize bug — `SalesDocumentEndpoints.FinalizeCoreAsync` adds the BG-23 breakdown via `doc.TaxBreakdown.Add(...)` on the tracked parent → EF marks the client-PK child Modified → 0-row UPDATE → `DbUpdateConcurrencyException`. Finalize/Storno/credit-note-finalize will THROW the moment a breakdown row is written. One-line src/* fix (`db.Add(row)`); the 03-08 harness regression-guards the correct pattern. This is a src/* fix outside this web-only plan and MUST land before finalize is exercised end-to-end.
- NON-BLOCKING (from 03-09, unchanged): the detail projection still does not expose the §14 issuer/recipient snapshots; the settings form now feeds the profile those snapshots freeze, but the detail cards stay on the "frozen on finalize" hint until a src/* follow-up adds the two jsonb fields to the detail DTO.

## Self-Check: PASSED

All 10 created files verified present; all 3 task commits (9174db7, c03f3ed, 07d7939) verified in the git log. Build green, tsc clean, vitest 47/47.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*
