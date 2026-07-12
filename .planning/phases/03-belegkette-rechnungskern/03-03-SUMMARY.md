---
phase: 03-belegkette-rechnungskern
plan: 03
subsystem: api
tags: [vat, en16931, rounding, pflichttext, tdd, golden-files, kleinunternehmer, reverse-charge]

# Dependency graph
requires:
  - phase: 01-plattform-kern
    provides: RoundingPolicy (per-category half-away-from-zero + DocumentVatTotal), TaxCategory enum, Money
  - phase: 03-belegkette-rechnungskern (03-01)
    provides: Numera.Modules.Sales project + wiring, CompanyProfile.IsKleinunternehmer issuer flag
provides:
  - VatCalculationService — the single VAT bucketing + BG-23 breakdown authority (pure, stateless)
  - VatLineInput / VatBreakdownRow record structs (VAT input + output contract)
  - Pflichttext map — category -> (BT-121 VATEX code, BT-120 German note) for §13b / intra-EU / §19 / export
  - Golden-file VAT test suite (8 fixtures) locking arithmetic + legal texts
affects: [03-05-finalize, phase-04-pdf, phase-05-e-invoice]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pure calculation service (static, no DbContext/IO) as the single VAT authority — reusable by finalize + PDF + e-invoice"
    - "Golden-file JSON fixtures copied next to the test assembly; one [Theory] over files + targeted [Fact] divergence guards"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/Vat/VatCalculationService.cs
    - src/modules/Numera.Modules.Sales/Vat/Pflichttext.cs
    - src/modules/Numera.Modules.Sales/Vat/VatLineInput.cs
    - src/modules/Numera.Modules.Sales/Vat/VatBreakdownRow.cs
    - tests/Numera.Platform.Tests/Sales/VatCalculationServiceTests.cs
    - tests/Numera.Platform.Tests/Sales/goldenfiles/*.json (8 fixtures)
  modified:
    - tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj

key-decisions:
  - "Only category S is taxed; AE/K/E/Z/G/O are all forced to effective rate 0 / tax 0 (single IsTaxed predicate)"
  - "Kleinunternehmer §19 is a document-wide override (issuer property), collapsing ALL lines into one category-E exempt row"
  - "Document VAT total = sum of per-category rows' already-rounded TaxAmount — never re-rounds a grand total"
  - "VATEX-EU-* code strings stored now but MEDIUM confidence (Phase-5 re-verify); the German note text is the load-bearing part for the Phase-4 PDF"

patterns-established:
  - "VAT is computed by ONE authority (VatCalculationService), never inline in an endpoint (RESEARCH.md Q5 LOCKED)"
  - "Bucketing key = (Category, EffectiveRate) via GroupBy; per-category RoundingPolicy.RoundTax then sum"

# Metrics
duration: 9min
completed: 2026-07-12
---

# Phase 3 Plan 03: VAT Breakdown + Pflichttext Service Summary

**Pure, TDD-driven `VatCalculationService` that buckets invoice lines into EN 16931 BG-23 rows by (category, rate), delegates all rounding to `RoundingPolicy` (per-category half-away-from-zero, then sum — never round the grand total), and attaches the correct VATEX exemption code + mandatory German Pflichttext for §13b / intra-EU / Kleinunternehmer §19 / export.**

## Performance

- **Duration:** ~9 min
- **Completed:** 2026-07-12
- **Tasks:** 2 (RED test, GREEN implementation; no REFACTOR needed)
- **Files modified:** 15 (4 source + 1 test + 8 golden fixtures + 1 csproj + 1 summary)

## Accomplishments
- `VatCalculationService.Calculate(lines, isKleinunternehmer)` — the single VAT authority: groups by (Category, EffectiveRate), taxes only category S via `RoundingPolicy.RoundTax`, zero-rates every other category with its Pflichttext, and `DocumentVatTotal` = sum of the rounded per-category amounts.
- `Pflichttext` static map: AE→VATEX-EU-AE (§13b), K→VATEX-EU-IC (innergem. Lieferung), E→VATEX-EU-D (§19), G→VATEX-EU-G (Ausfuhr), with the exact mandatory German note strings.
- Kleinunternehmer §19 document-wide override: collapses all lines (any category/rate) into a single category-E, rate-0, zero-tax row with the §19 note.
- 8 golden-file fixtures + 11 assertions covering single 19%, mixed 19/7, a true 19% midpoint (1.50 → 0.285 → 0.29 proving AwayFromZero over ToEven 0.28), document-level round-per-category-then-sum divergence (0.40 vs naive 0.39), and each exemption scenario.
- Service is pure/stateless (verified: no `DbContext`/EF reference, no ad-hoc `Math.Round` on totals) — directly reusable by 03-05 finalize and Phase 4/5.

## Task Commits

1. **Task 1 (RED): failing VAT breakdown golden files** - `9e6c737` (test)
2. **Task 2 (GREEN): VAT breakdown + Pflichttext service** - `01e794f` (feat)

_TDD: REFACTOR skipped — GREEN implementation was already clean (GroupBy bucketing, full delegation to RoundingPolicy); tests stayed green with no restructuring._

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/Vat/VatCalculationService.cs` - The BG-23 bucketing + breakdown authority
- `src/modules/Numera.Modules.Sales/Vat/Pflichttext.cs` - Category → (VATEX code, German note) map
- `src/modules/Numera.Modules.Sales/Vat/VatLineInput.cs` - Per-line VAT input record struct
- `src/modules/Numera.Modules.Sales/Vat/VatBreakdownRow.cs` - BG-23 output row record struct
- `tests/Numera.Platform.Tests/Sales/VatCalculationServiceTests.cs` - Golden-file suite (11 assertions)
- `tests/Numera.Platform.Tests/Sales/goldenfiles/*.json` - 8 scenario fixtures
- `tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj` - Copy Sales/goldenfiles/*.json to output

## Decisions Made
- **Only S is taxed.** A single `category == S` predicate drives both the effective rate (S keeps its rate; all others → 0) and the tax (S → RoundTax; all others → 0). Keeps the AE/K/E/Z/G/O zero-rate handling uniform.
- **Kleinunternehmer overrides per-line categories.** §19 is an issuer property, so the flag short-circuits into one exempt bucket over the sum of all nets — matches RESEARCH.md Pitfall 3.
- **Total from rows, not re-rounded.** `DocumentVatTotal` sums the rows' already-rounded `TaxAmount`, structurally preventing a `Math.Round(grandTotal)` (EN 16931 BR-CO-14). A dedicated golden case proves the 0.40-vs-0.39 divergence.
- **Real 19% midpoint fixture.** Used net 1.50 @ 19% = 0.285 (a genuine even-preceding-digit midpoint) so the AwayFromZero-vs-ToEven divergence is exercised at the actual standard rate, not an artificial rate.

## Deviations from Plan

None - plan executed exactly as written. (The plan allowed a REFACTOR commit "if needed"; it was not needed.)

## Issues Encountered
None. RED failed as expected (service absent → build error); GREEN passed all 11 assertions first run; full platform suite 54 green (43 prior + 11 new).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- VAT arithmetic + legal-text core of INV-04 is ready as a pure authority. Plan 03-05 (finalize) can now call `VatCalculationService.Calculate(...)` to compute the persisted document VAT breakdown and snapshot the Pflichttexte onto the invoice; Phase 4 (PDF) and Phase 5 (e-invoice/XML) share the same rows.
- Open follow-up (non-blocking): the `VATEX-EU-*` code strings are MEDIUM confidence — re-verify against the official EN 16931 VATEX list in Phase 5 (a data fill, not a schema change). The golden fixtures pin the current values so any change is deliberate.
- Coordination note honored: this plan touched ONLY `src/modules/Numera.Modules.Sales/Vat/` and `tests/Numera.Platform.Tests/Sales/` — no migration, model snapshot, entity, or rls_policies.sql changes (03-02's territory).

## Self-Check: PASSED

- All 4 source files + test file + SUMMARY.md present on disk
- 8 golden-file fixtures present
- Both task commits present in history (9e6c737 RED, 01e794f GREEN)
- Full platform test suite green (54 tests: 43 prior + 11 new)

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-12*
