---
phase: 05-e-rechnung-engine
plan: 02
subsystem: infra
tags: [kosit, xrechnung, validation, docker-compose, httpclient, en16931, e-rechnung]

# Dependency graph
requires:
  - phase: 04-pdf-versand
    provides: the Mailpit docker-compose sidecar + typed-options/DI/HttpClient conventions (EmailOptions, IEmailSender) mirrored here
  - phase: 05-e-rechnung-engine (05-01)
    provides: the Sales module EInvoice/ folder (mapper + XRechnung generator) whose XML this validator will gate in 05-03
provides:
  - kosit-validator docker-compose sidecar (pinned XRechnung-3.0.2 daemon, HTTP host :8081)
  - EInvoiceValidationStatus canonical enum (Accepted=0/Rejected=1/Unavailable=2, append-only ordinals) — the single definition 05-03 + 05-05 import
  - IEInvoiceValidator seam + EInvoiceValidationResult/EInvoiceFinding records (the validation contract 05-03/05-05 consume)
  - KoSitValidatorClient typed HttpClient (POST xml → parse report; outage ≠ rejection)
  - KoSitReport.Parse namespace-tolerant VARL parser with a DE/EN rule-id explanation map
affects: [05-03 two-stage validation gate, 05-05 inbound validation]

# Tech tracking
tech-stack:
  added:
    - "easybill/kosit-validator-xrechnung_3.0.2:v0.2.7 (KoSIT Validator JAR 1.5.0 + config XRechnung 3.0.2 / release 2025-07-09 / schematron 2.4.0) as a docker-compose sidecar"
  patterns:
    - "Typed HttpClient (AddHttpClient<IEInvoiceValidator, KoSitValidatorClient>) bound to a typed-options section, mirroring EmailOptions"
    - "Verdict rides on the HTTP status AND body: 200=accept / 406=reject both carry a VARL report; only a non-report body is an outage"
    - "Namespace-tolerant XML parsing (match by local-name) to survive config-version prefix/wrapper drift"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/EInvoice/EInvoiceValidationStatus.cs
    - src/Numera.Api/Services/EInvoiceValidationOptions.cs
    - src/Numera.Api/Services/IEInvoiceValidator.cs
    - src/Numera.Api/Services/KoSitValidatorClient.cs
    - src/Numera.Api/Services/KoSitReport.cs
    - tests/Numera.Platform.Tests/Sales/KoSitReportTests.cs
  modified:
    - docker-compose.yml
    - src/Numera.Api/appsettings.json
    - src/Numera.Api/Program.cs
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj

key-decisions:
  - "EInvoiceValidationStatus is the ONE canonical enum in the Sales module (Accepted=0/Rejected=1/Unavailable=2, append-only) — 05-03/05-05 + the Api result all import it; no re-declaration"
  - "The research-named apps4everything/kosit-docker:1.6.0 image does NOT exist on Docker Hub; substituted the real, pullable, explicitly-tagged easybill/kosit-validator-xrechnung_3.0.2:v0.2.7 (same XRechnung 3.0.2 production ruleset, JAR 1.5.0, config 2025-07-09)"
  - "The KoSIT daemon signals reject via HTTP 406 (accept via 200) — both carry a VARL report, so the client decides on the BODY (KoSitReport.IsReport), never on the status; a non-report body is Unavailable"
  - "The pure parser + public result records live in Numera.Api/Services (per plan must-haves); Numera.Platform.Tests references Numera.Api to unit-test them without a sidecar (mirrors IntegrationTests→Api, 03-11)"

patterns-established:
  - "KoSIT report parsing: read <rep:assessment>/<rep:accept|reject> for the verdict, <rep:message level=… code=…> for findings, enrich known rule ids with DE(authoritative)+EN, fall back to the raw message"
  - "Outage-vs-rejection: connection refused / timeout / non-report body → Unavailable; only an actual validator verdict → Accepted/Rejected"

# Metrics
duration: 40min
completed: 2026-07-26
---

# Phase 5 Plan 02: KoSIT Validation Engine Summary

**Government-authoritative XRechnung validation stood up as a pinned KoSIT docker-compose sidecar (daemon HTTP :8081) behind a typed IEInvoiceValidator that POSTs e-invoice XML and parses the KoSIT VARL report into an Accepted/Rejected/Unavailable verdict with DE/EN-explained findings — outage cleanly distinguished from rejection.**

## Performance

