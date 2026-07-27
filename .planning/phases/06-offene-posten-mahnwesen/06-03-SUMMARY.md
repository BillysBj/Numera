# Plan 06-03 Summary

## Outcome

Implemented the tenant-scoped dunning configuration backend and its schema foundation:

- Added configurable dunning levels and issued-notice history entities.
- Added a four-level German default ladder with DE/EN texts and configurable §288 interest.
- Added the `Dunning` migration with both RLS-protected tables, notice idempotency uniqueness,
  and the `open_items.current_dunning_level` / `last_dunned_on` columns.
- Added seed-on-empty GET and validated replacement PUT at `/api/dunning/config`.
- Added audit events for default seeding and configuration updates.
- Left the dunning-run/send workflow as an explicit 06-04 extension seam.

Fees and interest remain ancillary claims on the notice and do not alter
`OpenItem.OpenAmount`.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests`
  - Passed: 101
  - Failed: 0
  - Skipped: 0

The new real-Postgres tests prove default materialization, edited configuration
persistence, validation, tenant RLS isolation, the `open_items` column defaults,
and the notice unique-index violation.
