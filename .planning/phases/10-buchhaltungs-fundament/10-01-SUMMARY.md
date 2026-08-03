---
phase: 10-buchhaltungs-fundament
plan: 01
subsystem: ledger
tags: [ledger, gobd, postgres, rls, ef-core]

requires:
  - phase: 01-platform-fundament
    provides: tenant RLS and inert ledger tables
provides:
  - "Tenant ledger settings and lockable fiscal periods"
  - "GoBD append-only journal and postings with one-shot Festschreibung"
  - "Deferred balanced-posting and fiscal-period-lock database enforcement"
affects: [account-seeding, auto-posting, ledger-reads, ustva, reports]

tech-stack:
  added: []
  patterns:
    - "Reflective ledger mapping without a Platform.Db-to-Ledger reference"
    - "PostgreSQL RLS plus REVOKE and trigger-based GoBD enforcement"

key-files:
  created:
    - src/modules/Numera.Modules.Ledger/Steuerschluessel.cs
    - src/modules/Numera.Modules.Ledger/LedgerSettings.cs
    - src/modules/Numera.Modules.Ledger/FiscalPeriod.cs
    - src/platform/Numera.Platform.Db/Migrations/20260803141314_LedgerEngine.cs
    - src/platform/Numera.Platform.Db/Migrations/20260803141314_LedgerEngine.Designer.cs
  modified:
    - src/modules/Numera.Modules.Ledger/Account.cs
    - src/modules/Numera.Modules.Ledger/JournalEntry.cs
    - src/modules/Numera.Modules.Ledger/Posting.cs
    - src/modules/Numera.Modules.Ledger/Numera.Modules.Ledger.csproj
    - src/platform/Numera.Platform.Db/NumeraDbContext.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs

key-decisions:
  - "Existing Account rows default to SKR03 so the new non-null chart_variant column is additive"
  - "Period and reversal links use restrictive foreign keys"
  - "Only journal_number and festgeschrieben_at may be stamped once; all posting rows are fully append-only"

patterns-established:
  - "Ledger migrations layer handwritten RLS and legal invariants after EF-generated schema operations"

completed: 2026-08-03
---

# Phase 10-01: Buchhaltungs-Fundament

**The inert ledger schema is now a tenant-isolated, GoBD-enforced double-entry foundation with tax metadata, fiscal-period locking, and deferred balance validation.**

## Accomplishments

- Extended accounts, journal entries, and posting legs with SKR, DATEV BU, source, reversal, Festschreibung, and VAT metadata.
- Added one-per-tenant ledger settings and unique tenant/year/month fiscal periods.
- Added the journal and account read-path indexes plus the required uniqueness constraints.
- Added RLS to both new tables and database enforcement for immutable postings, one-shot journal Festschreibung, balanced entries, and locked periods.
- Generated the EF migration, designer, and updated model snapshot.

## Verification

- `dotnet build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors with SDK 10.0.301.
- `dotnet ef migrations has-pending-model-changes`: no model changes pending.
- Generated the LedgerEngine SQL script without connecting to Postgres and confirmed both RLS policies and all four required triggers are present.
- Did not run `dotnet ef database update`; database application and integration tests remain for reviewer execution.

## Deviations from Plan

No implementation deviations. The Ledger project gained the necessary direct reference to `Numera.Platform.Money` for `TaxCategory`. Verification used Release output and `--no-restore` because a running API process locked Debug assemblies and the sandbox could not read the user-profile NuGet configuration.

## Issues Encountered

- EF tools 10.0.3 reported an informational patch-version warning against runtime 10.0.10.
- The active Debug API process was left untouched; Release output provided an isolated build and EF design-time host.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
