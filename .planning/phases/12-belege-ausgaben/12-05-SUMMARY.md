---
phase: 12-belege-ausgaben
plan: 05
subsystem: receipt-review-booking
tags: [dotnet, receipts, ledger, vat, rls, audit, einvoice]

requires:
  - phase: 12-belege-ausgaben
    plan: 01
    provides: Multi-rate ExpensePostingInput/ExpensePostingSource and SKR expense mappings
  - phase: 12-belege-ausgaben
    plan: 04
    provides: Unified Receipt aggregate, Tier-A inbound link, and authenticated receipt route group
provides:
  - "Human-gated receipt review and non-persisting booking proposal preview"
  - "Idempotent confirm-book path producing one expense JournalEntry from one multi-rate input"
  - "Booking-date USt-VA Vorsteuer Kz 66 with Storno netting and reduced Kz 83 Zahllast"
  - "Real-Postgres coverage for review, booking, multi-rate VAT, and Kz 66"
affects: [banking-supplier-payments, receipt-reversal-follow-up]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-05: Receipt review and confirm-book

The owned receipt surface now supports proposal preview, human review, and idempotent
confirm-to-book. A receipt can reach `Booked` only after a current user stamps it `Reviewed`;
confirm-book builds one `ExpensePostingInput`, opens one caller-owned transaction, and invokes
`PostingEngine.PostAsync` exactly once to persist one balanced `JournalEntry` with all rate legs.

## Implemented

- Extended `/api/receipts` with:
  - `GET /{id}/proposal`, which is read-only and previews the exact breakdown and posting legs.
  - `PATCH /{id}/review`, restricted to `Extracted`/`Reviewed`, validating supported 19/7/0 rates,
    non-negative decimal amounts, `Net + Vat == Gross` within one cent, a booking date, and an
    RLS-visible supplier role before stamping `ReviewedByUserId` and `ReviewedAt`.
  - `POST /{id}/confirm-book`, rejecting quarantined or unreviewed receipts with 422, guarding
    idempotently on `Expense + receipt.Id`, resolving the tenant chart, and atomically linking the
    resulting entry back to the receipt with an audit event.
- Added proposal/review/booking contracts, including frozen per-rate rows and resolved account
  posting legs.
- Supplier resolution follows `Receipt.MatchedPartnerId` to the RLS-scoped `BusinessPartner`; its
  `CreditorAccount` is used when present, otherwise the SKR default creditor is resolved.
- Expense account preview uses the confirmed override or `SkrMapping.ExpenseMapping`, yielding
  4980 for SKR03 and 6300 for SKR04.
- Tier-A multi-rate data is loaded by reading the linked `InboundDocument.ReadModel`, deserializing
  `InboundReadModel`, and mapping every `BreakdownRow` to `(VatRatePercent, TaxableBase,
  TaxAmount)`. `AE`/`K` and rates outside 19/7/0 are rejected because reverse charge and
  intra-community acquisition are explicitly outside this phase.
- Both preview and booking call `ReceiptBookingProposal.Build` once. Booking passes the resulting
  N-row input to one `ExpensePostingSource` and one `PostingEngine.PostAsync` call, so a 19%+7%
  receipt produces two expense legs, two input-tax legs, and one creditor leg in one entry. A 0%
  row produces no input-tax leg.
- The booking transaction includes the journal header/postings, receipt status/link, and audit
  persistence before commit. Confirmed breakdown totals are checked against receipt net, VAT, and
  gross totals before and after posting.
- Added compile-verified Testcontainers coverage for the never-auto-book gate, balanced 19% entry,
  second-call idempotency, 0% without input tax, one-entry Tier-A 19%+7% booking, and the booked
  receipt feeding USt-VA Kz 66 while reducing Kz 83.
- Extended the fiscal-year USt-VA map additively from `{81, 86, 41, 83}` to
  `{81, 86, 41, 66, 83}`. Kz 66 is the two-decimal tax figure
  `Vorsteuerbeträge aus Rechnungen von anderen Unternehmern`.
- Extended Soll recognition rows with account type. The seed labels both input-tax and expense
  accounts with Kz 66; selecting Kz-66 `Asset` accounts isolates 1576/1571/1406/1401 and prevents
  expense net amounts from being counted as Vorsteuer.
- `UstVaCalculator` now reads booking-date Soll recognition for Kz 66 for both Soll- and
  Ist-Versteuerung. Debit is the natural side; opposite-side credit postings from a Storno subtract
  in the reversal entry's booking period. Kz 83 is now `Kz81*19% + Kz86*7% - Kz66`, and the obsolete
  output-VAT-only hint was removed.
- The reports-feed test is active and asserts a booked 22.80 EUR input-tax posting produces
  Kz 66 = 22.80 and Kz 83/Zahllast = -22.80 when the period has no output VAT.
- EÜR remains deliberately unchanged. It is cash-basis: a supplier expense reaches
  Betriebsausgaben only when the supplier payment is recognized, deferred to the Banking phase.

## Files modified

- `src/Numera.Api/Endpoints/ReceiptEndpoints.cs`
- `src/Numera.Api/Contracts/ReceiptContracts.cs`
- `src/Numera.Api/Reporting/UstVaKennzifferMap.cs`
- `src/Numera.Api/Reporting/UstVaCalculator.cs`
- `src/Numera.Api/Reporting/RecognitionReader.cs`
- `src/Numera.Api/Reporting/RecognitionModels.cs`
- `tests/Numera.IntegrationTests/UstVaCalculatorTests.cs`
- `tests/Numera.IntegrationTests/ReportRecognitionTests.cs`

## Files created

- `tests/Numera.IntegrationTests/ReceiptBookingTests.cs`
- `.planning/phases/12-belege-ausgaben/12-05-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- API Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Integration-test project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Repository whitespace check for the touched tracked changes: passed; Git reported only LF-to-CRLF
  working-copy notices.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- The user-approved cross-phase extension resolves the prior Kz-66 blocker. EÜR is intentionally
  absent from the receipt-booking assertion: its cash-basis contract defers Betriebsausgaben until
  an outgoing supplier payment exists.
- The numbered plan text places the `Reviewed` gate before the idempotency guard, but also requires a
  second confirm-book call to return already-booked after the first call has changed the receipt to
  `Booked`. To satisfy the observable idempotency requirement, confirm-book rejects quarantine first,
  then checks the mandated `AnyAsync(Expense, receipt.Id)` guard, and only applies the Reviewed gate
  when no existing entry is present.
- Missing `LedgerSettings` returns a 422 and leaves the receipt Reviewed. The mirrored sales hook
  logs and silently skips its posting because invoice finalization has a broader lifecycle to finish;
  confirm-book must not report success or mark a receipt Booked without a journal entry.
- No receipt edit/delete route was added for `Booked`; corrections remain reversal-only follow-up
  work. No migration, `Program.cs`, `MailboxEndpoints.cs`, or worker file was touched.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left
  untouched.
