---
phase: 13-banking-zahlungsabgleich
plan: 02
subsystem: reconciliation-scoring
tags: [dotnet, ef-core, postgres, rls, banking, receivables, reconciliation]

requires:
  - phase: 13-01
    provides: Tenant-scoped normalized BankTransaction model with signed decimal amounts
  - phase: 06
    provides: OpenItem receivables and payment settlement lifecycle
provides:
  - "Ranked receivable match proposals with decimal 0..1 confidence scores"
  - "High/Review queue tiers that never authorize automatic booking"
  - "Pre-scoring candidate filtering by exact open amount or extracted invoice reference"
affects: [bank-review-queue, reconciliation-confirmation, banking-api]

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-05
---

# Phase 13-02: Reconciliation scorer

The Banking module now ranks incoming transactions against tenant-visible open receivables. Candidate
selection happens in PostgreSQL before scoring, partner data is loaded in one additional bounded
query, and every result is only a proposal. The scorer does not create a Payment, mutate an OpenItem,
or clear a SalesDocument; outgoing transactions return no receivable candidates.

## Implemented

- Added `MatchCandidate` with the required open-item identity, document number, open and suggested
  allocation amounts, decimal score, tier, and German reason list.
- Added `MatchTier.High` and `MatchTier.Review`; `High` is assigned only when both amount and document
  reference are exact and still has no booking semantics.
- Added a stateless `ReconciliationScorer` over `NumeraDbContext` with the D2 guard for signed amounts
  (`Amount <= 0` returns an empty list).
- Extracts prefixed German invoice references (`RE`, `RECHNUNG`, `RG`, `R`, `INV`) with common
  separators, plus bare digit runs, from both `Purpose` and `EndToEndId` before querying.
- Filters open receivables in the database to status Open/PartiallyPaid and exact `OpenAmount` or an
  extracted exact `DocumentNumber`, avoiding an all-open-items in-memory scan.
- Scores exact reference at 0.50, exact amount at 0.35 (partial amount at 0.20), exact normalized IBAN
  at 0.15, and case/whitespace-insensitive containing name at 0.08, capped at 1.00.
- Uses `Min(tx.Amount, OpenAmount)` for suggested allocation, so partial payments propose only the
  transaction amount and overpayments never allocate more than the receivable remains open for.
- Loads the candidate partner IDs in one query and orders proposals by descending score with a stable
  document-number tie-breaker.
- Added one fixture-based Postgres test covering the four requested scenarios: exact high-confidence
  match, partial Review proposal, unrelated empty result, and outgoing empty result. It re-reads the
  OpenItem and SalesDocument to prove the scorer did not clear or mutate either record.

## Verified OpenItem and partner field shape

- `OpenItem.Status` is `OpenItemStatus`; the real enum values are `Open = 0`,
  `PartiallyPaid = 1`, `Paid = 2`, and `Cancelled = 3`. Only the first two are candidates.
- `OpenItem.OpenAmount` is `decimal` with precision `(19,4)` and is the remaining receivable amount
  used for filtering, amount scoring, and the allocation cap.
- `OpenItem.DocumentNumber` is the required denormalized `string` matched against extracted bank
  references.
- `OpenItem.PartnerId` is the nullable debtor link used by the scorer to load `BusinessPartner.Id`,
  `BusinessPartner.Iban`, and `BusinessPartner.Name`.
- `OpenItem.DocumentId` points to the finalized `SalesDocument.Id`; the document independently carries
  nullable `SalesDocument.PartnerId`. No OpenItem navigation property exists, so the scorer uses the
  denormalized `OpenItem.PartnerId` requested by the plan.

## Files created

- `src/modules/Numera.Modules.Banking/Reconciliation/MatchCandidate.cs`
- `src/modules/Numera.Modules.Banking/Reconciliation/ReconciliationScorer.cs`
- `tests/Numera.IntegrationTests/ReconciliationScorerTests.cs`
- `.planning/phases/13-banking-zahlungsabgleich/13-02-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Full `Numera.sln` build with `--configuration Release --no-restore`: passed with 0 warnings and
  0 errors.
- Direct `Numera.IntegrationTests.csproj` build with `--configuration Release --no-restore`: passed
  with 0 warnings and 0 errors, including a true Release Banking assembly output.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- The existing Banking project has no compile-time project references to Sales or CRM, while plan
  ownership explicitly excludes editing `Numera.Modules.Banking.csproj`. The scorer therefore uses
  parameterized `NumeraDbContext.Database.SqlQuery` projections over the mapped `open_items` and
  `partners` tables. The query uses the verified real column names and enum ordinals, executes under
  the context-opened tenant connection, and remains protected by PostgreSQL RLS. This preserves the
  two bounded queries and all required behavior without crossing the ownership boundary.
- The solution's Release configuration currently maps the Banking project to a Debug output path.
  A second direct Release build of the integration-test project forced Banking and the test assembly
  through Release and succeeded.
- Runtime SQL parameter-array behavior and assertions remain for the reviewer-run Testcontainers
  suite because integration tests were explicitly not executed here.
- No Import file, Program.cs, DI registration, migration, or project file was touched. No file was
  staged or committed; the pre-existing untracked `.claude/` directory was left untouched.
