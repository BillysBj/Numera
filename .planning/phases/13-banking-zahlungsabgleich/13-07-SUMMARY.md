---
phase: 13-banking-zahlungsabgleich
plan: 07
subsystem: banking-react-frontend
tags: [react, typescript, tanstack-query, i18n, banking, reconciliation]

requires:
  - phase: 13-banking-zahlungsabgleich
    provides: Authenticated bank-account and bank-transaction API endpoints from plan 13-06
provides:
  - Bank connection, consent health, re-consent, sync, and statement-import UI
  - Human review queue with match-status and confidence treatment
  - Ranked suggestion, manual allocation, split, explicit confirmation, and unmatch workflow
  - Authenticated banking routes, navigation, and German/English banking translations

completed_tasks: [1, 2]
pending_tasks: [3]
completed: 2026-08-05
---

# Phase 13-07: Banking React frontend

Tasks 1 and 2 are implemented. Task 3 remains the blocking human-verification checkpoint and was
not executed, as requested.

## Implemented

- Added typed TanStack Query hooks for the complete committed banking API surface: accounts,
  connect, re-authorization, consent refresh, sync, multipart statement import, transaction lists,
  suggestions, confirm, unmatch, and ignore. All calls use `lib/api` and therefore send the
  HttpOnly session cookie with `credentials: 'include'`.
- Added the bank-account page with connection health, last sync, consent expiry, consent refresh,
  re-consent, manual sync, finAPI Web Form redirect, and the CSV/MT940/CAMT.053 import fallback.
  The exact Stub 422 detail `Live-Bankanbindung ist nicht konfiguriert.` is converted into the
  user guidance `Nutze den Datei-Import.`
- Added the reconciliation queue with signed amount, value date, purpose, counterparty, numeric
  `MatchStatus` filter, High/Review confidence treatment, and receivable-only behavior. Outgoing
  transactions are marked `Keine Forderung` and expose only Ignore.
- Added the match page with ranked candidate scores and localized reasons, High-tier prefill,
  manual open-item selection, editable allocations, duplicate prevention, split allocation over
  multiple invoices, and an explicit human confirmation card.
- Allocation validation uses integer ten-thousandths because the backend persists
  `BankTransaction.Amount` as `decimal(19,4)`. Confirm remains disabled until every allocation is
  positive, does not exceed its open amount, and the allocation sum exactly equals the incoming
  transaction amount. Nothing auto-submits or auto-books.
- Confirmed matches display the linked payment and `Bank ↔ Forderung`; the unmatch action calls the
  reversal endpoint and returns the transaction to the review workflow.
- Added `/banking`, `/banking/queue`, and `/banking/tx/:id` behind the existing authenticated app
  shell, plus a Banking navigation group and complete German/English `banking` namespace.
- Extended the existing i18n test to assert that the banking bundle exists in both locales.

## Files created

- `web/src/features/banking/bankingApi.ts`
- `web/src/features/banking/BankAccountsPage.tsx`
- `web/src/features/banking/BankConnectCard.tsx`
- `web/src/features/banking/StatementImportCard.tsx`
- `web/src/features/banking/ReconciliationQueuePage.tsx`
- `web/src/features/banking/ReconciliationMatchPage.tsx`
- `web/src/i18n/locales/de/banking.json`
- `web/src/i18n/locales/en/banking.json`
- `.planning/phases/13-banking-zahlungsabgleich/13-07-SUMMARY.md`

## Files modified

- `web/src/App.tsx`
- `web/src/i18n/index.ts`
- `web/src/i18n/i18n.test.ts`

## Endpoint-shape corrections and adaptations

- The plan describes the transaction filter as `?status=`, while the actual minimal-API argument
  is `matchStatus`; the client sends `?matchStatus=<numeric enum>`.
- The API has no `GET /api/bank-transactions/{id}` route. Direct transaction routes therefore
  resolve the requested transaction through the existing paged lists for all five numeric match
  statuses. This also makes confirmed transactions reloadable without changing the committed
  backend.
