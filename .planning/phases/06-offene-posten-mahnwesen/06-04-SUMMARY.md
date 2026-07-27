# Phase 06 Plan 04: Dunning run backend

Implemented the manual tenant-scoped Mahnlauf from candidate selection through PDF delivery.

## Delivered

- Added `DunningService` candidate selection using the next configured level, overdue threshold,
  same-day idempotency, and decimal §288 interest through `RoundingPolicy`.
- Added the flat frozen-data `DunningNoticeModel` and one culture-driven QuestPDF layout for DE/EN
  labels. Money and dates use the selected label culture; configured German template prose remains
  verbatim.
- Added `POST /api/dunning/run`. Each run records pending notices, audit rows, and open-item dunning
  state in one transaction, then enqueues send jobs only after commit.
- Added `SendDunningNoticeJob` with three retries, its own tenant-restoring DI scope, RLS-scoped
  loads, render-if-absent, partner-to-frozen-snapshot recipient fallback, PDF attachment delivery,
  and Sent/Failed lifecycle updates.
- Added real PostgreSQL 18 + Mailpit integration coverage for issue/send, PDF capture, fee and
  interest calculation, unchanged open principal, same-day idempotency, unique-index enforcement,
  escalation, RLS isolation, and frozen-recipient fallback.

No migration was added. The 06-03 `open_items` dunning columns are used as-is.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter FullyQualifiedName~DunningRunTests --no-build`
  - Passed: 1; failed: 0; skipped: 0.
- `dotnet test tests/Numera.IntegrationTests`
  - Passed: 102; failed: 0; skipped: 0.
