---
phase: 09-v1-feinschliff-compliance
plan: 02
subsystem: api
tags: [entitlements, tarif-gate, einvoicing, dunning, capability, hangfire, testing]

requires:
  - phase: 01-plattform-kern
    provides: PlanCapabilityMap + IEntitlementService (EInvoicing/Dunning are L+)
  - phase: 05-e-rechnung-engine
    provides: e-invoice endpoints + EnqueueEInvoiceOnFinalize
  - phase: 06-offene-posten-mahnwesen
    provides: dunning config + run endpoints
provides:
  - "Server-authoritative EInvoicing (L+) gate on GET xrechnung/zugferd + POST send-einvoice + suppressed finalize auto-enqueue for non-EInvoicing tenants"
  - "Server-authoritative Dunning (L+) gate on GET/PUT /api/dunning/config + POST /api/dunning/run"
  - "Testable gate seam: internal-static HasCapabilityAsync predicate + internal UpgradeRequired() on both endpoint classes"
affects: [09-05, 09-06]

tech-stack:
  added: []
  patterns:
    - "Tarif gate as an internal-static HasCapabilityAsync(IEntitlementService, ct) predicate + internal UpgradeRequired() 403 — the single testable seam every gated handler shares (mirrors RecurringInvoiceEndpoints)"
    - "Shared FakeEntitlementService (Granting / Denying / GrantingOnly) test double"

key-files:
  created:
    - tests/Numera.IntegrationTests/EInvoiceGateTests.cs
    - tests/Numera.IntegrationTests/DunningGateTests.cs
    - tests/Numera.IntegrationTests/FakeEntitlementService.cs
  modified:
    - src/Numera.Api/Endpoints/EInvoiceEndpoints.cs
    - src/Numera.Api/Endpoints/DunningEndpoints.cs
    - src/Numera.Api/Events/EnqueueEInvoiceOnFinalize.cs
    - tests/Numera.IntegrationTests/EInvoiceOutboundTests.cs
    - tests/Numera.IntegrationTests/DunningRunTests.cs

key-decisions:
  - "Core finalize + §14 PDF + e-mail stay ALL-TIER; only e-invoice ARTIFACTS + dunning are L+ (locked 09-CONTEXT §3)"
  - "No app-level HTTP test infra exists, so the endpoint gate was extracted into a testable static predicate rather than adding a WebApplicationFactory; the auto-enqueue suppression (the real 'no document_einvoice' guarantee) is proven directly at the EnqueueEInvoiceOnFinalize handler"
  - "PlanCapabilityMap unchanged (EInvoicing/Dunning stay L+); no Program.cs change (handler + IEntitlementService already scoped-registered)"

patterns-established:
  - "Gate predicate + UpgradeRequired 403 as internal-static testable seam"

duration: ~40min
completed: 2026-08-02
---

# Phase 09-02: Tarif-gate server enforcement (EInvoicing + Dunning, L+)

**Server-authoritative EInvoicing/Dunning (L+) gates on the e-invoice + dunning surfaces and the finalize auto-enqueue, with core finalize/PDF/email untouched — the authoritative enforcement behind the 09-05 UpgradeHints.**

## Performance

- **Duration:** ~40 min (production landed pre-cutoff; tests + refactor completed by orchestrator)
- **Tasks:** 3
- **Files created:** 3 | **modified:** 5

## Accomplishments
- EInvoicing (L+) enforced on `GET /{id}/xrechnung`, `GET /{id}/zugferd`, `POST /{id}/send-einvoice` → 403 upgrade; the finalize auto-enqueue (`EnqueueEInvoiceOnFinalize`) is skipped for non-EInvoicing tenants so NO `document_einvoice` is produced, while finalize/PDF/email proceed for all tiers.
- Dunning (L+) enforced on `GET`/`PUT /api/dunning/config` and `POST /api/dunning/run` → 403 upgrade.
- Each inline gate refactored into an `internal static HasCapabilityAsync(...)` predicate + `internal UpgradeRequired()` (mirrors `RecurringInvoiceEndpoints`), making the gate a unit-testable seam.
- Gate tests (7/7): the gate queries the correct capability specifically (a different granted capability does not open it), `UpgradeRequired()` is 403, auto-enqueue is skipped/fires by tier, and `RunAsync` 403s + enqueues nothing (throwing job client) for a non-Dunning tenant on real postgres.

## Task Commits

1. **Task 1: Enforce EInvoicing + suppress non-L auto-enqueue** — `9026a6e` (feat)
2. **Task 2: Enforce Dunning on config + run** — `e34dc1d` (feat)
3. **Task 3: Gate tests + testable-seam refactor + caller fixes** — `60a89e0` (test)

## Files Created/Modified
- `EInvoiceEndpoints.cs` / `DunningEndpoints.cs` — gate predicate + internal `UpgradeRequired()`
- `EnqueueEInvoiceOnFinalize.cs` — injects `IEntitlementService`, skips enqueue without EInvoicing
- `EInvoiceGateTests.cs` / `DunningGateTests.cs` — the tarif-gate proofs
- `FakeEntitlementService.cs` — shared `Granting`/`Denying`/`GrantingOnly` double
- `EInvoiceOutboundTests.cs` / `DunningRunTests.cs` — updated the two callers whose signatures changed

## Decisions Made
See key-decisions. The notable adaptation: with no HTTP test harness in the suite, the endpoint gate was made testable as a static predicate instead of introducing WebApplicationFactory; the behaviorally-critical "no artifact for non-L finalize" is proven at the handler.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Integration test project no longer compiled**
- **Found during:** Task 3 (orchestrator resumption after limit cut-off)
- **Issue:** Task 1/2 changed `EnqueueEInvoiceOnFinalize`'s ctor and `DunningEndpoints.RunAsync`'s signature; the pre-existing `EInvoiceOutboundTests` + `DunningRunTests` callers were not updated before the agent was cut off → CS7036 build break blocking the whole suite.
- **Fix:** Updated both callers to pass `FakeEntitlementService.Granting`.
- **Verification:** Suite builds 0/0; EInvoiceOutbound + DunningRun 6/6.
- **Committed in:** `60a89e0`

**2. [Rule 1 - Testability] Inline gate not reachable by any test**
- **Found during:** Task 3
- **Issue:** The gate was inlined in minimal-API lambdas; no HTTP harness exists to reach it.
- **Fix:** Extracted `internal static HasCapabilityAsync` + `internal UpgradeRequired()` on both endpoint classes (the established `RecurringInvoiceEndpoints` pattern).
- **Verification:** Gate tests 7/7.
- **Committed in:** `60a89e0`

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 testability)
**Impact on plan:** Necessary to deliver the plan's Task-3 proofs; no scope creep — enforcement behavior is exactly as specified.

## Issues Encountered
The executing agent hit a session/usage limit mid-Task-3; the orchestrator verified the committed production code, fixed the build, added the gate tests via a testable-seam refactor, and verified.

## Next Phase Readiness
- 09-05 can render tier-aware UpgradeHints backed by these real 403s. 09-06 will re-run the full battery.

---
*Phase: 09-v1-feinschliff-compliance*
*Completed: 2026-08-02*
