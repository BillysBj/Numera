# 07-09 Summary: Serienrechnungen frontend

## Implemented

- Added a typed recurring-template BFF client for list, detail, create, update,
  activate, pause, and delete, including numeric enum wire values matching the
  C# declaration order and 400/422 validation-problem extraction.
- Added the recurring-template list with status, cadence, next-run date,
  generated count, edit links, and pause/activate actions.
- Added the RHF/zod template editor with customer, currency/exchange-rate,
  cadence, start/end conditions, invoice lines, active status, auto-finalize,
  and auto-send fields. Client validation mirrors the 07-06 server gates.
- Added cosmetic `RecurringInvoices` entitlement gates using `UpgradeHint`.
- Added `/recurring`, `/recurring/new`, and `/recurring/:id/edit`, primary
  navigation, and German/English `recurring` translations.

## Verification

- `npm --prefix web run lint` — passed.
- `npm --prefix web run build` — passed (270 modules transformed; Vite emitted
  the existing large-chunk advisory).
- `npm --prefix web run test` — passed: 7 test files, 56 tests.

## Pending

The plan's final `checkpoint:human-verify` was intentionally not performed.
User approval of the manual recurring-template lifecycle verification remains
pending.
