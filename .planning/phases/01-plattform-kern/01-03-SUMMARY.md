---
phase: 01-plattform-kern
plan: 03
subsystem: platform
tags: [money, decimal, vat, en16931, rounding, nodamoney, xunit, golden-files]

# Dependency graph
requires: []
provides:
  - "Numera.Platform.Money: decimal-backed Money value object (amount + ISO-4217 currency)"
  - "TaxCategory enum (EN 16931 codes S/AE/K/E/Z/G/O)"
  - "RoundingPolicy.RoundTax — per-category VAT rounding, half-away-from-zero"
  - "RoundingPolicy.DocumentVatTotal — per-category-round-then-sum document total"
  - "Golden-file VAT rounding regression suite (mixed rates, allowance, reverse-charge, midpoint)"
affects: [rechnungen, e-rechnung, buchhaltung, invoicing-engine, xrechnung, zugferd]

# Tech tracking
tech-stack:
  added: [NodaMoney 2.7.0, xUnit 2.9.2, Microsoft.NET.Test.Sdk 17.11.1]
  patterns:
    - "Central rounding authority: all VAT math routes through RoundingPolicy"
    - "Golden-file regression testing with hand-computed JSON fixtures"
    - "readonly record struct value objects, decimal-only monetary arithmetic"

key-files:
  created:
    - src/platform/Numera.Platform.Money/Money.cs
    - src/platform/Numera.Platform.Money/TaxCategory.cs
    - src/platform/Numera.Platform.Money/RoundingPolicy.cs
  modified:
    - tests/Numera.Platform.Tests/Money/RoundingPolicyTests.cs

key-decisions:
  - "MidpointRounding.AwayFromZero (kaufmaennisch) locked as VAT rounding mode, never banker's ToEven"
  - "Document VAT total = sum of per-category rounded amounts, never round(grand_total) — EN 16931 BR-CO-14"
  - "Target net8.0 for now (only .NET 8 SDK installed); Money/RoundingPolicy are TFM-agnostic, retarget net10.0 when SDK provisioned"
  - "NodaMoney validates ISO-4217 currency identity; rounding math stays on raw decimal"

patterns-established:
  - "RoundingPolicy is the single source of truth for VAT rounding — no ad-hoc Math.Round in callers"
  - "Golden-file fixtures with hand-computed expected values lock legally load-bearing behavior"

# Metrics
duration: ~30min
completed: 2026-07-10
---

# Phase 1 Plan 03: EN 16931 Money + VAT Rounding Summary

**Decimal-only Money value object with a central RoundingPolicy that rounds VAT per category half-away-from-zero and sums per-category rounded amounts for the document total, locked by a 7-test golden-file regression suite.**

## Performance

- **Duration:** ~30 min (this session; plan was previously interrupted at WIP)
- **Started:** 2026-07-10T01:10:00Z (approx, this session)
- **Completed:** 2026-07-10T01:39:00Z
- **Tasks:** 2 commits (GREEN implementation + test-fixture fix); RED test artifacts pre-existed in WIP
- **Files modified:** 4 (3 created, 1 modified)

## Accomplishments

- `Money` readonly record struct: exact `decimal Amount` + ISO-4217 `CurrencyCode` (NodaMoney-validated), `+`/`-`/`*` operators, cross-currency addition throws. No float/double anywhere.
- `TaxCategory` enum with the EN 16931 codes (S/AE/K/E/Z/G/O).
- `RoundingPolicy.RoundTax(base, rate)` = `Math.Round(base * rate / 100m, 2, MidpointRounding.AwayFromZero)` — kaufmaennische Rundung, documented against EN 16931 BR-S-08 / BR-CO-17.
- `RoundingPolicy.DocumentVatTotal(buckets)` sums per-category `RoundTax` results (never `round(grand_total)`), with a `TaxBucket` record for bucketing.
- All 7 golden-file / value-object tests pass (GREEN). The midpoint case (0.125 -> 0.13) proves AwayFromZero, not banker's ToEven (0.12); the mixed-rate case proves per-category-then-sum (6.31) diverges from naive round(6.3192)=6.32.

## Task Commits

1. **GREEN: implement Money + TaxCategory + RoundingPolicy** - `4e0f678` (feat)
2. **Fix test namespace collision** - `f14d356` (fix)

**Plan metadata:** see final docs commit.

_RED artifacts (RoundingPolicyTests.cs + 4 golden JSON fixtures) were already committed in the interrupted WIP commit d5e3b22 and re-verified as failing (CS0234, types absent) before implementation._

## Files Created/Modified

