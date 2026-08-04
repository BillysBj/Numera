---
phase: 12-belege-ausgaben
plan: 07
subsystem: receipt-frontend
tags: [react, typescript, tanstack-query, i18next, receipts, upload, review, booking]

requires:
  - phase: 12-belege-ausgaben
    plan: 05
    provides: Receipt review, proposal, and confirm-book endpoints
  - phase: 12-belege-ausgaben
    plan: 06
    provides: Per-tenant receipt mailbox endpoint
provides:
  - "Receipt camera/file capture with duplicate and quarantine outcomes"
  - "Filterable receipt review queue with source/status badges and tenant mailbox"
  - "Immutable-original-alongside-fields review, confidence badges, proposal legs, and explicit confirm-book"
  - "Belege routes, navigation group, and German/English translations"
affects: [phase-12-human-verification]

completed_tasks: [1, 2]
pending_tasks: [3]
completed: 2026-08-04
---

# Phase 12-07: Belege frontend (Tasks 1 and 2)

Tasks 1 and 2 are implemented. Task 3 is intentionally not executed: it remains the blocking
human-verification checkpoint for Claude and the user.

## Implemented

- Added typed numeric wire enums and camel-cased DTOs matching the receipt and mailbox contracts.
- Added TanStack Query hooks for receipt list/detail/proposal, review, confirm-book, multipart upload,
  authenticated immutable-original loading, and mailbox provisioning.
- Exported the shared `lib/api` request helpers and made JSON content-type selection FormData-safe,
  so all new receipt calls retain same-origin `credentials: 'include'` without overriding the
  browser-generated multipart boundary.
- Added a capture page with a mobile rear-camera input, keyboard-accessible PDF/image drop zone,
  15 MiB/type validation, malware-quarantine handling, duplicate handling, and queue navigation.
- Added a status-filtered review queue for Captured, Extracted, Reviewed, Booked, Duplicate, and
  Quarantined receipts, including source badges and de-DE date/money formatting.
- Added a tenant mailbox card with the provisioned `belege-…@…` address, active state, copy action,
  and forwarding guidance.
- Added the split review workspace: the archived original is fetched as authenticated binary data,
  displayed from a temporary browser object URL, and kept visible alongside the editable review
  fields. The original is never written or mutated by the frontend.
- Added confidence badges for every extractor-backed field. Missing and sub-80% confidence values
  receive the low-confidence highlight used by the editable field container.
- Added supplier selection from existing supplier partners with `matchedPartnerId` pre-selected,
  automatic/SKR03/SKR04 expense-account selection, 19/7/0 VAT selection, and an e-invoice
  breakdown option.
- Added the explicit two-gate interaction: save the human review to obtain a current proposal,
  then enable `Bestätigen & Buchen` only while the persisted review and visible form agree. The UI
  states explicitly that extraction and review alone never create a booking.
- Added proposal breakdown and posting-leg tables. Multi-rate e-invoice rows render individually;
  debit/credit values use the server's numeric `PostingDirection` values 1 and 2.
- After booking, the linked `journalEntryId` is shown and the original viewer remains mounted.
- Registered `/belege`, `/belege/capture`, and `/belege/:id`, plus a dedicated Belege navigation
  group with queue and capture entries.
- Added complete German and English `belege` namespaces and extended the existing namespace test.

## Endpoint contract corrections applied

- `ReceiptStatus` is numeric `Captured=0, Extracted=1, Reviewed=2, Booked=3, Duplicate=4,
  Rejected=5, Quarantined=6`; `ReceiptSource` is numeric `Camera=0, Upload=1, Email=2,
  EInvoice=3`.
- The upload success shape is only `{ id, status }`. Quarantine does not return that shape: the
  endpoint persists the quarantined row and returns a 422 validation problem, so capture handles
  status 422 explicitly and shows the quarantine notice in the queue.
- The review PATCH does not accept extracted supplier name, VAT ID, or currency. Those remain
  provenance/read-only values; the editable supplier field sends `supplierPartnerId`, and the body
  exactly matches the nine `ReviewReceiptRequest` properties.
- Proposal property names match `supplier`, `expenseAccount`, `entryDate`, `breakdowns`,
  `postingLegs`, and the three totals. Posting direction is `Debit=1` / `Credit=2`, not a zero-based
  enum.
- The original endpoint returns binary bytes with a download filename, not a JSON viewer URL. The
  UI therefore uses the authenticated shared binary helper and a revocable object URL so inline
  image/PDF display works even with attachment response headers.
- Mailbox JSON is exactly `{ address, isActive, createdAt }`.

## Files created

- `web/src/features/belege/belegeApi.ts`
- `web/src/features/belege/BelegCapturePage.tsx`
- `web/src/features/belege/BelegReviewQueuePage.tsx`
- `web/src/features/belege/BelegReviewPage.tsx`
- `web/src/features/belege/MailboxAddressCard.tsx`
- `web/src/i18n/locales/de/belege.json`
- `web/src/i18n/locales/en/belege.json`
- `.planning/phases/12-belege-ausgaben/12-07-SUMMARY.md`

## Files modified

- `web/src/App.tsx`
- `web/src/lib/api.ts`
- `web/src/i18n/index.ts`
- `web/src/i18n/i18n.test.ts`

## Verification

- `cd web; npm run build` — passed. TypeScript and Vite completed with 0 errors; Vite emitted only
  its pre-existing-style large-chunk advisory (801.75 kB main chunk).
- `cd web; npm run lint` — passed with 0 errors (`tsc -b --noEmit`). In this sandbox the direct
  PowerShell `npm.ps1` shim was policy-blocked on one invocation, so the successful verification
  was run through `cmd.exe /d /c "npm run lint"` (and independently through `npm.cmd run lint`).
- `cd web; npm test` — passed: 7 test files, 56 tests. The successful verification was run through
  `cmd.exe /d /c "npm test"` (and independently through `npm.cmd test`) to avoid the same PowerShell
  shim policy.
- `git diff --check` — passed.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was untouched.

## Deviations and uncertainties

- No reusable v1 camera component exists in `web/src`; the capture page therefore uses the native
  PWA/browser `capture="environment"` affordance and the app's existing file-input conventions.
- The only receipt multipart endpoint hardcodes `ReceiptSource.Upload`. A photo taken through the
  new camera affordance is uploaded successfully but will be labeled Upload, not Camera, until the
  backend accepts a source discriminator or exposes a camera-specific endpoint. The frontend-only
  scope was preserved.
- There is no account-list endpoint. The selector offers the safe automatic chart mapping plus the
  two backend defaults (`4980` SKR03 and `6300` SKR04), clearly labeled by chart.
- There is no journal-entry detail route in the current SPA/API. The booked receipt shows its linked
  `journalEntryId` rather than fabricating a dead link.
- No belege-specific component test was added; the existing i18n test was extended and the full
  frontend suite remains green. Runtime endpoint behavior belongs to pending Task 3.
- Task 3 remains pending for human verification of capture, extraction, review/booking, reports,
  e-invoice multi-rate behavior, WORM persistence, duplicates, quarantine, and email intake.
