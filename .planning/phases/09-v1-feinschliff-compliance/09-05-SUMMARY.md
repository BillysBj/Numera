---
phase: 09-v1-feinschliff-compliance
plan: 05
subsystem: ui
tags: [react, entitlements, tarif-gate, upgrade-hint, pwa, offline, dsgvo-export, i18n, vitest]

requires:
  - phase: 09-01
    provides: GET /api/export (Owner-only, DataExport-gated)
  - phase: 09-02
    provides: server EInvoicing + Dunning L+ gates behind the UpgradeHints
provides:
  - "Fixed entitlements plumbing (.capabilities, nested Me) + current-plan/tier badge"
  - "requiredTier-parameterized UpgradeHint on e-invoice + dunning surfaces"
  - "Outbound e-invoice UI (XRechnung/ZUGFeRD download + send-einvoice) gated by EInvoicing"
  - "Owner data-export download in settings"
  - "Offline-graceful finalize/send/PDF/e-invoice actions via a shared useOnline hook"
affects: [09-06]

tech-stack:
  added: []
  patterns:
    - "const caps = useEntitlements(); const allowed = hasCapability(caps.data?.capabilities, 'X'); {!allowed && <UpgradeHint requiredTier='L' />}"
    - "Shared useOnline hook; online-only actions disabled + banner when offline (never a throwing /api fetch)"

key-files:
  created:
    - web/src/lib/useOnline.ts
  modified:
    - web/src/lib/api.ts
    - web/src/lib/api/documents.ts
    - web/src/features/shared/UpgradeHint.tsx
    - web/src/pages/Dashboard.tsx
    - web/src/App.tsx
    - web/src/features/documents/DocumentDetailPage.tsx
    - web/src/features/dunning/DunningConfigSettingsPage.tsx
    - web/src/features/openItems/OpenItemsListPage.tsx
    - web/src/features/settings/CompanyProfileSettingsPage.tsx
    - web/src/features/dunning/DunningConfigSettingsPage.test.tsx
    - "web/src/i18n/locales/{de,en}/{common,documents,settings}.json"

key-decisions:
  - "USER DECISION: build the missing outbound e-invoice UI (XRechnung/ZUGFeRD download + send-einvoice) and gate it, rather than gate-only — Phase 5 shipped the backend + inbound viewer but never the outbound buttons"
  - "api.ts Me was stale (flat) vs the server's nested {user,tenant,role}; corrected the type rather than only adding plan, and fixed Dashboard's reads"
  - "Owner export uses a direct same-origin <a href='/api/export'> (cookie flows on navigation, server streams the ZIP) — no JS buffering of a large binary"

patterns-established:
  - "Tier-aware UpgradeHint over server-authoritative 403 gates"
  - "Shared useOnline offline-degradation hook"

duration: ~90min
completed: 2026-08-02
---

# Phase 09-05: Frontend tarif-gate UX + export + offline

**Consistent tier-aware UpgradeHints over the e-invoice + dunning gates, a current-plan badge, the Owner DSGVO-export download, new outbound e-invoice buttons, and offline-safe financial actions — the web half of criteria 2 & 3.**

## Performance

- **Duration:** ~90 min
- **Tasks:** 3
- **Files created:** 1 | **modified:** ~15

## Accomplishments
- **Entitlements bug fixed:** `api.ts` `Entitlements` is now `{ capabilities }` and `Me` matches the server's nested `{ user, tenant: {…, plan}, role }`; `Dashboard` reads the corrected shapes. A current-plan/tier badge sits in the app shell.
- **UpgradeHint parameterized** with `requiredTier` (`{{tier}}` interpolation, default L).
- **Outbound e-invoice UI added + gated** (user decision): XRechnung/ZUGFeRD download + send-einvoice on a finalized Rechnung, behind `hasCapability('EInvoicing')`; a non-L tenant sees an UpgradeHint instead. New client fns `downloadXRechnung`/`downloadZugferd`/`sendEInvoice`.
- **Dunning gated:** config form + Mahnlauf button disabled with an UpgradeHint when `Dunning` is absent.
- **Owner data export:** an "Alle Daten exportieren" download (`GET /api/export`) in settings, Owner-only.
- **Offline degradation:** shared `useOnline` hook; `DocumentDetailPage` disables finalize/send/PDF/e-invoice + shows a banner offline (no throwing `/api` fetch). PWA/SW config untouched.
- DE-authoritative i18n across common/documents/settings. tsc clean; vitest 56/56; `npm run build` + PWA OK.

## Task Commits

1. **Tasks 1–3 (frontend)** — `abd730a` (feat) — landed as one commit (the whole plan owns the shared shell files; changes are interdependent).

## Files Created/Modified
See key-files. Highlights: `useOnline.ts` (new), `api.ts` (Me/Entitlements types), `documents.ts` (e-invoice client fns), `DocumentDetailPage.tsx` (e-invoice buttons + gate + offline), `UpgradeHint.tsx` (requiredTier), `App.tsx` (plan badge), settings/dunning/openItems pages.

## Decisions Made
See key-decisions. The e-invoice-UI scope was a user decision (add + gate). The `Me` type was corrected to the real nested server shape.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing dependency] Outbound e-invoice UI did not exist**
- **Found during:** Task 2
- **Issue:** The plan assumed XRechnung/ZUGFeRD/send-einvoice actions existed to gate; they did not (Phase 5 shipped backend + inbound only).
- **Fix:** After a user decision, built the minimal outbound e-invoice UI + client fns and gated them.
- **Committed in:** `abd730a`

**2. [Rule 1 - Test correctness] Dunning gate broke an existing vitest**
- **Found during:** verification
- **Issue:** The Mahnlauf test rendered `OpenItemsListPage`, which now needs the Dunning capability; the unmocked `useEntitlements` left the button disabled.
- **Fix:** Mocked `useEntitlements` to grant Dunning (an L-tier tenant); `hasCapability` stays real.
- **Committed in:** `abd730a`

---

**Total deviations:** 2 auto-fixed (1 missing dependency handled via a user decision, 1 test-correctness). The e-invoice UI is a deliberate scope addition the user approved.

## Issues Encountered
None beyond the deviations.

## Next Phase Readiness
- 09-06 can human-verify install/offline, the export download, and the gate UX end-to-end.

---
*Phase: 09-v1-feinschliff-compliance*
*Completed: 2026-08-02*
