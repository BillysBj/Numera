---
phase: 09-v1-feinschliff-compliance
plan: 04
subsystem: testing
tags: [gobd, compliance, verfahrensdokumentation, docs, wording-guard, xunit]

requires:
  - phase: 03-belegkette-rechnungskern
    provides: invoice immutability, race-safe numbering, audit log
  - phase: 05-e-rechnung-engine
    provides: XRechnung/ZUGFeRD + KoSIT validation, inbound immutability
  - phase: 06-offene-posten-mahnwesen
    provides: append-only payment/allocation
provides:
  - "docs/gobd-verfahrensdokumentation.md — versioned 4-part GoBD Verfahrensdokumentation draft grounded in real Numera mechanisms"
  - "WordingComplianceTests — build-breaking guard against 'zertifiziert'/'certified' claims in locales + docs"
affects: [09-06, complete-milestone]

tech-stack:
  added: []
  patterns:
    - "Compliance-copy guard: a DB-free xUnit Theory scans real repo files (i18n locales + docs) and fails the build on a forbidden certification claim"

key-files:
  created:
    - docs/gobd-verfahrensdokumentation.md
    - tests/Numera.IntegrationTests/WordingComplianceTests.cs
  modified: []

key-decisions:
  - "Verfahrensdokumentation cites only already-implemented mechanisms (no invented controls); marked Entwurf/Draft + versioned per GoBD"
  - "Guard walks up to the repo root (Numera.sln) and reads live files, so a copy change is caught without rebuilding the web app; an 'at least one file scanned' Fact prevents a vacuous pass"
  - "'GoBD-konform' is used honestly; certification is never claimed — a Verfahrensdokumentation is a self-assessment"

patterns-established:
  - "Honest-wording regression guard as a first-class test"

duration: ~15min
completed: 2026-08-02
---

# Phase 09-04: GoBD Verfahrensdokumentation draft + wording guard

**Versioned 4-part GoBD Verfahrensdokumentation draft grounded in Numera's real compliance mechanisms, plus a build-breaking guard that keeps compliance copy honest ("GoBD-konform", never "zertifiziert").**

## Performance

- **Duration:** ~15 min (across a limit-interrupted wave)
- **Tasks:** 2
- **Files created:** 2

## Accomplishments
- `docs/gobd-verfahrensdokumentation.md` — Entwurf/Draft, versioned, four canonical parts (Allgemeine Beschreibung, Anwenderdokumentation/Belegweg, Technische Systemdokumentation, Betriebsdokumentation), citing real mechanisms: finalize-immutability (Storno instead of edit), race-safe sequential numbering, append-only audit log, append-only payments, immutable inbound originals, structured e-invoice XML as the record (BMF 2025-07-14), plus a DSGVO Art. 20 note pointing at `/api/export`.
- `WordingComplianceTests` — a Theory over `web/src/i18n/locales/**/*.json` + the Verfahrensdokumentation asserting no `zertifiziert`/`certified` claim, plus an "at least one file scanned" Fact. 30/30 green.
- Repo-wide audit (`web/src src README* docs`) surfaced no certification claim; the only matches are inside third-party binary DLLs.

## Task Commits

1. **Task 1: Author the 4-part Verfahrensdokumentation draft** — `ae63a1a` (docs)
2. **Task 2: Wording audit + guard test** — `2b5ca81` (test)

## Files Created/Modified
- `docs/gobd-verfahrensdokumentation.md` — versioned GoBD self-assessment draft
- `tests/Numera.IntegrationTests/WordingComplianceTests.cs` — certification-claim guard

## Decisions Made
None beyond plan — executed as specified.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
The executing agent was cut off by a session/usage limit mid-wave; the orchestrator verified the on-disk artefacts (docs already committed, test present), ran the guard (30/30), ran the audit (clean), and committed the test.

## Next Phase Readiness
- Success-criterion-4 docs/audit half delivered. The cross-tenant security half is 09-03. The final battery (09-06) will re-run this guard.

---
*Phase: 09-v1-feinschliff-compliance*
*Completed: 2026-08-02*
