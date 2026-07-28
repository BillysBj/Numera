# Plan 07-01 Summary — Entitlement foundation

## Delivered

- Appended `ForeignCurrencyInvoicing = 6`, `RecurringInvoices = 7`, and
  `DownPaymentInvoices = 8` to the server capability enum.
- Granted all three capabilities to L and XL only in the central
  `PlanCapabilityMap`.
- Added focused unit coverage for L/XL grants, S/M denial, and the proper-subset
  invariant across every tier.
- Added the SPA capability ordinal mirror, typed wire names,
  `useEntitlements()`, and the pure `hasCapability()` helper.
- Added a reusable, informational `UpgradeHint` status banner with German and
  English i18next defaults.

## Verification

- `dotnet build Numera.sln -c Debug`
  - Successful: 0 warnings, 0 errors.
- `dotnet test tests/Numera.Platform.Tests`
  - Successful: 97 passed, 0 failed, 0 skipped.
- `npm --prefix web run lint`
  - Successful (`tsc -b --noEmit`).
- `npm --prefix web run test`
  - Successful: 7 test files, 56 tests passed.
- `npm --prefix web run build`
  - Successful: TypeScript and Vite production build completed.
  - Vite reported its existing advisory that a generated chunk exceeds 500 kB.

No migration was added.