- `src/platform/Numera.Platform.Money/Money.cs` - decimal-backed Money value object; XML comments document numeric(19,4)/numeric(19,6) storage mapping for later phases.
- `src/platform/Numera.Platform.Money/TaxCategory.cs` - EN 16931 tax category enum.
- `src/platform/Numera.Platform.Money/RoundingPolicy.cs` - RoundTax + DocumentVatTotal + TaxBucket; XML comment cites BR-S-08/BR-CO-17 and RESEARCH Open Question 1 (re-lock against KoSIT reference invoices in the e-invoicing phase).
- `tests/Numera.Platform.Tests/Money/RoundingPolicyTests.cs` - fully-qualified the `Money` type references (namespace collision fix).

## Decisions Made

- **AwayFromZero locked** as the VAT rounding mode — legally load-bearing, banker's rounding causes future KoSIT rejections.
- **Per-category-round-then-sum** for the document VAT total — never round(grand_total). Un-retrofittable once amounts persist.
- **net8.0 target retained** from the WIP scaffold. The .NET 10 SDK (global.json pins 10.0.301) is not installed in this environment — only 8.0.303 exists. The Money type and RoundingPolicy are TFM-agnostic and byte-identical across TFMs, so the golden-file regression net is fully valid on net8.0. Retarget to net10.0 when the LTS SDK is provisioned (owned by plan 01-01's global.json / Directory.Build.props).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] .NET 10 SDK unavailable — built/tested against net8.0**
- **Found during:** RED verification (initial build)
- **Issue:** `global.json` pins SDK 10.0.301 with `allowPrerelease: false`; only SDK 8.0.303 is installed, so *every* `dotnet` command failed to resolve an SDK ("A compatible .NET SDK was not found").
- **Fix:** Kept the WIP scaffold's net8.0 target (already documented in the csproj comment). For each build/test invocation, temporarily moved the root `global.json` aside and restored it immediately after, so the committed file is untouched. `global.json` and `Directory.Build.props` (which forces net10.0) are owned by plan 01-01 and out of this plan's file scope, so they were not modified.
- **Files modified:** None persisted (transient global.json move/restore only).
- **Verification:** `dotnet test -c Release` → 7/7 pass on net8.0; global.json confirmed clean in `git status` after each run.
- **Committed in:** N/A (no file change).

**2. [Rule 1 - Bug] Test fixture referenced `Money.Money` ambiguously**
- **Found during:** GREEN verification (first test build after adding implementation)
- **Issue:** The test namespace `Numera.Platform.Tests.Money` shadowed the bare `Money` identifier, so `new Money.Money(...)` bound to the enclosing namespace segment instead of the `Numera.Platform.Money.Money` type → CS0234, uncompilable as written.
- **Fix:** Fully-qualified the three `Money.Money(...)` construction sites to `Numera.Platform.Money.Money`. Unambiguous references (`RoundingPolicy`, `TaxBucket`, `TaxCategory`) left as-is.
- **Files modified:** tests/Numera.Platform.Tests/Money/RoundingPolicyTests.cs
- **Verification:** Test project compiles; 7/7 tests pass.
- **Committed in:** `f14d356`

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both necessary to make the plan executable/verifiable. No scope creep — deliverables (Money, TaxCategory, RoundingPolicy, golden-file suite) match the plan exactly. The net8.0 target is a documented, reversible provisioning constraint, not a design change.

## Issues Encountered

- Stale `obj/Release/net8.0` build artifacts from the interrupted session were cleaned (bin/obj are gitignored, so nothing was committed) before rebuilding.

## User Setup Required

None for this plan's deliverables. Environment note for the team: provision the **.NET 10 SDK (10.0.301)** to satisfy `global.json`; until then, all `dotnet` commands require the global.json to be absent or retargeted. Once provisioned, retarget `Numera.Platform.Money` and `Numera.Platform.Tests` to `net10.0` (they inherit from `Directory.Build.props` once their local `TargetFramework` overrides are removed).

## Next Phase Readiness

- The exact-money foundation is in place: the invoicing engine (Phase 3/5) plugs into `RoundingPolicy` and the golden-file suite.
- AwayFromZero and per-category-then-sum are the regression net; both are to be RE-LOCKED against KoSIT-validated reference invoices in the e-invoicing phase (RESEARCH Open Question 1).
- Blocker to clear before/at Phase build automation: .NET 10 SDK provisioning + retarget to net10.0.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*

## Self-Check: PASSED

- All created/modified files exist on disk.
- Both task commits (4e0f678, f14d356) exist in git history.
- `dotnet test -c Release` → 7/7 pass (net8.0); verification greps confirm `MidpointRounding.AwayFromZero`, per-category `Sum(`, and no `float`/`double` in the Money project code.