- The actual confirm payload is camel-cased
  `{ allocations: [{ openItemId, amount }], method? }`; `method` is omitted so the endpoint applies
  its BankTransfer default (numeric value `0`). Numeric `MatchStatus`, `ConsentStatus`, source, and
  candidate tier values mirror the C# declaration order.
- The connect and reauth failure is RFC ProblemDetails with HTTP 422 and the exact German `detail`,
  not a validation-errors dictionary. Import and confirm 422 responses use validation dictionaries;
  the UI error reader supports both shapes.
- Manual allocation reuses the existing authenticated `/api/open-items` list, restricted to Open
  and PartiallyPaid receivables, because suggestions alone are intentionally ranked candidates and
  are not a complete manual picker.

## Verification

Run from `web/` using the Windows npm executable:

- `npm.cmd run build`: passed (`tsc -b && vite build`; 305 modules transformed, 0 errors). Vite
  retained the existing non-blocking chunk-size warning for the 840.99 kB main chunk.
- `npm.cmd run lint`: passed (`tsc -b --noEmit`; 0 errors).
- `npm.cmd test`: passed (7 test files, 56 tests).
- `git diff --check`: passed.

No .NET or Docker command was run. No file was staged or committed.

## Deviations and uncertainties

- No Task 1/2 functional deviation. Task 3 was deliberately skipped and remains pending human
  verification.
- The transaction-list DTO exposes `ConfidenceScore` and `MatchStatus`, but not the scorer's
  `MatchTier`. The queue labels a row High when it is `Suggested` or has score at least `0.85`
  (the scorer's minimum High score from exact reference + exact amount); all other incoming rows
  are Review. The detail suggestions use the authoritative numeric tier returned by the API.
- Resolving a direct transaction URL may issue several paged list requests because the committed
  API lacks a detail route. This is correct for the available surface but is less efficient than a
  future dedicated detail endpoint.

## Live finAPI gap closure (2026-08-06)

- GAP 1: Added authenticated
  `POST /api/bank-accounts/connections/{connectionId}/refresh-accounts`. The RLS-scoped endpoint
  loads the connection, calls `IBankConnectionProvider.ListAccountsAsync`, upserts provider-backed
  `BankAccount` rows by `FinApiAccountId`, updates IBAN/name/currency/connection metadata, refreshes
  the persisted consent snapshot through `GetConsentStatusAsync`, and returns the connection's
  accounts. Repeated calls update existing rows instead of duplicating them; the Stub path returns
  an empty list without an error.
- GAP 2: Extracted the complete config-gated Stub/finAPI registration into the shared
  `AddBankConnectionProvider` service-collection extension. Both API and Worker call it. The
  Worker already references `Numera.Api`, so it can reuse `FinApiOptions`, `FinApiClient`, the data
  protection credential adapter, and `FinApiBankConnectionProvider` without moving types or adding
  packages. Missing ClientId/ClientSecret/BaseUrl continues to resolve `StubBankConnectionProvider`.
- GAP 3: Registered `CheckBankConsentJob` through the shared banking module and added a Worker
  recurring entry `bank-connections:check-consent` at `0 6 * * *`. Its recurring entry point scans
  connections tenant-agnostically using the same infrastructure-connection pattern as sync fan-out,
  then calls the existing idempotent tenant-scoped check for each connection; the class remains on
  Hangfire's `worker` queue.
- Added a real-Postgres integration test for account creation, metadata update, repeat-call
  idempotency, consent persistence, and a cross-tenant connection-id attempt. Added a compile-time
  DI assertion that missing finAPI configuration resolves the no-network Stub. Integration tests
  were deliberately not executed.
- Verification with .NET SDK 10.0.301 and `--configuration Release --no-restore`: API build passed
  with 0 warnings/0 errors; Worker build passed with 0 warnings/0 errors; the integration-test
  project was additionally compiled (not run) with 0 warnings/0 errors.
- No live finAPI call, package addition, migration, frontend change, staging, or commit was made.

## Pending human checkpoint (Task 3)

Human verification of connect/import → sync → queue → confirm/book → split → unmatch, idempotent
re-import, outgoing-only Ignore, and visible consent lifecycle remains to be run with the user.

---
*Phase: 13-banking-zahlungsabgleich · Plan: 07 · Tasks completed: 1–2 only*