- **Duration:** ~40 min (incl. Docker Desktop cold-start + KoSIT image pull)
- **Started:** 2026-07-26T21:00:00Z
- **Completed:** 2026-07-26T19:35:17Z (local 21:35)
- **Tasks:** 3
- **Files modified/created:** 10

## Accomplishments
- Added the `kosit-validator` sidecar to `docker-compose.yml` (mirroring Mailpit): a pinned, explicitly-tagged XRechnung-3.0.2 KoSIT daemon, verified reachable on host `:8081` (`200` accept / `406` reject against the official KoSIT test invoice).
- Defined `EInvoiceValidationStatus` ONCE (Accepted=0/Rejected=1/Unavailable=2, append-only) in the Sales module — the single definition 05-03/05-05 + the Api result import.
- Built the `IEInvoiceValidator` seam (`EInvoiceValidationResult` + `EInvoiceFinding`) and `KoSitValidatorClient` typed HttpClient; a sidecar outage (refused/timeout/non-report body) surfaces as `Unavailable`, never a false `Rejected`.
- `KoSitReport.Parse`: a namespace-tolerant VARL parser (verdict from `<rep:assessment>`, findings from `<rep:message level/code>`) with a DE(authoritative)+EN rule-id explanation map (BR-DE-*/BR-CO-*) and raw-message fallback — golden-tested against the real captured report structure (RESEARCH Open Question 2 resolved).

## Task Commits

1. **Task 1: KoSIT validator sidecar + validation options** — `5c36b01` (chore) + fix `6af9c1d` (real image)
2. **Task 2: IEInvoiceValidator + KoSitValidatorClient + KoSitReport parser** — `7da96bc` (feat) + fix `38b332d` (HTTP 406 reject handling)
3. **Task 3: KoSitReport parser golden tests** — `1ead880` (test)

_Two mid-execution fixes (6af9c1d, 38b332d) were driven by the live sidecar — see Deviations._

## Files Created/Modified
- `docker-compose.yml` — `kosit-validator` service (easybill/kosit-validator-xrechnung_3.0.2:v0.2.7, daemon HTTP, host :8081→container 8080), with a comment that prod builds our own image from the pinned official JAR + config.
- `src/Numera.Api/appsettings.json` — `EInvoiceValidation` section (BaseUrl/ValidationPath/TimeoutSeconds/ConfiguredProfileVersion).
- `src/Numera.Api/Services/EInvoiceValidationOptions.cs` — typed options bound from that section (mirrors EmailOptions).
- `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceValidationStatus.cs` — the canonical 3-value enum.
- `src/Numera.Api/Services/IEInvoiceValidator.cs` — seam + `EInvoiceValidationResult`/`EInvoiceFinding` records.
- `src/Numera.Api/Services/KoSitValidatorClient.cs` — typed HttpClient → sidecar; outage ≠ rejection.
- `src/Numera.Api/Services/KoSitReport.cs` — pure VARL parser + `IsReport` gate + DE/EN rule map.
- `src/Numera.Api/Program.cs` — registered the typed client bound to the options BaseUrl+Timeout.
- `tests/Numera.Platform.Tests/Sales/KoSitReportTests.cs` — 12 pure golden tests (embedded accept/reject reports).
- `tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj` — ProjectReference to Numera.Api so the pure parser is unit-testable here.

## Decisions Made
- **Canonical enum home = Sales module**, ordinals fixed and append-only (05-05 frontend hardcodes the numbers; 05-03 artifacts key on them).
- **Image substitution** (see Deviation 1): real pullable XRechnung-3.0.2 daemon image instead of the non-existent research-named one.
- **Body-driven verdict** (see Deviation 2): the daemon returns HTTP 406 for reject, so the client trusts the report BODY, gated by `KoSitReport.IsReport`, never the status code.
- **Parser + result records stay in Numera.Api** (per plan must-haves); to unit-test the pure parser in `Numera.Platform.Tests` without a sidecar, that test project now references `Numera.Api` (same precedent as `Numera.IntegrationTests`→`Numera.Api`, 03-11). Golden reports are embedded as string constants in the test — no shared goldenfile-csproj wiring, which also avoids a concurrent-edit clash with 05-01.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] The pinned community image does not exist on Docker Hub**
- **Found during:** Task 1 (bringing the sidecar up)
- **Issue:** `apps4everything/kosit-docker:1.6.0` (the research/plan-named image, JAR 1.6.0 + config 2026-01-31) returns `pull access denied … repository does not exist`. It is not on Docker Hub.
- **Fix:** Pinned `easybill/kosit-validator-xrechnung_3.0.2:v0.2.7` — a real, explicitly-tagged, multi-arch XRechnung-3.0.2 daemon image (verified bundling `Validator Configuration XRechnung 3.0.2`, config release 2025-07-09, schematron 2.4.0, KoSIT Validator JAR 1.5.0). Same **XRechnung 3.0.2** production ruleset (the legally load-bearing pin); the config point-release + JAR minor differ from the plan's intended pin. Compose comment + `ConfiguredProfileVersion` updated to the truth; the "prod builds our own image from the pinned official JAR + config" guidance retained as the reproducibility seam.
- **Files modified:** docker-compose.yml, src/Numera.Api/appsettings.json, src/Numera.Api/Services/EInvoiceValidationOptions.cs
- **Verification:** `docker compose config` parses; `docker compose up -d kosit-validator` starts; daemon answers on :8081 (405 on GET, 200/406 on POST).
- **Committed in:** 6af9c1d

