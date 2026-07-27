---
phase: 06-offene-posten-mahnwesen
plan: 05
status: tasks-1-3-complete
human_verify: pending
---

# Plan 06-05 Summary

Implemented Tasks 1–3 of the dunning frontend plan.

## Delivered

- Added a typed dunning BFF client for reading/saving the ladder and starting a dunning run.
- Added bilingual German/English dunning translations and registered the namespace.
- Added the RHF/zod dunning settings page with API prefill, client-side ladder gates,
  ValidationProblem handling, and save feedback.
- Enriched the existing OP overview additively with current dunning level, days overdue,
  and a dunning-run action while preserving due-date ordering and payment recording.
- Added the `/settings/dunning` route and navigation link.
- Enriched the OP-list DTO with `CurrentDunningLevel` and `LastDunnedOn`. The endpoint
  reads the existing DB-only shadow columns with one raw-SQL lookup keyed by page IDs;
  no CLR entity properties or migration were added.
- Added Vitest coverage for config prefill, invalid ladders, valid saves, the dunning
  run result, and the OP-list dunning-level column.

## Verification

- `npm --prefix web run build` — passed.
- `npm --prefix web run lint` — passed.
- `npm --prefix web run test` — passed: 56 tests in 7 files.
- `dotnet build Numera.sln -c Debug` — passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests` — passed: 102 tests, 0 failed,
  0 skipped.

## Pending checkpoint

Task 4 was intentionally not performed. The full running-app and Mailpit/PDF
human-verification checkpoint is pending user approval.
