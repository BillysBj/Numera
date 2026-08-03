---
phase: 10-buchhaltungs-fundament
plan: 05
subsystem: ledger
tags: [ledger, payment, posting, reversal, open-items]

requires:
  - phase: 10-03
    provides: PostingEngine + PaymentPostingSource + AccountResolver
  - phase: 10-02
    provides: per-tenant chart + ledger_settings
provides:
  - "Automatic balanced Bank/Forderung booking for recorded payments"
  - "Payment Generalumkehr atomic with open-item restoration"
affects: [ledger-reads, festschreibung, cash-reconciliation, reports]

tech-stack:
  added: []
  patterns:
    - "Inline payment posting inside the existing settlement transaction"
    - "One receivable posting leg per PaymentAllocation"
    - "Graceful skip for pre-ledger tenants without LedgerSettings"

key-files:
  created:
    - tests/Numera.IntegrationTests/LedgerPaymentPostingTests.cs
  modified:
    - src/Numera.Api/Services/PaymentService.cs

key-decisions:
  - "All MVP PaymentMethods resolve to the selected chart's standard Bank account; Kasse mapping is deferred"
  - "Document partner DebtorAccount overrides are resolved per allocation when recording"
  - "Reversal reuses the original journal entry's account numbers and swaps directions through PaymentPostingSource"
  - "Idempotency is keyed by SourceType=Payment and SourceRef=payment.Id"

patterns-established:
  - "Payment settlement and its legal ledger projection share one caller-owned transaction"

completed: 2026-08-03
---

# Phase 10-05: Auto-booking of payments

**Recording an incoming payment now creates a balanced Bank/Forderung entry atomically with its open-item changes; reversal restores the receivable and appends a linked Generalumkehr.**

## Accomplishments

- Injected the scoped posting engine, account resolver and logger into `PaymentService` while retaining the existing direct-construction seam used by integration tests.
- Added an idempotent payment booking inside `RecordAsync`, after open-item mutation and before commit.
- Built one standard Bank debit for the payment total and one Forderung credit per allocation, honoring each document partner's optional debtor-account override.
- Added graceful warning-and-skip behavior when the tenant has no ledger setup.
- Added reversal booking inside `ReverseAsync` with swapped directions, `PostingType.Storno`, and `ReversesEntryId` linked to the original payment entry.
- Added five real-Postgres integration cases for full settlement, multi-item allocation, invalid-allocation atomicity, reversal, and no-ledger compatibility.

## Verification

- `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors with SDK 10.0.301.
- Integration tests were compiled but not run, per reviewer instruction.

## Deviations from Plan

No functional deviations. The reversal resolves its input from the original persisted journal legs so it remains an account-for-account Generalumkehr if partner master data changes later.

## Issues Encountered

None.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
