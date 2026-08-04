---
phase: 12-belege-ausgaben
plan: 01
subsystem: ledger-posting
tags: [dotnet, postgres, ledger, expenses, vat, skr03, skr04]

requires:
  - phase: 10-ledger-foundation
    provides: Expense posting source, SKR account mappings and balanced posting engine
provides:
  - "0%/steuerfrei expense mapping for SKR03 and SKR04 without an input-tax account"
  - "Multi-rate expense breakdown postings with one aggregated creditor leg"
  - "Golden integration coverage for single-rate, zero-rate and multi-rate expense bookings"
affects: [12-05-confirm-book]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-01: Multi-rate and zero-rate expense posting

Plan 12-01 is implemented. Expense posting now accepts frozen per-rate breakdown rows, supports the locked 0%/steuerfrei path, and returns one balanced posting set for a single `PostAsync` call and one `JournalEntry`.

## Implemented

- Extended `SkrMapping.ExpenseMapping` with 0% mappings: SKR03 uses expense account 4980 and SKR04 uses 6300, both with no input-tax account and `Steuerschluessel.None`.
- Preserved the loud `ArgumentOutOfRangeException` fallback for rates other than 0%, 7% and 19%; reverse-charge expense behavior remains out of scope.
- Added an explicit `InvalidOperationException` guard when input-tax resolution is incorrectly requested for a mapping without a Vorsteuer account.
- Added `ExpensePostingBreakdown` and changed `ExpensePostingInput` to carry an `IReadOnlyList` of breakdowns.
- Preserved existing single-rate callers through the requested six-argument secondary constructor.
- Changed `ExpensePostingSource.BuildPostings` to emit one expense leg per breakdown, one input-tax leg only when that row's tax is positive, and one creditor credit for the summed gross amount.
- Added persisted integration-test cases for a two-leg 0% expense and a five-leg 19%+7% expense. The multi-rate case verifies exact golden amounts, total net 300.00, total VAT 33.00, creditor 333.00, balance, and exactly one journal-entry row.

## Files modified

- `src/modules/Numera.Modules.Ledger/Seed/SkrMapping.cs`
- `src/modules/Numera.Modules.Ledger/Posting/AccountResolver.cs`
- `src/modules/Numera.Modules.Ledger/Posting/ExpensePostingSource.cs`
- `tests/Numera.IntegrationTests/LedgerPostingEngineTests.cs`

## Files created

- `.planning/phases/12-belege-ausgaben/12-01-SUMMARY.md`

## Verification

- `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build "src\modules\Numera.Modules.Ledger\Numera.Modules.Ledger.csproj" --configuration Release --no-restore` — passed with 0 warnings and 0 errors.
- `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build "tests\Numera.IntegrationTests\Numera.IntegrationTests.csproj" --configuration Release --no-restore` — passed with 0 warnings and 0 errors.
- Integration tests were not run, per task instruction; the Testcontainers suite remains for the reviewer to execute.

## Deviations and uncertainties

- No implementation deviations from plan 12-01.
- Runtime Postgres/Testcontainers behavior was not exercised because integration-test execution was explicitly excluded. Both the Ledger and integration-test projects compile successfully.
- `PostingEngine` was not changed.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left untouched.