**2. [Rule 1 - Bug] The daemon signals rejection with HTTP 406, which the client mis-mapped to Unavailable**
- **Found during:** Task 2 (live round-trip against the sidecar)
- **Issue:** The plan's client logic parsed only on HTTP success and mapped every non-2xx to `Unavailable`. But the live KoSIT daemon returns **`200 OK` for accept and `406 Not Acceptable` for reject**, both with a full VARL `<rep:report>` body. The original logic would have reported EVERY rejected invoice as a service outage — an inverted Pitfall 6.
- **Fix:** The client now reads the response body and, via a new `KoSitReport.IsReport` gate (root is the VARL `report` element), parses ANY response that is a report (accept or reject) regardless of status; only a missing/non-report body (5xx page, empty, timeout, connection refused) is `Unavailable`. `IsReport` also prevents a stray 200 with unrelated XML from being trusted as a false `Accepted`.
- **Files modified:** src/Numera.Api/Services/KoSitValidatorClient.cs, src/Numera.Api/Services/KoSitReport.cs
- **Verification:** curl proved 200/`valid="true"`/`<rep:accept>` for the official KoSIT test invoice and 406/`valid="false"`/`<rep:reject>`/`BR-DE-15` for a copy with `BuyerReference` (BT-10) removed; KoSitReportTests (incl. IsReport report-vs-non-report cases) pass.
- **Committed in:** 38b332d

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both essential for correctness — the sidecar could not run without a real image, and rejections would otherwise be silently misreported as outages. No scope creep; the seam shape (IEInvoiceValidator/result records) is exactly as planned.

## Issues Encountered
- **Docker Desktop was not running** at start (Linux engine pipe absent) despite the coordination note — launched `Docker Desktop.exe`, waited for the engine, then pulled + ran the sidecar. Resolved.
- **Open Question 2 (report XPaths) resolved:** captured a real accept report AND a real reject report from the pinned daemon by POSTing the official KoSIT `xrechnung-testsuite` invoice; confirmed the VARL structure (`<rep:assessment>`/`<rep:accept|reject>`, `<rep:message level=… code=…>text</rep:message>`) and that the large embedded XHTML explanation carries NO colliding local-names — so the parser is safe on real output. The golden fixtures mirror this structure faithfully (BR-DE-15 message verbatim).

## User Setup Required
None - the sidecar is a local docker-compose service (no auth); production must build/point at its own pinned KoSIT image via `EInvoiceValidation:BaseUrl`.

## Next Phase Readiness
- The validation engine is callable from .NET with structured, human-readable results and robust outage handling — ready for 05-03 to wire into the two-stage send gate (pre-finalize dry-run + post-finalize send gate) and for 05-05 inbound validation. Both import the canonical `EInvoiceValidationStatus`.
- **Not this plan's coverage (by design):** proving a REAL generated XRechnung passes the LIVE validator (Accepted) is 05-03 Task 4's checked-in live-KoSIT conformance harness (which by wave 2 has both the 05-01 mapper output and this sidecar). The live client round-trip (incl. the Unavailable path) is likewise 05-03's integration path; this plan covers the pure parser + the manual curl round-trip.

## Self-Check: PASSED
- All 7 key artifacts exist on disk (verified).
- All 5 commits present (5c36b01, 7da96bc, 6af9c1d, 38b332d, 1ead880).
- Full platform suite green (87/87, incl. 12 new KoSitReport tests); solution builds 0 warnings (TreatWarningsAsErrors).

---
*Phase: 05-e-rechnung-engine*
*Completed: 2026-07-26*
