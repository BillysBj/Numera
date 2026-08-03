---
phase: 10-buchhaltungs-fundament
plan: 03
subsystem: ledger
tags: [ledger, posting-engine, doppik, tdd, skr, gobd]

requires:
  - phase: 10-01
    provides: journal/posting schema and database balance enforcement
  - phase: 10-02
    provides: tenant charts, ChartSeeder and SkrMapping
provides:
  - "SKR-resolved balanced posting sources for invoices, payments and expenses"
  - "Caller-transaction PostingEngine with a pre-save balance guard"
  - "Golden integration coverage for tax rates, reverse charge, payments, expense and Storno"
affects: [invoice-finalize-posting, payment-posting, expense-capture, ledger-reads]

tech-stack:
  added: []
  patterns:
    - "Frozen facts project to non-negative posting legs with explicit debit/credit direction"
    - "Account numbers resolve only through SkrMapping before tenant Account lookup"

key-files:
  created:
    - src/modules/Numera.Modules.Ledger/Posting/AccountResolver.cs
    - src/modules/Numera.Modules.Ledger/Posting/InvoicePostingSource.cs
    - src/modules/Numera.Modules.Ledger/Posting/PaymentPostingSource.cs
    - src/modules/Numera.Modules.Ledger/Posting/ExpensePostingSource.cs
    - src/modules/Numera.Modules.Ledger/Posting/PostingEngine.cs
    - tests/Numera.IntegrationTests/LedgerPostingEngineTests.cs
  modified:
    - src/Numera.Api/Program.cs

key-decisions:
  - "Revenue has no override seam; it always resolves through SkrMapping"
  - "Invoice Storno reuses the same frozen input and explicitly reverses every posting direction"
  - "PostingEngine performs one SaveChanges call and never creates its own transaction"
  - "Missing mapped accounts and conflicting Automatikkonto keys have dedicated clear exceptions"

patterns-established:
  - "Caller supplies the journal header; posting sources supply balanced account legs"
  - "Domain balance equality mirrors the deferred PostgreSQL constraint before persistence"

completed: 2026-08-03
---

# Phase 10-03: Posting engine

**Invoices, payments and expenses now project into SKR-resolved balanced Soll/Haben sets, with Generalumkehr and pre-save correctness guards.**

## Accomplishments

- Added a tenant-aware resolver for revenue, output tax, expense, input tax and standard accounts, with document-level debtor/creditor/expense/bank overrides where reachable.
- Added invoice posting for 19%, 7%, mixed VAT, exempt/reverse-charge and Kleinunternehmer facts using frozen breakdown amounts.
- Added multi-allocation payment and reversal posting plus the ACCT-05 expense/Vorsteuer rule.
- Added Generalumkehr support through direction reversal while the journal header records Storno and its original entry.
- Added PostingEngine with exact Soll/Haben equality, tenant consistency and one-save persistence on the ambient transaction.
- Added eight required golden cases and three explicit guard tests for imbalance, missing K account and Automatikkonto key conflict.

## Verification

- `dotnet build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors with SDK 10.0.301.
- Static checks confirm production posting code contains no four-digit account literals, all resolver paths use SkrMapping, ResolveRevenue has no override parameter, and PostingEngine opens no transaction.
- Integration tests were compiled but not run, per reviewer instruction.

## Deviations from Plan

No functional deviations. The suite includes three additional negative-path tests beyond the eight required golden cases. Payment and expense DTOs retain the exact requested shapes; tenant and chart variant are supplied to their posting-source constructors because those DTOs intentionally do not carry them.

## Issues Encountered

- The existing synchronous `IPostingSource.BuildPostings()` contract requires synchronous tenant-account lookups during source construction/building. No interface change was made in this plan.
- TaxCategory K intentionally resolves to 8125/4125 and raises the clear missing-account exception because those accounts are outside the locked 13-account starter charts.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
