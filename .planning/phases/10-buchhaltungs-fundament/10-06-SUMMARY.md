---
phase: 10-buchhaltungs-fundament
plan: 06
subsystem: ledger
tags: [ledger, journal, account-statement, festschreibung, gobd]

requires:
  - phase: 10-04
    provides: atomic invoice journal entries and Storno reversals
  - phase: 10-05
    provides: atomic payment journal entries and reversals
  - phase: 10-01
    provides: journal immutability whitelist and period-lock trigger
provides:
  - "RLS-scoped, keyset-paginated Buchungsjournal"
  - "Per-account Kontoauszug with natural-side running balance"
  - "Gapless per-calendar-year Festschreibung and permanent period locks"
affects: [ledger-ui, reports, exports, compliance]

tech-stack:
  added: []
  patterns:
    - "PostgreSQL window SUM for account running balances"
    - "UUIDv7 cursor plus ordering metadata for stable keyset pages"
    - "Transaction-scoped advisory lock serializes fiscal-year number allocation"

key-files:
  created:
    - src/Numera.Api/Endpoints/LedgerEndpoints.cs
    - src/Numera.Api/Services/FestschreibungService.cs
    - tests/Numera.IntegrationTests/LedgerJournalReadTests.cs
    - tests/Numera.IntegrationTests/FestschreibungTests.cs
  modified:
    - src/Numera.Api/Contracts/LedgerContracts.cs
    - src/Numera.Api/Program.cs

key-decisions:
  - "Journal numbers are assigned only at Festschreibung and continue the calendar year's current maximum"
  - "Only journal_number, festgeschrieben_at and period_id are changed on an entry during the one-shot stamp"
  - "Calendar year/month remains the MVP fiscal-period model; FiscalYearStartMonth is deferred"
  - "There is no unlock endpoint"

patterns-established:
  - "Locked prior periods supply the Kontoauszug opening balance; the requested range uses a windowed running sum"
  - "Owner-only period mutation, authenticated RLS-scoped ledger reads"

completed: 2026-08-03
---

# Phase 10-06: Journal reads and Festschreibung

**The ledger now exposes a Buchungsjournal and per-account Kontoauszug, while owner-triggered Festschreibung assigns gapless annual numbers and permanently closes a calendar month to new bookings.**

## Accomplishments

- Added journal and account-statement contracts plus a reusable UUID cursor envelope.
- Added authenticated journal reads with debit/credit aggregation, optional date/source filters, deterministic locked/unlocked ordering and stable keyset pagination.
- Added Kontoauszug reads with counteraccounts, natural-side signed movement, locked-period opening balance and PostgreSQL windowed running balance.
- Added owner-only period locking with a typed conflict result and no unlock path.
- Serialized same-tenant/year locks with a transaction-scoped advisory lock, continued the year's maximum suffix and stamped entries in `(entry_date, id)` order.
- Limited each entry's one-shot EF update to `JournalNumber`, `FestgeschriebenAt` and `PeriodId`, matching the plan-01 whitelist trigger.
- Added real-Postgres coverage for journal/statement reads, keyset stability, RLS, gapless stamping, locked-period rollback, repeated-lock conflict and entry immutability.

## Verification

- `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors with SDK 10.0.301.
- Integration tests were compiled but not run, per reviewer instruction.
- The plan-01 read indexes already exist (`journal_entries(tenant_id, entry_date)` and `postings(account_id)`), so no migration change was required.

## Deviations from Plan

No functional deviations. A transaction-scoped advisory lock was added to preserve gapless annual allocation under concurrent locks; it is not a sequence and releases automatically with the transaction.

## Issues Encountered

None.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
