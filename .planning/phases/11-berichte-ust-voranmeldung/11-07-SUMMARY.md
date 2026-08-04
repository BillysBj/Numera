---
phase: 11-berichte-ust-voranmeldung
plan: 07
subsystem: reporting-frontend
tags: [react, typescript, tanstack-query, i18next, ustva, euer]

requires:
  - phase: 11-06
    provides: Authenticated /api/reports review, drill-down and export endpoints
provides:
  - "USt-VA Prüfansicht with period selection, server-provided Kz rows, Zahllast, status metadata and Soll/Ist drill-down"
  - "Kleinunternehmer-gated USt-VA state plus XML and PDF downloads for regular taxpayers"
  - "EÜR period view with income, expenses, totals, Gewinn, incompleteness warning and PDF download"
  - "Reports routes, navigation group and German/English reports namespace"
affects: [human-reporting-verification]

completed_tasks: [1, 2]
pending_tasks: [3]
completed: 2026-08-04
---

# Phase 11-07: Reporting frontend (Tasks 1 and 2)

Tasks 1 and 2 are implemented. Task 3 remains the blocking human-verification checkpoint and was intentionally not performed.

## Implemented

- Added typed TanStack Query hooks for USt-VA review, per-Kz drill-down and EÜR review. Requests use the same-origin `/api` base and always send the HttpOnly auth cookie with `credentials: 'include'`.
- Added authenticated download helpers for USt-VA XML, USt-VA PDF and EÜR PDF, including server-provided filename handling and API error propagation.
- Added the USt-VA Prüfansicht with year/month-or-quarter selection, Besteuerungsart, provisional status, Kz table, server-provided Kz 83 Zahllast row, the no-input-VAT Hinweis and expandable Soll/Ist evidence tables.
- Added the explicit §19 Kleinunternehmer empty state. USt-VA export controls are not rendered for gated reports.
- Added the EÜR view with year/from/to range, Betriebseinnahmen (including every server-provided line such as Zeile 17), Betriebsausgaben, section totals, Gewinn and the visible server-provided incompleteness Hinweis.
- Registered `/reports/ustva` and `/reports/euer`, plus a dedicated Berichte/Reports navigation group.
- Added and registered the `reports` i18n namespace for German and English, with coverage in the existing i18n test.

## Files created

- `web/src/features/reports/ustvaApi.ts`
- `web/src/features/reports/UstVaPruefansichtPage.tsx`
- `web/src/features/reports/EuerReportPage.tsx`
- `web/src/i18n/locales/de/reports.json`
- `web/src/i18n/locales/en/reports.json`
- `.planning/phases/11-berichte-ust-voranmeldung/11-07-SUMMARY.md`

## Files modified

- `web/src/App.tsx`
- `web/src/i18n/index.ts`
- `web/src/i18n/i18n.test.ts`

## Endpoint-shape alignment

- `Besteuerungsart` is a numeric JSON enum (`Soll = 0`, `Ist = 1`), not a string discriminator.
- ASP.NET response properties are typed using their camel-cased JSON names.
- Drill-down uses `recognitionBasis` (`journalEntryDate`, `paymentValueDate`, or `gated`) and the nullable journal/invoice/payment fields returned on every entry; `kind` remains `journal` or `payment`.
- The USt-VA response already includes computed Kz 83 in `lines` and also exposes top-level `zahllast`. The UI renders the server-provided computed line once as the highlighted Zahllast row, avoiding a fabricated or duplicated row.
- Kleinunternehmer review is HTTP 200 with `isKleinunternehmer: true` and empty `lines`; USt-VA exports are HTTP 409. The UI gates before offering those downloads.

## Verification

Commands were run from `web/` using `npm.cmd` because this sandbox's PowerShell execution policy intermittently blocks the `npm.ps1` shim:

- `npm.cmd run build` — passed; TypeScript build and Vite production build completed (290 modules transformed). Vite emitted its existing non-failing chunk-size warning.
- `npm.cmd run lint` — passed with 0 TypeScript errors (`tsc -b --noEmit`).
- `npm.cmd test` — passed: 7 test files, 56 tests.
- Repository diff whitespace check (`git diff --check`) — passed.

## Deviations and uncertainties

- No report-page component test was added; the existing suite has schema/dialog coverage rather than a page test requirement. The reports namespace was added to the existing i18n resource test.
- No live API/browser verification was performed because that is Task 3, the explicitly excluded human checkpoint.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left untouched.

## Pending: Task 3

Human verification remains pending for the seeded-ledger end-to-end flow: Soll and Ist drill-down, XML/PDF contents, payment-date recognition, provisional/festgeschrieben behavior, EÜR values and warning, and Kleinunternehmer gating.
