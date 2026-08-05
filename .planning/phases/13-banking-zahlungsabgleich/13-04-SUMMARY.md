---
phase: 13-banking-zahlungsabgleich
plan: 04
subsystem: banking
tags: [hangfire, postgres, rls, sha256, idempotency]

requires:
  - phase: 13-banking-zahlungsabgleich
    provides: Banking entities, provider port, tenant/account/dedupe unique index
provides:
  - Shared idempotent transaction ingest for provider sync and file imports
  - Tenant-scoped incremental Hangfire sync with cursor advancement
  - Hourly configurable, staggered connected-account fan-out
  - Fake provider and Postgres integration coverage for dedupe and RLS
affects: [13-05-finapi, 13-06-reconciliation, bank-statement-import]

tech-stack:
  added: []
  patterns:
    - Provider-id-or-canonical-SHA256 dedupe guarded by a tenant/account unique index
    - SetTenant before resolving NumeraDbContext in background jobs
    - Tenant-agnostic ID discovery followed by tenant-scoped Hangfire jobs

key-files:
  created:
    - src/modules/Numera.Modules.Banking/BankTransactionIngestService.cs
    - src/Numera.Api/Jobs/SyncBankTransactionsJob.cs
    - tests/Numera.IntegrationTests/BankTransactionSyncTests.cs
    - tests/Numera.IntegrationTests/Fixtures/FakeBankConnectionProvider.cs
  modified:
    - src/Numera.Api/Services/BankingModuleServiceCollectionExtensions.cs
    - src/Numera.Worker/Program.cs

key-decisions:
  - "Provider IDs are used directly; provider-less drafts use length-prefixed canonical fields and lowercase SHA-256 hex."
  - "The incremental cursor is the maximum observed value date in ISO format, intentionally allowing a one-day overlap protected by dedupe."
  - "The recurring fan-out reads only tenant/account IDs through the Hangfire connection and staggers scheduled account jobs by five seconds by default."

patterns-established:
  - "Shared ingest: provider sync and file imports call the same BankTransactionIngestService overloads."
  - "Race-safe insert: optimistic pre-check plus the unique index, with unique-conflict retry preserving unrelated batch rows."

duration: 30min
completed: 2026-08-05
---

# Phase 13 Plan 04: Idempotent Transaction Sync Summary

**Tenant-scoped bank transaction synchronization now re-fetches overlapping provider windows without duplicates and advances an incremental cursor through one shared ingest path.**

## Performance

- **Duration:** 30 min
- **Completed:** 2026-08-05T15:51:21+02:00
- **Tasks:** 3
- **Files modified:** 7 (including this summary)

## Accomplishments

- Added provider-ID/content-fingerprint deduplication with batched persistence and unique-index race recovery.
- Added the worker-queue sync job with RLS-first tenant setup, consent-safe handling, cursor advancement, and successful-sync timestamps.
- Added configurable hourly fan-out with per-account scheduling and rate-limit staggering.
- Added a fully scripted fake provider and real-Postgres test source for signed decimals, repeated windows, new transactions, cursor progression, and cross-tenant RLS.

## Task Commits

No commits were created; the user explicitly requested an unstaged, uncommitted worktree.

## Files Created/Modified

- `src/modules/Numera.Modules.Banking/BankTransactionIngestService.cs` - Shared async/enumerable ingest, canonical fingerprinting, and race-safe dedupe.
- `src/Numera.Api/Jobs/SyncBankTransactionsJob.cs` - Tenant sync plus staggered connected-account fan-out.
- `src/Numera.Api/Services/BankingModuleServiceCollectionExtensions.cs` - Scoped ingest and transient Hangfire job registrations.
- `src/Numera.Worker/Program.cs` - Configurable recurring banking fan-out registration.
- `tests/Numera.IntegrationTests/Fixtures/FakeBankConnectionProvider.cs` - Network-free scripted provider.
- `tests/Numera.IntegrationTests/BankTransactionSyncTests.cs` - Postgres idempotency, cursor, decimal-sign, and RLS coverage.
- `.planning/phases/13-banking-zahlungsabgleich/13-04-SUMMARY.md` - Plan execution record.

## Decisions Made

- Fingerprints length-prefix each canonical UTF-8 field, avoiding ambiguous concatenations; purpose whitespace/case and IBAN whitespace/case are normalized.
- The cursor stores the maximum observed value date (`yyyy-MM-dd`). Fetching that date again is deliberate because the dedupe key and unique index make overlaps harmless.
- Fan-out uses the existing Hangfire connection for a tenant-agnostic ID-only query, then places tenant and account IDs into separately staggered jobs where `SetTenant` happens before DbContext resolution.

## Deviations from Plan

None - all three tasks were implemented in the specified owned files. No scoring, matching, finAPI implementation, package changes, or API startup edits were added.

## Issues Encountered

- `rg` was unavailable in the execution environment; repository discovery used PowerShell and `git` with a per-command safe-directory override.
- The repository is owned by a different local Windows identity, so the same non-mutating `git -c safe.directory=...` override was used for status and diff checks. No Git configuration was changed.

## Verification

- `dotnet build Numera.sln --configuration Release --no-restore`: **passed with 0 errors and 0 warnings**.
- `dotnet build src/modules/Numera.Modules.Banking/Numera.Modules.Banking.csproj --configuration Release --no-restore`: **passed with 0 errors and 0 warnings** (explicit module verification because the solution log referenced a cached Debug output path for this project).
- Integration tests were **not run**, per user instruction; their source compiled as part of the solution build.
- No files under `src/Numera.Api/Program.cs` or `FinApi/*` were touched.
- No files were staged or committed.

## User Setup Required

None for this plan. The worker's `ConnectionStrings:Hangfire` identity must be permitted to perform the tenant-agnostic connected-account ID query; the supplied local configuration uses the Postgres bootstrap/Hangfire identity.

## Next Phase Readiness

- Plan 13-05 can supply the live provider behind `IBankConnectionProvider`; the sync loop is provider-neutral.
- Plan 13-06 can score only newly stored `Unmatched` rows without changing ingest.
- The Postgres integration tests remain to be executed by the reviewer as requested.

---
*Phase: 13-banking-zahlungsabgleich*
*Completed: 2026-08-05*
