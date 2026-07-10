# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-09)

**Core value:** Ein Unternehmen erledigt seine komplette Auftrags- und Finanzverwaltung — von der Rechnung inkl. gesetzlicher E-Rechnung bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht) an einem Ort, auf jedem Gerät.
**Current focus:** Phase 1 — Plattform-Kern

## Current Position

Phase: 1 of 9 (Plattform-Kern)
Plan: 0 of TBD in current phase
Status: Executing phase 1 (wave 1, parallel)
Last activity: 2026-07-10 — Plan 01-03 complete (Money value object + EN 16931 VAT rounding, golden-file suite, 7/7 tests pass)

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: — min
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**
- Last 5 plans: —
- Trend: —

*Updated after each plan completion*
| Phase 01 P07 | 18 | 3 tasks | 14 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: v1 = Rechnungen + E-Rechnung; compliance-kritischer Kern (Belegkette, E-Rechnung) landet vor erweiterten Rechnungstypen und CRM-Ausbau
- [Roadmap]: Nicht-nachrüstbare Fundamente (RLS-Isolation, Geldtyp, Audit — Phase 1; Unveränderbarkeit + Nummernvergabe — Phase 3) zuerst
- [Research]: Modularer Monolith (.NET 10 + ASP.NET Core), Postgres 18 mit RLS, React 19 PWA, ZUGFeRD-csharp + KoSIT-Validator-Sidecar
- [Phase 01-plattform-kern]: VAT rounding locked to MidpointRounding.AwayFromZero (kaufmaennisch); document total = sum of per-category rounded amounts (EN 16931 BR-CO-14), never round(grand_total)
- [Phase 01]: Plan 01-07: SPA holds no tokens — BFF HttpOnly session cookie carries auth via same-origin credentials:'include' fetch; PWA precaches app shell only and treats /api as NetworkOnly (no stale financial data)
- [Phase 01]: Plan 01-07: DE/EN i18n via react-i18next (German default/fallback); language persisted to localStorage + lng cookie so BFF/SSR agree on locale

### Pending Todos

None yet.

### Blockers/Concerns

Offene Entscheidungen aus Research (nicht blockierend, aber vor betroffener Phase zu klären):
- Hosting-Entscheidung (Hetzner vs. UbiCloud Managed Postgres) — in Phase 1 entscheiden
- QuestPDF PDF/A-3b-Konformität — Spike in Phase 4/5 (Fallback iText)
- XRechnung 4.0 (Ende 2026 erwartet) — Upgrade-Pfad in Phase 5 bestätigen
- .NET 10 SDK (global.json pins 10.0.301) not installed — only 8.0.303 present; Money project builds on net8.0 for now, retarget net10.0 when SDK provisioned

## Session Continuity

Last session: 2026-07-10
Stopped at: Completed 01-03-PLAN.md (Money + VAT rounding) and 01-07-PLAN.md (PWA shell + DE/EN i18n + BFF cookie client); plan 01-01 running in parallel
Resume file: None
