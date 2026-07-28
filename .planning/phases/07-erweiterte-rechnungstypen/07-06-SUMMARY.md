---
phase: 07-erweiterte-rechnungstypen
plan: 06
subsystem: api
tags: [hangfire, postgres, rls, recurring-invoices, ef-core]
requires:
  - phase: 07-01
    provides: RecurringInvoices capability and L/XL entitlement mapping
  - phase: 07-04
    provides: frozen foreign-currency invoice fields and validation conventions
provides:
  - RLS-forced recurring invoice template and line storage
  - Tenant-scoped recurring template CRUD and activate/pause scheduling
  - Per-period generated-document idempotency constraint
affects: [07-07, recurring-generation]
tech-stack:
  added: []
  patterns: [one Hangfire recurring job per tenant template, partial unique generation guard]
key-files:
  created:
    - src/modules/Numera.Modules.Sales/Recurring/RecurringInvoiceTemplate.cs
    - src/Numera.Api/Endpoints/RecurringInvoiceEndpoints.cs
    - src/Numera.Api/Jobs/GenerateRecurringInvoiceJob.cs
    - tests/Numera.IntegrationTests/RecurringTemplateTests.cs
  modified:
    - src/modules/Numera.Modules.Sales/SalesDocument.cs
    - src/Numera.Api/Program.cs
    - src/platform/Numera.Platform.Db/Sql/rls_policies.sql
key-decisions:
  - "Each active template owns recurring-invoice:{tenantId}:{templateId}; tenantId and templateId are Hangfire arguments."
  - "Generated documents are unique by tenant, template, and period key."
patterns-established:
  - "Recurring jobs re-enter tenant scope from a tenantId argument and never enumerate tenants."
completed: 2026-07-28
---

# 07-06 Summary — INV-06 Recurring Template Infrastructure

**Tenant-isolated recurring templates with L+ CRUD, per-template Hangfire schedules, and database-enforced period idempotency**

## Delivered

- Added recurring template and line entities with cadence, end conditions, next-run state,
  frozen currency inputs, generated count, active/paused/ended status, auto-finalize default,
  and auto-send configuration.
- Added nullable recurring-template and period-key provenance to generated sales documents.
- Generated the `RecurringInvoices` EF migration after `ForeignCurrencyColumns`; it creates
  both template tables, enables and forces tenant RLS, and adds the partial unique
  `(tenant_id, recurring_template_id, recurring_period_key)` document index.
- Added authenticated list/detail/create/update/delete/activate/pause endpoints. Every
  mutation checks `Capability.RecurringInvoices` and returns a 403 upgrade response when absent.
- Active create/update/activate operations register one stable Hangfire recurring job with
  tenant and template IDs embedded in both its ID and arguments; pause/delete removes it.
- Added the stable `GenerateRecurringInvoiceJob.RunAsync(Guid, Guid, CancellationToken)` stub,
  including tenant-scope re-entry, for plan 07-07 to replace with generation logic.
- Added PostgreSQL integration coverage for template/line persistence, status changes, RLS
  isolation, the capability seam, scheduling identity/cron, and duplicate-period rejection.

## Verification

- `dotnet ef migrations add RecurringInvoices --project src/platform/Numera.Platform.Db --startup-project src/Numera.Api`
  - Succeeded; EF tooling noted local `dotnet-ef` 10.0.3 is older than runtime 10.0.10.
- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter FullyQualifiedName~RecurringTemplateTests --no-build`
  - Passed: 3, failed: 0, skipped: 0.
- `dotnet test tests/Numera.IntegrationTests --no-build`
  - Passed: 118, failed: 0, skipped: 0.

## Notes

- No cross-tenant dispatcher or `BYPASSRLS` path was introduced.
- The job body remains intentionally empty after tenant-scope setup; plan 07-07 supplies
  idempotent catch-up generation while retaining the stable scheduling signature.
- No frontend files were changed and no git write commands were run.
