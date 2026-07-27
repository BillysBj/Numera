---
phase: 06-offene-posten-mahnwesen
plan: 02
subsystem: payments-ui
tags: [react, tanstack-query, react-hook-form, zod, i18next, vitest]

# Dependency graph
requires:
  - phase: 06-offene-posten-mahnwesen
    provides: Payment record, reverse, and list BFF endpoints from plan 06-01
provides:
  - Typed numeric-enum payments BFF client
  - Bilingual payment-entry dialog with client-side amount gates
  - Open-items row action and query refresh after recording a payment
  - Component tests for payment defaults, validation, submission, and 422 errors
affects: [open-items, payments, dunning-frontend]

# Tech tracking
tech-stack:
  added: []
  patterns: [RHF and localized zod schema factory, TanStack mutation with open-items invalidation, inline toast notice]

key-files:
  created:
    - web/src/lib/api/payments.ts
    - web/src/features/payments/paymentSchema.ts
    - web/src/features/payments/RecordPaymentDialog.tsx
    - web/src/features/payments/RecordPaymentDialog.test.tsx
    - web/src/i18n/locales/de/payments.json
    - web/src/i18n/locales/en/payments.json
  modified:
    - web/src/features/openItems/OpenItemsListPage.tsx
    - web/src/i18n/index.ts

key-decisions:
  - "PaymentMethod uses numeric values 0–4 matching the C# declaration order."
  - "The payment success/error notice is rendered by the dialog component because the project has no global toast dependency."
  - "The OP list integration is limited to a row-action column and controlled dialog state."

patterns-established:
  - "Payment forms use a localized schema factory so amount limits can depend on the selected open item."
  - "Successful payment writes invalidate the open-items query prefix."

# Metrics
duration: 6min
completed: 2026-07-27
---

# Phase 06 Plan 02: Payments Frontend Summary

**Manual incoming payments can be recorded from the OP overview with partial-payment validation, numeric enum-safe API payloads, bilingual UI, and immediate list refresh**

## Performance

- **Duration:** 6 min
- **Started:** 2026-07-27T23:46:46+02:00
- **Completed:** 2026-07-27T23:52:00+02:00
- **Tasks:** 3
- **Files modified:** 9

## Accomplishments

- Added typed record, reverse, and paged-list payment client operations with cookie-authenticated same-origin requests.
- Added a German-authoritative, bilingual RHF/zod payment dialog that defaults to the open amount and blocks zero, negative, and overpayment values.
- Added the localized payment action only for Open and PartiallyPaid rows and invalidated open-item queries after a successful payment.
- Added five component tests covering the plan's client-side gates and server validation behavior.

## Task Commits

No commits were created because execution explicitly required leaving all changes uncommitted.

## Files Created/Modified

- `web/src/lib/api/payments.ts` - Numeric `PaymentMethod` map, DTOs, and payment endpoints.
- `web/src/features/payments/paymentSchema.ts` - Localized amount/date/method validation schema.
- `web/src/features/payments/RecordPaymentDialog.tsx` - Controlled payment-entry form, mutation, notices, and cache invalidation.
- `web/src/features/payments/RecordPaymentDialog.test.tsx` - Five client-gate component tests.
- `web/src/features/openItems/OpenItemsListPage.tsx` - Additive per-row payment action and dialog wiring.
- `web/src/i18n/locales/de/payments.json` - Authoritative German payment strings.
- `web/src/i18n/locales/en/payments.json` - English payment strings.
- `web/src/i18n/index.ts` - Additive payments namespace registration.
- `.planning/phases/06-offene-posten-mahnwesen/06-02-SUMMARY.md` - Plan execution record.

## Decisions Made

- Kept all payment arithmetic server-authoritative; the client validates and displays amounts but posts the selected decimal value directly.
- Mapped 422 ValidationProblem keys for amount/allocation, value date, method, and reference into RHF field errors, with a localized notice fallback.
- Did not add Skonto fields or behavior.

## Deviations from Plan

None - plan executed as specified. The project has no toast package, so the required toast feedback uses a lightweight fixed notice owned by the dialog instead of adding a dependency.

## Issues Encountered

- React's direct DOM test harness initially emitted `act` environment warnings; the test file now declares the React act environment and the final suite is warning-free apart from existing informational build/i18next output.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Payment entry and open-item refresh are ready for the later dunning frontend enrichment.
- No frontend blocker remains for plan 06-02.

---
*Phase: 06-offene-posten-mahnwesen*
*Completed: 2026-07-27*
