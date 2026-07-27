---
phase: 06-offene-posten-mahnwesen
plan: 01
subsystem: payments
tags: [dotnet-10, ef-core, postgres-18, rls, gobd, payments]

requires:
  - phase: 03-sales
    provides: Finalized sales documents, open items, audit writer, and immutable-document lifecycle
provides:
  - Append-only tenant-scoped payment and payment-allocation persistence
  - Atomic partial/full settlement and reversal service
  - Authorized record, reverse, and paged-list payment endpoints
  - Real-Postgres integration proof for settlement, validation, immutability, reversal, and RLS
affects: [06-offene-posten-mahnwesen, bank-reconciliation, open-items]

tech-stack:
  added: []
  patterns: [append-only reversal payments, grouped allocation validation, mutate-audit-save transaction]

key-files:
  created:
    - src/platform/Numera.Platform.Db/Migrations/20260727205739_Payments.cs
    - src/Numera.Api/Services/PaymentService.cs
    - src/Numera.Api/Contracts/PaymentContracts.cs
    - src/Numera.Api/Endpoints/PaymentEndpoints.cs
    - tests/Numera.IntegrationTests/PaymentTests.cs
  modified:
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - src/Numera.Api/Program.cs

key-decisions:
  - "Validate allocations grouped by open-item id inside the transaction so duplicate inputs cannot exceed an open balance."
  - "Corrections append a negative payment and negative allocations; original booked rows are never mutated."

patterns-established:
  - "Payment commands use one EF transaction for payment rows, receivable mutations, audit append, save, and commit."
  - "Booked payment facts are protected by both role privileges and unconditional database triggers."

duration: 18min
completed: 2026-07-27
---

# Phase 6 Plan 01: Payments Backend Summary

**Tenant-isolated append-only payments now settle or reopen invoice receivables atomically, with audited reversal-based corrections**

## Performance

- **Duration:** 18 min
- **Completed:** 2026-07-27
- **Tasks:** 3
- **Files created/modified:** 9 implementation/generated files plus this summary

## Accomplishments

- Added `payment` and `payment_allocation` tables with decimal money columns, RLS, and GoBD append-only privilege/trigger enforcement.
- Added atomic record/reverse behavior that updates open items and sales-document payment lifecycle state and appends audit events.
- Added authorized POST record/reverse and paged GET endpoints, including optional open-item filtering and typed 404/409/422 outcomes.
- Proved all required behaviors plus multi-open-item allocation on real Postgres 18; the complete 95-test integration suite passes.

## Task Commits

No commits were created, per the task's explicit requirement to leave all changes uncommitted for human review.

## Files Created/Modified

- `src/platform/Numera.Platform.Db/Migrations/20260727205739_Payments.cs` - Creates both payment tables and their RLS/append-only controls.
- `src/platform/Numera.Platform.Db/Migrations/20260727205739_Payments.Designer.cs` - Generated EF migration metadata.
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs` - Includes both payment entities.
- `src/Numera.Api/Services/PaymentService.cs` - Validates, records, reverses, mutates receivables, and audits in one transaction.
- `src/Numera.Api/Contracts/PaymentContracts.cs` - Payment request, allocation, and list DTOs.
- `src/Numera.Api/Endpoints/PaymentEndpoints.cs` - Authorized record, reverse, and paged-list routes.
- `src/Numera.Api/Program.cs` - Registers the service and maps payment endpoints.
- `tests/Numera.IntegrationTests/PaymentTests.cs` - Seven real-Postgres payment behavior tests.
- `.planning/phases/06-offene-posten-mahnwesen/06-01-SUMMARY.md` - Execution record and verification results.

The three pre-existing files under `src/modules/Numera.Modules.Sales/Payments/` were reviewed against the plan and required no refinement.

## Decisions Made

- Allocation totals must equal the payment amount, and repeated allocations targeting the same open item are grouped before the in-transaction balance check.
- Reopening a fully paid document through reversal returns its lifecycle status to `Finalized`; partially reopened documents remain receivable-bearing.

## Deviations from Plan

None - the plan was executed as specified. One additional multi-open-item integration test was included to directly prove a stated must-have.

## Issues Encountered

- `dotnet ef` reported that the installed tool version 10.0.3 is older than runtime 10.0.10. Migration generation still completed successfully; this was informational and produced no build warning.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Payment recording and correction are ready for the payment UI and subsequent dunning work.
- The append-only tables provide the transaction history seam needed by future bank reconciliation.

---
*Phase: 06-offene-posten-mahnwesen*
*Completed: 2026-07-27*
