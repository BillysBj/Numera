---
phase: 11-berichte-ust-voranmeldung
plan: 02
subsystem: reporting
tags: [ust-va, vat, postgres, rls, soll, ist]

requires:
  - phase: 11-01
    provides: Soll and payment-date recognition reads
  - phase: 10-04
    provides: frozen invoice postings and tax breakdowns
  - phase: 10-05
    provides: signed payment-allocation reversals
provides:
  - "Fiscal-year-versioned USt-VA map containing only Kz 81, 86, 41 and 83"
  - "Soll/Ist USt-VA calculator with signed Storno netting and Kleinunternehmer gate"
  - "Seeded SKR03/SKR04 intra-community revenue accounts for Kz 41"
  - "Compile-verified real-Postgres integration scenarios for all six requested behaviors"
affects: [ust-va-preview, ust-va-export, reporting-drilldown]

tech-stack:
  added: []
  patterns:
    - "Versioned additive Kennziffer map; unsupported Kz are absent"
    - "Soll direction netting versus already-signed Ist pro-rata recognition"
    - "Whole-euro conversion only at the report-model boundary"

key-files:
  created:
    - src/Numera.Api/Reporting/UstVaKennzifferMap.cs
    - src/Numera.Api/Reporting/UstVaModels.cs
    - src/Numera.Api/Reporting/UstVaCalculator.cs
    - tests/Numera.IntegrationTests/UstVaCalculatorTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-02-SUMMARY.md
  modified:
    - src/modules/Numera.Modules.Ledger/Seed/skr03.accounts.json
    - src/modules/Numera.Modules.Ledger/Seed/skr04.accounts.json

key-decisions:
  - "Kz 83 is output VAT only: RoundAmount(untruncated Kz81*0.19 + untruncated Kz86*0.07)"
  - "Soll bases are Credit minus Debit; Ist bases directly sum signed NetAmount"
  - "A quarter is festgeschrieben only when all three constituent FiscalPeriod rows are Locked"
  - "The line model exposes contributing account numbers as the Plan-06 drill-down seam"

completed: 2026-08-04
---

# Phase 11-02: USt-VA Kennziffer map and calculator

**The USt-VA calculator now produces only Kz 81, 86, 41 and computed 83 from real tenant-scoped recognition data, supports both Besteuerungsarten, preserves cross-period reversals, and gates Kleinunternehmer entirely.**

## Accomplishments

- Added a fiscal-year-versioned map effective from 2026 with exactly the producible set `{81, 86, 41, 83}`.
- Added the requested `UstVaLine` and `UstVaReport` records, including contributing account numbers per line as a drill-down seam.
- Added ELSTER period-code mapping for months `01`-`12` and quarters `41`-`44`.
- Implemented Soll recognition as signed natural-direction netting (`Credit - Debit`) so a later-period Storno reduces that later period.
- Implemented Ist recognition from payment-date rows by summing the already-signed `NetAmount`.
- Kept untruncated decimal bases through aggregation and Kz 83 calculation; base lines use `decimal.Floor` only at the whole-euro output boundary, including for legitimate negative periods.
- Computed Kz 83 with `RoundingPolicy.RoundAmount` and attached `Ohne Vorsteuerabzug (Belegerfassung ab Phase 12).`
- Returned no VAT lines for `CompanyProfile.IsKleinunternehmer`.
- Treated a month as festgeschrieben when its `FiscalPeriod` is locked and a quarter as festgeschrieben only when all three months are locked.
- Added six integration-test scenarios: Soll golden values, Ist month attribution, Kz 41 on both chart variants, cross-period Storno, Kleinunternehmer gating, and omitted-Kz absence.

## Seed additions

- SKR03 account `8125`: `Steuerfreie innergemeinschaftliche Lieferungen`, `Revenue`, non-Automatikkonto, `steuerschluessel: 1` (`TaxFreeWithInput`), `ustvaKennziffer: "41"`.
- SKR04 account `4125`: same metadata and Kz 41 mapping.
- Both JSON resources parsed successfully with PowerShell `ConvertFrom-Json` after modification.

## Verification

- SDK: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" --version` returned `10.0.301`.
- Exact build command: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build --configuration Release --no-restore`.
- Result: successful full-repository build, including `Numera.Api` and `Numera.IntegrationTests`; **0 warnings, 0 errors**.
- Integration tests were compiled but not run, per explicit instruction.
- No files were staged or committed.

## Deviations and uncertainties

- No implementation deviation from plan 11-02 or locked D1/D3/NO-FABRICATION behavior.
- No `Euer*.cs`, DI registration, migrations, frozen Phase-10 entities, or posting-engine files were changed.
- Runtime Testcontainers execution remains for reviewer-run verification because integration tests were explicitly not to be run in this task.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-04*
