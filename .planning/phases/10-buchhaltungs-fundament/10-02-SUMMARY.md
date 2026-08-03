---
phase: 10-buchhaltungs-fundament
plan: 02
subsystem: ledger
tags: [ledger, skr03, skr04, seeding, rls, setup]

requires:
  - phase: 10-01
    provides: ledger entities, settings table, account metadata and tenant RLS
provides:
  - "Embedded 13-account SKR03 and SKR04 starter charts"
  - "Pure EN-16931 tax-category and standard-account SKR mapping"
  - "Owner-gated atomic ledger setup and authenticated settings read endpoints"
  - "Least-privilege integration coverage for setup, idempotency, variants and RLS"
affects: [auto-posting, ledger-reads, ustva, reports]

tech-stack:
  added: []
  patterns:
    - "Embedded JSON chart resources materialized in the caller's tenant transaction"
    - "Pure static SKR mapping shared by setup and posting engines"

key-files:
  created:
    - src/modules/Numera.Modules.Ledger/Seed/skr03.accounts.json
    - src/modules/Numera.Modules.Ledger/Seed/skr04.accounts.json
    - src/modules/Numera.Modules.Ledger/Seed/SkrMapping.cs
    - src/modules/Numera.Modules.Ledger/Seed/ChartSeeder.cs
    - src/Numera.Api/Contracts/LedgerContracts.cs
    - src/Numera.Api/Endpoints/LedgerSetupEndpoints.cs
    - tests/Numera.IntegrationTests/LedgerSetupTests.cs
  modified:
    - src/modules/Numera.Modules.Ledger/Numera.Modules.Ledger.csproj
    - src/Numera.Api/Program.cs

key-decisions:
  - "Only POST /api/ledger/setup is owner-gated; authenticated members may read settings"
  - "ChartSeeder owns the single SaveChanges call while the endpoint transaction includes settings, accounts and audit"
  - "A pre-existing chart without settings is treated as a setup conflict rather than claiming a possibly mismatched variant"

patterns-established:
  - "One-time tenant setup uses an application guard plus the database uniqueness constraint as a concurrency backstop"

completed: 2026-08-03
---

# Phase 10-02: SKR setup and tenant chart seeding

**A tenant can now select SKR03 or SKR04 once and atomically receive ledger settings plus a tenant-isolated active chart of accounts.**

## Accomplishments

- Added curated, embedded 13-account SKR03 and SKR04 charts.
- Added pure mappings for revenue/VAT, expense/input-tax and standard debtor, creditor, bank and cash accounts.
- Added an idempotent chart seeder that relies on the caller's tenant context and performs one save.
- Added owner-gated `POST /api/ledger/setup`, authenticated `GET /api/ledger/settings`, atomic audit recording and concurrent-setup conflict handling.
- Added four least-privilege integration cases covering both variants, duplicate setup and cross-tenant RLS.

## Verification

- `dotnet build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors with SDK 10.0.301.
- Parsed both JSON resources: 13 unique accounts in SKR03 and 13 unique accounts in SKR04.
- Inspected the built Ledger assembly and confirmed both JSON files are embedded under the resource names used by ChartSeeder.
- Integration tests were authored but not run, per reviewer instruction.

## Deviations from Plan

No functional deviations. A typed `LedgerSetupResponse` was added alongside the two required contracts so the 201 response exposes settings and account count explicitly. The mapping includes a 7% expense path using the verified input-tax accounts in addition to the plan's explicit 19% example.

## Issues Encountered

- The locked 13-account seed lists do not include the research-mandated intra-community revenue accounts 8125/4125. `SkrMapping` returns 8125/4125 for TaxCategory K exactly as required; a later posting plan must either extend the materialized chart or explicitly resolve that seam before booking K documents.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
