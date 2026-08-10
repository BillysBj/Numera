---
phase: 14-monetarisierung-stripe
plan: 06
subsystem: stripe-billing-react-frontend
tags: [react, typescript, tanstack-query, stripe-checkout, customer-portal, i18n]

requires:
  - phase: 14-03
    provides: Authenticated Checkout, Customer Portal, and billing-status API endpoints
provides:
  - Hosted Stripe Checkout and Customer Portal redirects from the authenticated web app
  - Current-plan, trial-days, subscription-status, and paid-tier pricing surface
  - App-wide non-blocking degradation warning linked to billing recovery
  - German and English billing localization
affects: [billing-ux, entitlement-refresh, read-only-degradation, stripe-self-service]

tech-stack:
  added: []
  patterns:
    - Same-origin authenticated billing requests through apiRequest
    - Server-authoritative return flow with TanStack Query invalidation only
    - Hosted payment navigation through window.location.assign

key-files:
  created:
    - web/src/features/billing/billingApi.ts
    - web/src/features/billing/BillingPage.tsx
    - web/src/features/billing/PricingCards.tsx
    - web/src/features/billing/DegradationBanner.tsx
    - web/src/i18n/locales/de/billing.json
    - web/src/i18n/locales/en/billing.json
  modified:
    - web/src/App.tsx
    - web/src/i18n/index.ts

key-decisions:
  - "Checkout and Portal remain top-level hosted Stripe navigations; Numera renders no payment inputs."
  - "Checkout success only invalidates billing-status and entitlements; webhook-backed server state remains authoritative."
  - "Degradation is represented by a persistent non-modal banner, preserving application reads and navigation."

completed_tasks: [1, 2]
pending_tasks: []
duration: 15min
completed: 2026-08-10
---

# Phase 14-06: Stripe billing React frontend

Authenticated users can inspect plan and trial state, start or switch S/M/L/XL plans through hosted
Stripe Checkout, open the Customer Portal, and see an app-wide read-only warning when server billing
status reports degradation.

## Implemented

- Added a typed billing API client using the shared `apiRequest` wrapper, so all status, Checkout,
  and Portal requests retain the existing same-origin `credentials: 'include'` behavior.
- Added `useBillingStatus()` over `GET /api/billing/status`, plus direct hosted-navigation commands
  for `POST /api/billing/checkout` and `POST /api/billing/portal`.
- Added a billing page showing the current plan, active-trial days remaining, subscription status,
  and the Portal action when a subscription exists. A Portal 409 becomes localized guidance to
  start a plan first.
- Added S/M/L/XL pricing cards with a distinct current-plan marker and start/switch actions. The
  feature contains no card-data, coupon, or promotion inputs.
- Added `/billing`, `/billing/success`, and `/billing/cancel` routes. A successful return with
  `session_id` invalidates exactly `['billing-status']` and `['entitlements']` and shows the existing
  accessible inline status notification; it does not update entitlements or plan state locally.
- Added a persistent, non-dismissable, non-modal degradation banner once above authenticated routed
  content. It links to `/billing` and never blocks reads or navigation.
- Added the billing item beside Settings in the System navigation group and registered complete DE
  and EN billing namespaces.

## Files created

- `web/src/features/billing/billingApi.ts`
- `web/src/features/billing/BillingPage.tsx`
- `web/src/features/billing/PricingCards.tsx`
- `web/src/features/billing/DegradationBanner.tsx`
- `web/src/i18n/locales/de/billing.json`
- `web/src/i18n/locales/en/billing.json`
- `.planning/phases/14-monetarisierung-stripe/14-06-SUMMARY.md`

## Files modified

- `web/src/App.tsx`
- `web/src/i18n/index.ts`

## Verification

Run from `web/` through the Windows npm executable:

- `npm.cmd run lint`: passed (`tsc -b --noEmit`; 0 errors).
- `npm.cmd run build`: passed (`tsc -b && vite build`; 311 modules transformed). Vite retained its
  non-blocking warning that the main 851.70 kB chunk exceeds 500 kB.
- `npm.cmd run test`: passed (7 test files, 56 tests, 0 failures).
- `git diff --check`: passed.
- Static feature scan for `<input`, Stripe card elements/card-number fields, coupon fields, and
  promotion fields: no matches.

The initial PowerShell invocation `npm run lint` was blocked before npm started because local script
execution policy rejects `npm.ps1`. Running the identical scripts via `npm.cmd` succeeded; no restore,
dependency, or network command was needed.

## Deviations from Plan

- The plan frontmatter names `web/src/locales/de/billing.json`, but Numera's explicit i18n loader and
  every existing locale live under `web/src/i18n/locales/{de,en}`. The billing files were placed in
  that active repository convention and registered in `web/src/i18n/index.ts`; no unused parallel
  locale tree was created.
- Numera has no toast library or global toast provider. Checkout success, cancellation, and request
  failures therefore use the established accessible inline `role="status"` / `role="alert"` pattern.
  The success notice is shown only for `/billing/success` with a `session_id`.

## Issues Encountered

- No implementation or verification failures. The PowerShell `npm.ps1` policy issue was resolved by
  invoking the checked-in scripts through `npm.cmd`.

## User Setup Required

None - no frontend dependency or external configuration was added.

## Next Phase Readiness

- The web frontend is ready to consume the Stripe webhook-driven plan changes and the server-side
  degradation write guard.
- No file was staged or committed.

---
*Phase: 14-monetarisierung-stripe · Plan: 06 · Tasks completed: 2/2*
