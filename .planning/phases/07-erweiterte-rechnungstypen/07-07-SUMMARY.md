---
phase: 07-erweiterte-rechnungstypen
plan: 07
subsystem: api
tags: [hangfire, postgres, rls, recurring-invoices, idempotency]
requires:
  - phase: 07-06
    provides: recurring templates, generated-document provenance, unique period index, and per-template schedules
  - phase: 07-04
    provides: frozen foreign-currency fields and EUR VAT calculation
provides:
  - Tenant-safe recurring invoice catch-up generation
  - Database-backed per-period idempotency
  - Shared-path automatic finalization and optional draft generation
  - End-condition state transitions and recurring-job removal
affects: [recurring-invoices, sales-finalize, document-delivery]
tech-stack:
  added: []
  patterns: [template-state-driven catch-up, per-occurrence transaction, post-commit side effects]
key-files:
  created:
    - tests/Numera.IntegrationTests/RecurringGenerationTests.cs
  modified:
    - src/Numera.Api/Jobs/GenerateRecurringInvoiceJob.cs
key-decisions:
  - "Each occurrence locks and reloads the tenant-scoped template row before generation, serializing concurrent fires while retaining the unique period index as the final guard."
  - "Monthly single-interval occurrences use yyyy-MM keys; all other cadences use stable start..end keys."
  - "Generated invoices reuse SalesDocumentEndpoints.FinalizeCoreAsync and publish InvoiceFinalized only after commit."
patterns-established:
  - "Recurring generation advances exactly one cadence per persisted or already-existing period and loops until NextRunOn is in the future or an end condition is reached."
completed: 2026-07-28
---

# 07-07 Summary — Recurring Invoice Generation

## Delivered

- Replaced the recurring generation stub with a tenant-scoped Hangfire job carrying
  `AutomaticRetry(Attempts = 3)` and the unchanged
  `RunAsync(Guid tenantId, Guid templateId, CancellationToken)` contract.
- Added template-state-driven catch-up. Every due occurrence copies the template header and
  lines into a Rechnung draft with deterministic period provenance, service-period dates, and
  frozen currency/exchange-rate inputs.
- Added a per-occurrence transaction with a PostgreSQL row lock. The partial unique recurring
  period index remains the final idempotency guard; its specific `23505` violation is rolled
  back and treated as an already-generated period while template state still advances.
- Reused `SalesDocumentEndpoints.FinalizeCoreAsync` for automatic finalization, preserving
  race-safe numbering, snapshots, VAT breakdown, EUR VAT freezing, due dates, open items, and
  audit behavior. KoSIT rejection leaves the occurrence as a Draft without burning a number;
  validator unavailability logs and continues, matching manual finalize.
- Published `InvoiceFinalized` after commit so existing PDF/e-invoice handlers fan out.
  Auto-send creates the existing queued `DocumentEmail` record after finalize commit and then
  enqueues `SendDocumentEmailJob`.
- Advanced `NextRunOn`, `LastGeneratedPeriodEnd`, and `GeneratedCount` per occurrence. Reached
  UntilDate/AfterCount templates become Ended and their Hangfire recurring registration is
  removed.
- Added real PostgreSQL 18 integration coverage for three-period catch-up, finalization and
  numbering, one open item per invoice, distinct period keys, rerun idempotency, end-state
  transition, draft opt-out, RLS isolation, and foreign-currency `TotalTaxEur` freezing.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter FullyQualifiedName~RecurringGenerationTests --no-build -c Debug`
  - Passed: 3, failed: 0, skipped: 0.
- `dotnet test tests/Numera.IntegrationTests --no-build -c Debug`
  - Passed: 121, failed: 0, skipped: 0.
- `git diff --check`
  - Passed; only Git's informational LF-to-CRLF warning was emitted for the modified job file.

## Notes

- No migration, frontend file, or file outside the plan's allowed implementation/test/summary
  scope was changed.
- No git write command was run.
