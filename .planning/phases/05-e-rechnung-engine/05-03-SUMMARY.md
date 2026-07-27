---
phase: 05-e-rechnung-engine
plan: 03
subsystem: api
tags: [xrechnung, kosit, e-invoice, validation, hangfire, rls, ubl, cii, en16931, cius]

# Dependency graph
requires:
  - phase: 05-e-rechnung-engine (05-01)
    provides: EInvoiceMapper + XRechnungGenerator (frozen InvoicePdfModel → UBL+CII bytes) + EInvoiceFormat enum
  - phase: 05-e-rechnung-engine (05-02)
    provides: IEInvoiceValidator + KoSitValidatorClient + EInvoiceValidationStatus + the pinned kosit-validator sidecar
  - phase: 04-pdf-versand
    provides: DocumentPdfService/RenderDocumentPdfJob/EnqueuePdfOnFinalize trio + DocumentEmail send seam mirrored here
  - phase: 03-belegkette-rechnungskern
    provides: FinalizeCoreAsync (§14 gate + numbering) + InvoiceFinalized domain event + frozen snapshot
provides:
  - "EInvoiceArtifact (document_einvoice) RLS entity + migration: generated XML bytes + ValidationStatus + jsonb report keyed per (document, format)"
  - "EInvoiceService: generate UBL+CII from the frozen snapshot → validate via KoSIT → persist idempotently, plus DryRunAsync (pre-finalize) and GetOrGenerate (render-if-absent)"
  - "GenerateEInvoiceJob (Hangfire, fresh scope + SetTenant) iterating EInvoiceService.FinalizeFormats — the single 05-04 extension point"
  - "EnqueueEInvoiceOnFinalize: InvoiceFinalized → enqueue only, Rechnung-only"
  - "Two-stage KoSIT gate: pre-finalize synchronous dry-run (blocks a non-conformant finalize before a number is burned; outage does not block) + post-finalize Accepted-only send gate"
  - "GET /api/documents/{id}/xrechnung?syntax=ubl|cii download + POST /{id}/send-einvoice"
  - "Live-KoSIT conformance harness: every VAT scenario's REAL generated UBL+CII proven Accepted by the real validator; 12 accept-reports committed as fixtures"
