---
phase: 01-plattform-kern
plan: 07
subsystem: ui
tags: [react, vite, pwa, vite-plugin-pwa, i18next, react-i18next, tanstack-query, react-router, bff, workbox]

# Dependency graph
requires:
  - phase: 01-plattform-kern
    provides: "BFF endpoints /api/auth/login, /api/auth/register, /api/me, /api/me/entitlements (plan 01-06, wired at runtime — no build-time dependency)"
provides:
  - "Installable React 19 + Vite 7 PWA shell (web/) with app-shell-only precache and /api excluded from the service worker"
  - "DE/EN i18n (react-i18next) with a persisting LanguageSwitcher (localStorage + cookie)"
  - "BFF-friendly cookie-only API client (credentials: 'include', no token storage)"
  - "Login + Dashboard page shells wired to the BFF via TanStack Query"
affects: [01-08, "any phase adding SPA feature areas, auth UI, or offline behavior"]

# Tech tracking
tech-stack:
  added: [react-19, vite-7, vite-plugin-pwa, i18next, react-i18next, i18next-browser-languagedetector, "@tanstack/react-query", react-router-dom-7, vitest, jsdom]
  patterns:
    - "PWA precaches static shell only; /api is NetworkOnly + navigateFallbackDenylist (no stale financial data)"
    - "SPA is a public client holding no tokens; auth via HttpOnly session cookie on same-origin fetch"
    - "i18n namespaces (common + auth) with de default/fallback; language persisted to localStorage + cookie for BFF/SSR agreement"
    - "Same-origin BFF in dev via Vite server.proxy /api -> localhost:5080"

key-files:
  created:
    - web/.gitignore
    - web/src/i18n/index.ts
    - web/src/i18n/locales/de/common.json
    - web/src/i18n/locales/en/common.json
    - web/src/i18n/locales/de/auth.json
    - web/src/i18n/locales/en/auth.json
    - web/src/i18n/i18n.test.ts
    - web/src/components/LanguageSwitcher.tsx
    - web/src/lib/api.ts
    - web/src/pages/Login.tsx
    - web/src/pages/Dashboard.tsx
  modified:
    - web/src/App.tsx
    - web/src/main.tsx
    - web/vite.config.ts

key-decisions:
  - "PWA workbox: app-shell globPatterns + NetworkOnly runtime rule (GET+POST) for /api + navigateFallbackDenylist [/^\\/api/] — financial data never served stale"
  - "SPA stores no tokens; HttpOnly session cookie carries auth on same-origin credentials:'include' fetch"
  - "German is the i18n default/fallback; language persisted to both localStorage and a lng cookie so the BFF/SSR agree"
  - "Vitest unit test over i18n resources (jsdom) instead of adding React Testing Library — keeps deps minimal"

patterns-established:
  - "No-token SPA + BFF cookie auth: lib/api.ts is the single fetch wrapper; all endpoints go through it"
  - "i18n namespace split (common/auth) so future feature areas add their own namespaces"
  - "PWA data posture: static shell cached, /api always network"

# Metrics
duration: 18min
completed: 2026-07-10
---

# Phase 1 Plan 7: React 19 PWA Shell + DE/EN i18n + BFF Cookie Client Summary

**Installable React 19 + Vite 7 PWA whose service worker precaches only the app shell and never caches /api, with a persisting DE/EN language switch and a token-less BFF cookie API client feeding Login + Dashboard shells.**

## Performance

- **Duration:** ~18 min (resumed from interrupted WIP)
- **Completed:** 2026-07-10
- **Tasks:** 3
- **Files modified:** 14 (11 created, 3 modified)

## Accomplishments
- Installable PWA: `vite build` emits `sw.js` + `manifest.webmanifest`, precaching 10 shell entries; built `sw.js` confirmed to carry `navigateFallbackDenylist: [/^\/api/]` and `NetworkOnly` runtime rules for GET/POST `/api` — no stale financial data offline.
- DE/EN i18n via react-i18next (namespaces `common` + `auth`, German default/fallback, browser language detector); `LanguageSwitcher` toggles language and persists `lng` to localStorage + cookie. Backed by a passing Vitest suite (4 tests).
- Token-less BFF API client (`lib/api.ts`): same-origin `/api` fetch with `credentials: 'include'`, typed `getMe` / `getEntitlements` / `registerCompany` + `loginUrl`; verified no token is stored in localStorage.
- Login shell (sign-in navigates to BFF `/api/auth/login`; register form posts via `registerCompany`) and Dashboard shell (TanStack Query `getMe` + `getEntitlements`, tenant name, cosmetic feature list, offline banner).

