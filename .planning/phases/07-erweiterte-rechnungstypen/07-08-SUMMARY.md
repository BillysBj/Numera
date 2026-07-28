# Plan 07-08 Summary — Down-Payment and Currency Documents

## Delivered

- Added server-authoritative `DownPaymentInvoices` gating and dedicated draft creation
  endpoints for Abschlagsrechnungen and Schlussrechnungen.
- Schlussrechnung creation validates tenant-scoped, finalized Abschlagsrechnungen for
  the same partner and freezes their legal number, date, net, VAT, and gross totals in
  `SalesDocumentPrepayment` rows using `db.Add(...)`.
- Added partner filtering for the document list and exposed frozen prepayments in the
  document detail/finalize response.
- Extended the React document client and numeric enum mirror for Abschlagsrechnung (6)
  and Schlussrechnung (7), including FX and prepayment DTO fields.
- Added EUR-default currency fields, a two-minor-unit currency allow-list, and conditional
  positive exchange-rate/rate-date validation using the server's `1 EUR = X currency`
  convention.
- Added entitlement-aware document-type/currency choices, the finalized-Abschlag picker,
  Schlussrechnung residual preview, frozen deduction detail table, and foreign-currency
  EUR VAT/rate display.
- Added additive German-authoritative and English translations for the new UI.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed with 0 warnings and 0 errors.
- `dotnet test tests/Numera.IntegrationTests -c Debug --no-build`
  - Passed: 121, failed: 0, skipped: 0.
- `npm --prefix web run build`
  - Passed (`tsc -b` and Vite production build).
- `npm --prefix web run lint`
  - Passed (`tsc -b --noEmit`).
- `npm --prefix web run test`
  - Passed: 7 test files, 56 tests; failed: 0.
- `git diff --check`
  - Passed; only line-ending notices were emitted by Git.

## Pending Checkpoint

Task 3 (`checkpoint:human-verify`) was intentionally not performed. The foreign-currency
and Abschlag → Schlussrechnung browser flows remain pending user approval.
