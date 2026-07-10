# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-09)

**Core value:** Ein Unternehmen erledigt seine komplette Auftrags- und Finanzverwaltung — von der Rechnung inkl. gesetzlicher E-Rechnung bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht) an einem Ort, auf jedem Gerät.
**Current focus:** Phase 1 — Plattform-Kern

## Current Position

Phase: 1 of 9 (Plattform-Kern)
Plan: 6 of 8 complete in current phase (01-01, 01-02, 01-03, 01-04, 01-05, 01-07)
Status: Executing phase 1 (wave 3)
Last activity: 2026-07-10 — Plan 01-04 complete (immutable append-only audit log: audit_events with REVOKE UPDATE/DELETE + BEFORE UPDATE OR DELETE trigger + RLS; ICurrentUser seam; synchronous in-transaction AuditWriter; verified live on postgres:18)

Progress: [███████░░░] 75%

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
| Phase 01-plattform-kern P01 | 35 | 3 tasks | 16 files |
| Phase 01-plattform-kern P02 | 35 | 2 tasks | 10 files |
| Phase 01-plattform-kern P02 | 35 | 2 tasks tasks | 10 files files |
| Phase 01-plattform-kern P05 | 15 | 2 tasks | 10 files |
| Phase 01-plattform-kern P04 | 10 | 2 tasks | 11 files |

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
- [Phase 01-plattform-kern]: 01-01: Tenant GUC via parameterized set_config interceptor + RESET on close (pool/injection-safe); EF global query filter mirrors RLS for every ITenantEntity
- [Phase 01-plattform-kern]: 01-01: Provisioned .NET SDK 10.0.301 user-local (machine had only 8.0.303); forced classic Numera.sln format over .NET 10 default .slnx
- [Phase 01-plattform-kern]: 01-02: tenants self-scoped by RLS on id (not ITenantEntity); FORCE RLS on every table + numera_app has NO BYPASSRLS makes RLS the unconditional primary isolation control
- [Phase 01-plattform-kern]: 01-02: shared migration in Platform.Db covers Modules.Ledger tables via reflective Numera.Modules.*.dll discovery (avoids circular ref); Api is the ef startup project
- [Phase 01-plattform-kern]: 01-05: tier entitlement = pure projection of tenants.plan; PlanCapabilityMap is single source of truth, Plan re-exports TenantPlan (no duplicate enum); server-authoritative FeatureManagement filter (auth ∧ tenant ∧ entitlement), frontend visibility cosmetic; no payment code; no migration (reads existing column)
- [Phase 01-plattform-kern]: 01-04: audit_events immutability is DB-enforced by TWO layers — REVOKE UPDATE/DELETE from numera_app (permission denied) + BEFORE UPDATE OR DELETE trigger raising 'audit_events is append-only' (unconditional, catches mis-grant/owner/superuser); app-code discipline deemed insufficient
- [Phase 01-plattform-kern]: 01-04: ICurrentUser seam lives in Platform.Tenancy (not Api) so Platform.Audit avoids an Api dependency; AuditWriter appends into the caller's DbContext with no own transaction/scope/SaveChanges (atomic with the recorded change); prev_hash/row_hash reserved nullable for a non-breaking GoBD hash-chain later

### Pending Todos

None yet.

### Blockers/Concerns

Offene Entscheidungen aus Research (nicht blockierend, aber vor betroffener Phase zu klären):
- Hosting-Entscheidung (Hetzner vs. UbiCloud Managed Postgres) — in Phase 1 entscheiden
- QuestPDF PDF/A-3b-Konformität — Spike in Phase 4/5 (Fallback iText)
- XRechnung 4.0 (Ende 2026 erwartet) — Upgrade-Pfad in Phase 5 bestätigen
- .NET 10 SDK (global.json pins 10.0.301) installed user-local; on machines where the shell defaults to SDK 8.0.303 for SDK subcommands, prepend %LOCALAPPDATA%\Microsoft\dotnet to PATH + set DOTNET_ROOT/DOTNET_MULTILEVEL_LOOKUP=0 (RESOLVED)
- Docker daemon running; postgres:18 comes up healthy after fixing the volume mount to /var/lib/postgresql. 01-02 RLS migration applied + isolation verified live (RESOLVED — was the 01-01 docker blocker)

## Session Continuity

Last session: 2026-07-10
Stopped at: Completed 01-04-PLAN.md (immutable append-only audit log: audit_events table with ENABLE+FORCE RLS + tenant_isolation, REVOKE UPDATE/DELETE + GRANT INSERT/SELECT to numera_app, BEFORE UPDATE OR DELETE trigger raising 'audit_events is append-only', reserved prev_hash/row_hash; ICurrentUser seam in Platform.Tenancy; synchronous in-transaction AuditWriter; verified live on postgres:18). 01-01, 01-02, 01-03, 01-05, 01-07 also complete. Remaining phase-1 plans: 01-06, 01-08.
Resume file: None
