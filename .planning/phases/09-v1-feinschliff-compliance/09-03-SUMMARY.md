---
phase: 09-v1-feinschliff-compliance
plan: 03
subsystem: testing
tags: [rls, security, cross-tenant, isolation, postgres, compliance, gobd]

requires:
  - phase: 06-offene-posten-mahnwesen
    provides: payment, payment_allocation, dunning_level_config, dunning_notice tables
  - phase: 07-erweiterte-rechnungstypen
    provides: recurring templates(+lines), sales_document_prepayment tables
  - phase: 08-crm-ausbau-steuerberater-zugang
    provides: partner_tasks, customer_files tables
provides:
  - "Consolidating cross-tenant RLS isolation proof over 11 phase-6→8 tables (read-isolation + WITH CHECK reject)"
affects: [09-06, complete-milestone]

tech-stack:
  added: []
  patterns:
    - "Data-driven RLS-completeness test: minimal per-table row builders driven through two generic helpers (IgnoreQueryFilters read-isolation + WITH CHECK reject), FK tables seed the parent as tenant A"

key-files:
  created:
    - tests/Numera.IntegrationTests/CrossTenantIsolationCompletenessTests.cs
  modified: []

key-decisions:
  - "IgnoreQueryFilters() everywhere so RLS — not the EF global filter — is the only control under test (mirrors RlsIsolationTests)"
  - "Only recurring_invoice_template_lines→template and sales_document_prepayment→document have DB-enforced FKs; those seed the parent as tenant A so the sole violation under test is the foreign child TenantId; the other 9 tables insert directly"

patterns-established:
  - "One consolidating 'nothing leaks' proof per uncovered tenant table"

duration: ~35min
completed: 2026-08-02
---

# Phase 09-03: Cross-tenant RLS completeness

**One consolidating proof that all 11 phase-6→8 tables isolate across tenants — tenant A reads zero of tenant B's rows (RLS alone) and a cross-tenant INSERT is rejected by WITH CHECK — the auditable "Cross-Tenant-Sicherheitssuite grün" evidence for criterion 4.**

## Performance

- **Duration:** ~35 min
- **Tasks:** 2 (read-isolation, WITH CHECK reject)
- **Files created:** 1

## Accomplishments
- `CrossTenantIsolationCompletenessTests` on real postgres:18 as the non-BYPASSRLS `numera_app` role, covering `payment`, `payment_allocation`, `dunning_level_config`, `dunning_notice`, `recurring_invoice_templates`, `recurring_invoice_template_lines`, `sales_document_prepayment`, `partner_tasks`, `customer_files`, `document_einvoice`, `inbound_document`.
- Read-isolation fact: seeds a row (+parents) as tenant A and as tenant B, then with the EF filter OFF asserts A sees only A's rows and B independently sees its own (so the proof is not vacuous).
- WITH CHECK fact: acting as A, a row stamped for B is rejected with a "row-level security" error; FK tables seed the parent as A so the only violation is the foreign child TenantId.
- 2/2 green (each fact iterates all 11 tables).

## Task Commits

1. **Task 1 + 2: read-isolation + WITH CHECK reject** — `5f01d76` (test)

## Files Created/Modified
- `CrossTenantIsolationCompletenessTests.cs` — the consolidating isolation proof

## Decisions Made
See key-decisions. Determined FK enforcement from the model snapshot: only template_lines and prepayment carry DB-enforced FKs among the 11.

## Deviations from Plan
None - plan executed as written (Task 1 + Task 2 combined into one file with two data-driven Facts, exactly as the plan's "single 'nothing leaks' proof" intent).

## Issues Encountered
Wave 1 was interrupted by a usage limit before this plan was started; the orchestrator authored it fresh against the canonical `RlsIsolationTests` pattern.

## Next Phase Readiness
- Criterion-4 security half proven. 09-06 will fold this into the full battery.

---
*Phase: 09-v1-feinschliff-compliance*
*Completed: 2026-08-02*
