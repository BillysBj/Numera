# Plan 08-03 Summary — CRM-04 Partner Tasks

## Delivered

- Added the reflectively discovered `PartnerTask` tenant entity with UUIDv7 IDs,
  Open/Done status, optional due date, description and assignee, plus completion
  timestamps.
- Added the `20260728202315_PartnerTasks` EF Core migration after
  `20260728145309_RecurringInvoices`, including the tenant-leading index and
  hand-written ENABLE/FORCE/`tenant_isolation` RLS policy. The canonical
  `rls_policies.sql` reference is synchronized.
- Added authenticated partner-task endpoints for list, create, update,
  complete, and delete. List queries support `openOnly` and `overdueOnly`;
  overdue means a non-null due date before today while status is Open.
- Added the React task query/mutation client and an additive Aufgaben section on
  partner details with create/edit/delete, Done toggle, due date, assignee,
  open/overdue filters, overdue badge, and accessible status/error feedback.
- Added German-authoritative and English task translations and registered the
  additive `tasks` i18n namespace.
- Added real-Postgres integration coverage for create/edit/complete/delete
  persistence, overdue filtering, cross-tenant invisibility, and rejected
  cross-tenant inserts.
- No reminder jobs or email reminders were added, per locked decision 5.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests`
  - Passed: 133 total, 133 passed, 0 failed, 0 skipped.
- `npm --prefix web run build`
  - Passed: TypeScript build and Vite production build completed.
  - Vite emitted its existing advisory that one output chunk exceeds 500 kB.
- `npm --prefix web run lint`
  - Passed: `tsc -b --noEmit`.
- `npm --prefix web run test`
  - Passed: 7 test files, 56 tests, 0 failed.
- `git diff --check`
  - Passed with no whitespace errors; Git printed line-ending conversion notices.
