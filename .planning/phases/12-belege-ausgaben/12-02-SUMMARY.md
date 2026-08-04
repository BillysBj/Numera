---
phase: 12-belege-ausgaben
plan: 02
subsystem: receipt-archive
tags: [dotnet, ef-core, postgres, rls, gobd, worm, receipts, expenses]

requires:
  - phase: 12-belege-ausgaben
    plan: 01
    provides: Multi-rate and zero-rate ExpensePostingInput breakdowns
provides:
  - "Converged tenant-scoped receipt aggregate for camera, upload, email, and e-invoice intake"
  - "Ten-year GoBD receipt archive with RLS, privilege revocation, and trigger-enforced WORM storage"
  - "Exact-hash and supplier/invoice/gross/date duplicate verdicts"
  - "One Receipt-to-ExpensePostingInput mapping with one breakdown row per VAT rate"
affects: [12-03-extraction, 12-04-ingest, 12-05-confirm-book, 12-06-email-intake]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-02: Receipt aggregate and GoBD WORM archive

Plan 12-02 is implemented. All capture sources can converge on one receipt state machine, Tier-B
originals have a tenant-isolated append-only archive, duplicate intake is surfaced for review, and
confirmed single- or multi-rate facts map to one expense posting input.

## Implemented

- Added `ReceiptStatus` (`Captured`, `Extracted`, `Reviewed`, `Booked`, `Duplicate`, `Rejected`,
  `Quarantined`) and `ReceiptSource` (`Camera`, `Upload`, `Email`, `EInvoice`).
- Added the self-describing `Receipt` `ITenantEntity` with UUIDv7 identity, status/content-hash
  indexes, Tier-A/Tier-B provenance links, extracted and confirmed fields, jsonb confidence,
  partner/account/journal links, and review audit fields. It explicitly documents the human review
  gate: extraction is never auto-booked.
- Added the self-describing `ReceiptArchive` `ITenantEntity` with init-only original bytes and
  metadata, SHA-256 content hash, receipt provenance, source/upload metadata, and documented GoBD
  ten-year append-only retention.
- Added `ReceiptDeduplicator`: lowercase SHA-256 hex hashing, exact content-hash detection before
  business-key detection, rejection exclusion, and explicit `None`, `Exact`, and
  `SuspectedBusinessKey` verdicts. It never deletes or suppresses an incoming item.
- Added `ReceiptBookingProposal.Build`: an empty external breakdown maps the receipt's confirmed
  single-rate net/VAT/rate; a supplied e-invoice breakdown maps one row per VAT rate. Both paths
  return one `ExpensePostingInput`, using the override/creditor accounts and expense-or-invoice date.
- Added the required Sales-to-Ledger project reference for the concrete `ExpensePostingInput` API.
- Generated migration `20260804152146_Receipts` in the shared Db project and retained the generated
  designer and EF-updated `NumeraDbContextModelSnapshot`.
- Added RLS-only state-machine protection to mutable `receipt`; no update/delete privilege is
  revoked from this table.
- Added full RLS plus guarded `REVOKE UPDATE, DELETE`, `GRANT INSERT, SELECT`, and a database
  immutability trigger to `receipt_archive`, with symmetric cleanup/re-grant in `Down`.
- Added real-Postgres integration coverage for RLS reads, RLS `WITH CHECK`, archive byte updates and
  deletes, and both duplicate verdicts. Added both new entity types to the standing cross-tenant
  isolation completeness suite for read and write proofs.

## WORM trigger SQL

```sql
CREATE FUNCTION receipt_archive_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'receipt_archive rows are append-only (GoBD); archive a new original, do not edit/delete';
END;
$$;

CREATE TRIGGER receipt_archive_immutable
BEFORE UPDATE OR DELETE ON receipt_archive
FOR EACH ROW EXECUTE FUNCTION receipt_archive_immutable();
```

The migration also executes the role-guarded privilege layer:

```sql
REVOKE UPDATE, DELETE ON receipt_archive FROM numera_app;
GRANT INSERT, SELECT ON receipt_archive TO numera_app;
```

## Files created

- `src/modules/Numera.Modules.Sales/Belege/ReceiptStatus.cs`
- `src/modules/Numera.Modules.Sales/Belege/Receipt.cs`
- `src/modules/Numera.Modules.Sales/Belege/ReceiptArchive.cs`
- `src/modules/Numera.Modules.Sales/Belege/ReceiptDeduplicator.cs`
- `src/modules/Numera.Modules.Sales/Belege/ReceiptBookingProposal.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260804152146_Receipts.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260804152146_Receipts.Designer.cs`
- `tests/Numera.IntegrationTests/ReceiptArchiveRlsTests.cs`
- `.planning/phases/12-belege-ausgaben/12-02-SUMMARY.md`

## Files modified

- `src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj`
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs`
- `tests/Numera.IntegrationTests/CrossTenantIsolationCompletenessTests.cs`

## Verification

- SDK check: `10.0.301`.
- Sales Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Integration-test project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Final Api Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- `dotnet ef migrations has-pending-model-changes` using the Db project and Api startup project:
  passed (`No changes have been made to the model since the last migration`).
- Repository whitespace check (`git diff --check`): passed.
- The EF CLI emitted one tooling warning: EF tools `10.0.3` are older than runtime `10.0.10`.

## Deviations and uncertainties

- Added `Numera.Modules.Ledger` as a direct Sales project reference. This supporting file was not in
  the plan's abbreviated ownership list, but is required for the specified public
  `ExpensePostingInput` return type; Ledger has no Sales reference, so this introduces no cycle.
- Integration tests and a live migration application were not run, per instruction. The migration
  SQL therefore has compile/model verification but awaits the reviewer's Testcontainers execution
  for runtime Postgres proof.
- `Program.cs`, `NumeraDbContext.cs`, `IReceiptExtractor`, and `IAttachmentScanner` were not touched.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left
  untouched.