affects: [05-04 (ZUGFeRD PDF/A-3 appends to FinalizeFormats), 05-05 (inbound validation + frontend status ordinals)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Two-stage validation gate: structural dry-run on a provisional descriptor BEFORE numbering (422 strands no number); authoritative stored-status gate BEFORE the Versand"
    - "Outage never blocks finalize (Unavailable proceeds) but never permits a send (send gate requires Accepted) — RESEARCH Pitfall 6"
    - "Live-conformance loop: feed the REAL generated XRechnung to the REAL validator, let its findings name the missing CIUS field, fix the mapper, iterate (RESEARCH Pitfall 2)"
    - "Eager-format list (FinalizeFormats) is the one extension seam — 05-04 appends ZugferdPdfA3 with no job edit"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/EInvoice/EInvoiceArtifact.cs
    - src/platform/Numera.Platform.Db/Migrations/[timestamp]_DocumentEInvoice.cs
    - src/Numera.Api/Services/EInvoiceService.cs
    - src/Numera.Api/Jobs/GenerateEInvoiceJob.cs
    - src/Numera.Api/Events/EnqueueEInvoiceOnFinalize.cs
    - src/Numera.Api/Endpoints/EInvoiceEndpoints.cs
    - tests/Numera.IntegrationTests/EInvoiceOutboundTests.cs
    - tests/Numera.IntegrationTests/KoSitConformanceTests.cs
    - tests/Numera.IntegrationTests/Fixtures/KoSit/ (12 real accept-reports)
  modified:
    - src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
    - src/Numera.Api/Program.cs

key-decisions:
  - "document_einvoice keyed per (document, format), idempotent delete-then-add; ValidationStatus/report stored so the send gate + frontend read an authoritative verdict without re-validating"
  - "Pre-finalize dry-run validates a PROVISIONAL descriptor (placeholder number) so BR-DE-15/structural errors block finalize before the gapless number is burned; Unavailable proceeds"
  - "Live-conformance harness is trait-gated (Category=KositConformance) and SKIPS via a TCP probe when the sidecar is down — never a silent pass, never a hard fail"
  - "Mapper: seller+buyer SpecifiedLegalOrganization so UBL emits PartyLegalEntity/RegistrationName (BT-27/44); payment-terms description derived from the due date (no empty CII element); intra-community deliver-to = buyer address (BT-80)"

patterns-established:
  - "Pattern: the live validator's findings drive the mapper — checked-in accept-reports are the regression + provenance of the pinned KoSIT config"
  - "Pattern: EInvoiceMapper stays the single change-locus for CIUS conformance fixes"

# Metrics
duration: 45min
completed: 2026-07-27
---

# Phase 5 Plan 03: Outbound XRechnung Pipeline + Two-Stage KoSIT Gate Summary

**A finalized invoice's numbered XRechnung (UBL+CII) is generated from the frozen snapshot, validated against KoSIT, and stored under RLS in `document_einvoice` by a Hangfire job; a two-stage gate blocks a non-conformant finalize before the number is burned and blocks the Versand unless the stored status is Accepted; a checked-in live-KoSIT harness proves every VAT scenario's real UBL+CII is Accepted by the real validator.**

## Performance

- **Duration:** ~45 min for Task 4 continuation (Tasks 1-3 executed in a prior session)
- **Completed:** 2026-07-27
- **Tasks:** 4
- **Files modified:** ~11 across the plan

## Accomplishments
- **Persistence + generation (Task 1):** `EInvoiceArtifact`/`document_einvoice` (RLS: ENABLE+FORCE+tenant_isolation, jsonb report) + `EInvoiceService` (generate UBL+CII from the frozen model → validate → persist idempotently) + `GenerateEInvoiceJob` (fresh scope + SetTenant, iterates `FinalizeFormats`) + `EnqueueEInvoiceOnFinalize` (enqueue-only, Rechnung-only).
- **Two-stage gate + download (Task 2):** pre-finalize `DryRunAsync` blocks a structurally-invalid Rechnung with a 422 before numbering (outage proceeds); `GET /{id}/xrechnung?syntax=ubl|cii`; `POST /{id}/send-einvoice` refuses unless the stored `ValidationStatus == Accepted`.
- **Deterministic tests (Task 3):** generate/validate/persist + idempotency + RLS + both gate stages proven on real Postgres with a fake validator (no sidecar dependency).
- **Live conformance (Task 4):** `KoSitConformanceTests` feeds every VAT scenario (S / AE §13b / E §19 / K / G / Z) in BOTH UBL and CII to the REAL KoSIT sidecar and asserts Accepted with zero error findings — closing RESEARCH Pitfall 2. 12 real accept-reports committed under `Fixtures/KoSit/`. The harness skips cleanly (TCP probe) when the sidecar is absent.

## Task Commits

1. **Task 1: document_einvoice + EInvoiceService + job + enqueue-on-finalize** - `e366e90` (feat)
2. **Task 2: Two-stage gate — pre-finalize dry-run + send gate + XRechnung download** - `60ca3cb` (feat)
3. **Task 3: Integration tests — generate/validate/persist + both gate stages** - `f8e0be5` (test)
4. **Task 4: Live-KoSIT conformance harness + mapper CIUS fixes** - `051b4fc` (test)

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceArtifact.cs` - RLS artifact entity (XML + status + jsonb report per document+format).
- `src/platform/Numera.Platform.Db/Migrations/*_DocumentEInvoice.cs` - document_einvoice table + hand-written RLS + jsonb report column.
- `src/Numera.Api/Services/EInvoiceService.cs` - generate→validate→persist + DryRunAsync + GetOrGenerate + FinalizeFormats.
- `src/Numera.Api/Jobs/GenerateEInvoiceJob.cs` - Hangfire job iterating FinalizeFormats.
- `src/Numera.Api/Events/EnqueueEInvoiceOnFinalize.cs` - InvoiceFinalized → enqueue (Rechnung-only).
- `src/Numera.Api/Endpoints/EInvoiceEndpoints.cs` - xrechnung download + send-einvoice gate.
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` - pre-finalize dry-run wired into the finalize handler.
- `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs` - Task-4 CIUS conformance fixes (see Deviations).
- `tests/Numera.IntegrationTests/EInvoiceOutboundTests.cs` - deterministic fake-validator suite.
- `tests/Numera.IntegrationTests/KoSitConformanceTests.cs` - live-KoSIT harness (trait-gated, skippable).
- `tests/Numera.IntegrationTests/Fixtures/KoSit/*.xml` - 12 real KoSIT accept-reports (regression + config provenance: KoSIT Validator 1.5.0, XRechnung 3.0.2).

## Decisions Made
- The live harness is a CI-gated / manually-triggered job (needs the Java sidecar), NOT part of the fast unit gate — but it is checked-in and repeatable, not a one-time human action. Run: `dotnet test --filter Category=KositConformance` with the `kosit-validator` sidecar up.
- The captured accept-reports double as the pinned-config provenance (each report footer names the KoSIT Validator version + XRechnung ruleset).

## Deviations from Plan

### Auto-fixed Issues (Task 4 — the live-conformance loop)

The plan explicitly anticipated this: "editing the mapper here IS expected and allowed; that is the whole point of the loop." The first live run rejected every scenario; the validator's findings named exactly which CIUS fields were missing, each fixed in `EInvoiceMapper`.

**1. [Rule 1 - Bug] UBL missing BT-27/BT-44 legal name (BR-06 / BR-07)**
- **Found during:** Task 4 (first live run — all UBL scenarios rejected)
- **Issue:** The ZUGFeRD-csharp 18 UBL writer emits `cac:PartyName/cbc:Name` (BT-28 trading name) but only writes the REQUIRED `cac:PartyLegalEntity/cbc:RegistrationName` (BT-27 seller / BT-44 buyer) when the party carries a legal organization — so the seller/buyer legal name was absent and KoSIT rejected. (CII already carried `ram:Name`, so CII was unaffected.)
- **Fix:** Set `desc.Seller.SpecifiedLegalOrganization` and `desc.Buyer.SpecifiedLegalOrganization` with `TradingBusinessName` = the frozen legal name, so `PartyLegalEntity/RegistrationName` is emitted from the same frozen field.
- **Files modified:** src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs
- **Verification:** all UBL scenarios now Accepted; 05-01 golden tests still 87/87.
- **Committed in:** `051b4fc`

**2. [Rule 1 - Bug] CII empty `<ram:Description>` (PEPPOL-EN16931-R008)**
- **Found during:** Task 4 (first live run — all CII scenarios rejected)
- **Issue:** `AddTradePaymentTerms(null, dueDate)` made the CII writer emit an empty `<ram:Description>` element for the payment terms; PEPPOL-EN16931-R008 forbids empty elements.
- **Fix:** Supply a BT-20 payment-terms description derived from the frozen due date (`"Zahlbar ohne Abzug bis {dd.MM.yyyy}."`) — nothing invented beyond the fixed template around the frozen date.
- **Files modified:** src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs
- **Verification:** all CII scenarios now Accepted with 0 errors.
- **Committed in:** `051b4fc`

**3. [Rule 2 - Missing Critical] Intra-community deliver-to country BT-80 (BR-IC-12)**
- **Found during:** Task 4 (K scenario)
- **Issue:** For an intra-community supply (category K), EN 16931 BR-IC-12 requires the Deliver-to country code (BT-80) to be present; it was blank.
- **Fix:** For invoices carrying a K breakdown/line, default `desc.ShipTo` to the frozen buyer address (goods delivered to the buyer), which carries BT-80. Emitted only for intra-community — other categories keep no BG-13.
- **Files modified:** src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs
- **Verification:** K/UBL and K/CII now Accepted.
- **Committed in:** `051b4fc`

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 missing-critical) — all in the mapper, all driven by the real validator's findings (the intended Pitfall-2 loop).
**Impact on plan:** No scope creep; these are exactly the CIUS-mandatory-field closures the plan set out to prove. The mapper stays the single conformance change-locus.

## Issues Encountered
- The partial harness left on disk from a prior cut-off session was intact and API-correct; it was kept as-is (it compiled and its `KoSitProbe`/`KositFact`/`KositTheory` skip-gates and env-driven fixture capture were exactly the design the plan called for). No rewrite needed.

## User Setup Required
None for the deterministic suites. The live-conformance harness needs the `kosit-validator` docker-compose sidecar (host :8081); it skips cleanly when absent. `docker compose up -d kosit-validator` to run it locally.

## Next Phase Readiness
- **05-04** appends `EInvoiceFormat.ZugferdPdfA3` to `EInvoiceService.FinalizeFormats` and reuses `XRechnungGenerator.GenerateCiiForZugferd` — the job needs no edit. The now-conformant mapper output is the CII the PDF/A-3 will embed.
- **05-05** (inbound + frontend) imports the fixed `EInvoiceValidationStatus` ordinals and can rely on `document_einvoice.ValidationStatus` as the authoritative verdict.
- The whole outbound pipeline is proven Accepted against the real government validator for all six VAT scenarios in both syntaxes — RESEARCH Pitfall 2 closed.

---
*Phase: 05-e-rechnung-engine*
*Completed: 2026-07-27*

## Self-Check: PASSED

- All key files (EInvoiceService, EInvoiceEndpoints, EInvoiceMapper, both test suites, 12 fixtures, SUMMARY) exist on disk.
- All 4 task commits (e366e90, 60ca3cb, f8e0be5, 051b4fc) present in git history.
- Full solution builds 0 warnings (TreatWarningsAsErrors); platform suite 87/87; integration suite 84/84 (72 deterministic + 12 live-KoSIT conformance, sidecar up).
