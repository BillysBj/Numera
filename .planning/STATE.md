# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-09)

**Core value:** Ein Unternehmen erledigt seine komplette Auftrags- und Finanzverwaltung — von der Rechnung inkl. gesetzlicher E-Rechnung bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht) an einem Ort, auf jedem Gerät.
**Current focus:** Phase 1 — Plattform-Kern

## Current Position

Phase: 1 of 9 (Plattform-Kern) — ✓ COMPLETE + VERIFIED
Plan: 8 of 8 complete (01-01 … 01-08)
Status: Phase 1 verified (7/7 must-haves). Ready to plan Phase 2 (Stammdaten).
Last activity: 2026-07-10 — Phase verification passed after remediating 1 gap: Numera.Platform.Money + Numera.Platform.Tests were missing from Numera.sln, so the CI `dotnet test --no-build` step would have failed before ever reaching the RLS/audit hard gate. Both projects registered, Money retargeted net8.0→net10.0, full CI sequence replayed green (build 0/0, unit 16/16, integration 8/8) — commit 3912848.

Progress: [██████████] 100% (Phase 1 of 9)

## Performance Metrics

**Velocity:**
- Total plans completed: 8 (Phase 1: 01-01 … 01-08)
- Average duration: ~31 min (over 7 recorded plans; 01-03 not recorded)
- Total execution time: ~3.6 hours (recorded plans)

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-plattform-kern | 8 | ~218 min (7 recorded) | ~31 min |

**Per-Plan (Phase 01):**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| 01-01 | 35 min | 3 | 16 |
| 01-02 | 35 min | 2 | 10 |
| 01-03 | — (not recorded) | — | — |
| 01-04 | 10 min | 2 | 11 |
| 01-05 | 15 min | 2 | 10 |
| 01-06 | 45 min | 3 (+3 fixes) | 13 |
| 01-07 | 18 min | 3 | 14 |
| 01-08 | 60 min | 3 (2 code) | 7 |

**Recent Trend:**
- Last 5 plans: 10, 15, 45, 18, 60 min
- Trend: stable; 01-08 longer due to the human-verify checkpoint

*Updated after each plan completion*

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
- [Phase 01-plattform-kern]: 01-08: non-retrofittable foundations (tenant isolation, audit immutability, pool-safety) are proven on real postgres:18 via Testcontainers running as numera_app (NO BYPASSRLS) with IgnoreQueryFilters — a superuser silently bypasses FORCE RLS and would make isolation tests falsely pass; RLS+audit suites are non-continue-on-error CI steps (hard gates). CI validated locally only — first live GitHub Actions run pending a git remote
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
Stopped at: Phase 1 (Plattform-Kern) complete and verified — 8/8 plans, 7/7 must-haves, ROADMAP + REQUIREMENTS traceability updated (PLAT-01/02/04/05/06/07/09 = Complete). One verifier-found gap remediated (solution-registration of Money/unit-test projects; CI would have died before the RLS gate).
Open caveats: no git remote yet, so the GitHub Actions workflow has never run live — validated locally only. Docker stack (postgres + keycloak) left running. Keycloak BFF client secret lives in dotnet user-secrets, not in the repo.
Resume file: None
Next: /gsd:plan-phase 2 (Stammdaten — Kunden/Lieferanten + Produktkatalog).
