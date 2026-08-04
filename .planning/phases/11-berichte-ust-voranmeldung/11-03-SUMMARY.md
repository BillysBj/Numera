---
phase: 11-berichte-ust-voranmeldung
plan: 03
subsystem: reporting
tags: [euer, cash-basis, postgres, rls, kleinunternehmer]

requires:
  - phase: 11-01
    provides: payment-date cash recognition rows from frozen invoice tax breakdowns
  - phase: 10-04
    provides: SKR03/SKR04 account setup and frozen invoice postings
  - phase: 10-05
    provides: payment allocations and signed payment reversals
provides:
  - "Checked-in SKR03/SKR04 account-to-Anlage-EÜR line map"
  - "Cash-basis EÜR calculator using the shared payment-date recognition read"
  - "Regelunternehmer net/VAT split and Kleinunternehmer gross presentation"
  - "Explicit Phase-12 expense-incompleteness caveat with present zero lines"
  - "Compile-verified real-Postgres integration scenarios for all six requested behaviors"
affects: [euer-screen, euer-print, reporting-endpoints]

tech-stack:
  added: []
  patterns:
    - "Checked-in per-chart account-to-report-line mapping"
    - "Payment.ValueDate cash recognition shared with Ist-USt-VA"
    - "Aggregate decimals first and apply RoundingPolicy only at report boundaries"

key-files:
  created:
    - src/Numera.Api/Reporting/EuerLineMap.cs
    - src/Numera.Api/Reporting/EuerModels.cs
    - src/Numera.Api/Reporting/EuerCalculator.cs
    - tests/Numera.IntegrationTests/EuerCalculatorTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-03-SUMMARY.md
  modified: []

key-decisions:
  - "Revenue is recognized exclusively through RecognitionReader.ReadCashRecognitionAsync; accrual and payment journal postings are never summed together"
  - "Regelunternehmer income is net revenue plus Zeile 17 collected VAT; Kleinunternehmer receipts are gross with no Zeile 17"
  - "Expense mappings remain visible at zero, omit the separate paid-input-VAT line for Kleinunternehmer, and always carry the Phase-12 incompleteness warning"
  - "AfA/Abschreibungen is documented as deferred because durable assets require depreciation rather than payment-date expensing"

completed: 2026-08-04
---

# Phase 11-03: EÜR calculator

**The EÜR calculator now produces an Anlage-EÜR-shaped cash-basis report from real payment allocations, with correct Regel-/Kleinunternehmer presentation and an explicit warning that Phase-12 expense capture is not yet available.**

## Accomplishments

- Added checked-in SKR03/SKR04 mappings for taxable revenue (`8400`/`8300`, `4400`/`4300`), tax-free/non-taxable revenue (`8200`/`8125`, `4200`/`4125`), collected VAT, operating expenses, paid input VAT, and VAT paid to the tax office.
- Included seeded SKR04 account `5400` as the counterpart to SKR03 `3400`, in addition to the explicitly requested `6300` mapping.
- Added the exact requested `EuerLine` and `EuerReport` records.
- Implemented `EuerCalculator.ComputeAsync(int jahr, DateOnly from, DateOnly to, CancellationToken ct)` over `NumeraDbContext` and `RecognitionReader`.
- Recognized income only from `ReadCashRecognitionAsync`, so `Payment.ValueDate` controls Zufluss and invoice plus payment postings cannot double-count revenue.
- Aggregated recognized net amounts by mapped revenue group and collected VAT into Zeile 17 for Regelunternehmer.
- Presented Kleinunternehmer receipts gross without Zeile 17 and omitted the separate paid-input-VAT expense line.
- Kept all currently supported expense account groups present at zero without inventing supplier-payment facts.
- Set `IsExpenseDataIncomplete = true` with `Betriebsausgaben unvollständig — Belegerfassung ab Phase 12.` on every current report.
- Applied `RoundingPolicy.RoundAmount` only after decimal aggregation and for final sums/profit.
- Documented AfA/Abschreibungen as explicitly out of scope and deferred.
- Added six real-Postgres integration scenarios covering payment timing, no double-count, Regel split, Kleinunternehmer gross handling, profit arithmetic, and the expense-incompleteness caveat.

## Verification

- Exact build command: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build Numera.sln --configuration Release --no-restore`.
- Result: successful full-solution Release build, including `Numera.Api` and `Numera.IntegrationTests`; **0 warnings, 0 errors**.
- Integration tests were compiled but not run, per explicit instruction.
- Protected `UstVa*.cs` files and both SKR seed JSON files have no diff.
- No files were staged or committed.

## Deviations and uncertainties

- No behavioral deviation from plan 11-03 or locked D2/NO-FABRICATION decisions.
- The only additive map coverage beyond the account list in the task is seeded SKR04 goods-expense account `5400`, paired with SKR03 `3400`; it does not create or fabricate runtime expense amounts.
- Runtime Testcontainers execution remains for reviewer-run verification because integration tests were explicitly not to be run in this task.
- Expense account attribution, supplier-payment cash recognition, and transition of `IsExpenseDataIncomplete` to `false` remain Phase-12 work by design.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-04*