## Task Commits

Each task was committed atomically:

1. **Task 1: Vite + React 19 scaffold + PWA (shell-only, /api excluded)** - `4a26182` (chore — scaffold pre-existed in WIP; finalized with web/.gitignore + build verification)
2. **Task 2: react-i18next (DE/EN) + persisting LanguageSwitcher** - `6025129` (feat)
3. **Task 3: BFF API client + Login/Dashboard shells** - `bca7625` (feat)

_Note: the interrupted WIP commit `d5e3b22` provided the Vite scaffold, manifest, and icons; it does not count as a task commit._

## Files Created/Modified
- `web/vite.config.ts` - VitePWA (autoUpdate, shell globPatterns, /api NetworkOnly + denylist), dev `/api` proxy, Vitest jsdom config (modified)
- `web/public/manifest.webmanifest` - PWA manifest, standalone, 192/512 + maskable icons (from WIP)
- `web/src/i18n/index.ts` - react-i18next init: common/auth namespaces, de fallback, language detector (localStorage → cookie → navigator)
- `web/src/i18n/locales/{de,en}/{common,auth}.json` - shell + auth strings for both locales
- `web/src/components/LanguageSwitcher.tsx` - DE/EN toggle persisting lng to localStorage + cookie
- `web/src/lib/api.ts` - BFF cookie fetch wrapper (credentials:'include'), typed endpoints, ApiError, no token storage
- `web/src/pages/Login.tsx` - sign-in → `/api/auth/login`; i18n'd register form
- `web/src/pages/Dashboard.tsx` - TanStack Query me/entitlements, tenant name, offline banner
- `web/src/App.tsx` - app header with LanguageSwitcher + router wiring real pages (modified)
- `web/src/main.tsx` - imports i18n init (modified)
- `web/.gitignore` - dist/node_modules/tsbuildinfo (created)
- `web/src/i18n/i18n.test.ts` - Vitest: default de, switch to en, persist lng, both namespaces present

## Decisions Made
- Kept the interrupted WIP under `web/` (correct directory per plan) rather than restarting; verified it against the plan and completed only the missing i18n / API-client / pages work.
- Used a Vitest unit test over the i18n resources (jsdom already installed) instead of pulling in React Testing Library, to keep the dependency surface minimal while still proving the language-switch + persistence behavior.
- Split runtime caching into explicit GET and POST NetworkOnly rules for `/api` so both financial reads and writes always hit the network.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ApiError rewritten to avoid TypeScript parameter properties**
- **Found during:** Task 3 (BFF API client)
- **Issue:** `tsconfig.app.json` enables `erasableSyntaxOnly`, which forbids constructor parameter-property shorthand (`public readonly status`); `tsc -b` failed with TS1294.
- **Fix:** Declared `status`/`body` as explicit class fields and assigned them in the constructor body.
- **Files modified:** web/src/lib/api.ts
- **Verification:** `npm run build` (tsc + vite) succeeds.
- **Committed in:** `bca7625` (Task 3 commit)

**2. [Rule 3 - Blocking] Added web/.gitignore for build artifacts**
- **Found during:** Task 1 (scaffold finalization)
- **Issue:** `tsc -b` emits `*.tsbuildinfo` files that were untracked and not covered by the root .gitignore.
- **Fix:** Added `web/.gitignore` (dist, node_modules, *.tsbuildinfo, logs, editor dirs).
- **Files modified:** web/.gitignore
- **Verification:** `git status` clean of build artifacts.
- **Committed in:** `4a26182` (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 3 blocking)
**Impact on plan:** Both fixes were required to build and to keep the repo clean. No scope creep; all planned artifacts delivered.

## Issues Encountered
- Parallel executors (plans 01-01, 01-03) committed interleaved commits during this plan; stayed strictly within `web/` and my plan's files. The `global.json`/`global.json.bak` change in the working tree belongs to plan 01-01 and was not touched or staged.

## User Setup Required
None - no external service configuration required. The SPA calls the BFF at same-origin `/api` (dev proxy targets `http://localhost:5080`); end-to-end wiring with the backend is verified in plan 01-08.

## Next Phase Readiness
- PWA shell, i18n, and cookie API client are ready. Login/Dashboard shells target the BFF endpoints from plan 01-06 and will be verified end-to-end in plan 01-08.
- No blockers. Confirm the dev proxy target port matches the BFF's actual listening port when integrating (currently `5080`).

## Self-Check: PASSED

All 12 claimed files exist on disk; all 3 task commits (`4a26182`, `6025129`, `bca7625`) present in git history.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
