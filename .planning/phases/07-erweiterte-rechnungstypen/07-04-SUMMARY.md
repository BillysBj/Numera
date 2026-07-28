# 07-04 Summary — INV-05 Core Foreign Currency

## Delivered

- Added nullable frozen FX fields to `sales_documents`: exchange rate (`numeric(19,6)`),
  rate reference date, and VAT total in EUR (`numeric(19,4)`).
- Added a NodaMoney-backed two-minor-unit currency guard; USD/GBP/CHF-style currencies
  are accepted while JPY/BHD and unknown codes are rejected.
- Extended draft create/update and detail contracts with currency and FX data.
- Added server-authoritative `ForeignCurrencyInvoicing` capability gates to foreign
  currency create, update, convert, and finalize operations, returning a 403 upgrade hint.
- Copied rate and rate date through conversion, Storno, and credit-note draft paths.
- Added `RoundingPolicy.RoundAmount` and froze
  `TotalTaxEur = RoundAmount(TotalTax / ExchangeRate)` during finalization.
- Kept document totals and open items in the document currency.
- Generated the `ForeignCurrencyColumns` EF migration and model snapshot. The existing
  `sales_documents` RLS policy covers the new columns, so `rls_policies.sql` required no change.
- Added PostgreSQL 18 integration coverage for USD finalization, EUR behavior, open-item
  currency/amounts, and rejection of JPY/BHD.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Passed: 0 warnings, 0 errors.
- `dotnet test tests/Numera.IntegrationTests --filter FullyQualifiedName~ForeignCurrencyInvoiceTests --no-build -c Debug`
  - Passed: 4, failed: 0, skipped: 0.
- `dotnet test tests/Numera.IntegrationTests -c Debug`
  - Passed: 113, failed: 0, skipped: 0.

## Notes

- EF tooling reported that local `dotnet-ef` 10.0.3 is older than runtime 10.0.10;
  migration generation nevertheless completed successfully.
- No frontend files were changed and no git write commands were run.
