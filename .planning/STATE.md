# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-09)

**Core value:** Ein Unternehmen erledigt seine komplette Auftrags- und Finanzverwaltung — von der Rechnung inkl. gesetzlicher E-Rechnung bis zur Buchhaltung — rechtskonform (GoBD, E-Rechnungspflicht) an einem Ort, auf jedem Gerät.
**Current focus:** Phase 2 — Stammdaten

## Current Position

Phase: 2 of 9 (Stammdaten) — IN PROGRESS
Plan: 3 of 7 complete (02-01, 02-02, 02-03)
Status: Phase 2 wave 3 delivered. Numera.Modules.Catalog exists — CatalogItem (article number, UN/ECE Rec 20 unit code, numeric(19,4) net/cost price, EN 16931 TaxCategory + VAT rate, archivable) with a curated UnitOfMeasure code helper. _Catalog migration (chained after _Crm) RLS-isolates catalog_items on real Postgres and enforces a per-tenant-unique article number among non-archived rows. Ready for 02-04.
Last activity: 2026-07-11 — Executed 02-03: created the Catalog data layer + _Catalog migration with per-table hand-written RLS + partial-unique article-number index, and the UnitOfMeasure UN/ECE Rec 20 helper. Build 0/0; integration 25/25 (7 new Catalog RLS/uniqueness/archive assertions); unit 43/43 (12 new UnitOfMeasure). No deviations — the 02-02 owned-type-safe snake-caser meant no NumeraDbContext edit. Commits 86f7c09, 9c8139a, a51225c.

Progress: [███░░░░] 32% (Phase 2 of 9 — 3/7 plans)

## Performance Metrics

**Velocity:**
- Total plans completed: 8 (Phase 1: 01-01 … 01-08)
- Average duration: ~31 min (over 7 recorded plans; 01-03 not recorded)
- Total execution time: ~3.6 hours (recorded plans)

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-plattform-kern | 8 | ~218 min (7 recorded) | ~31 min |
| 02-stammdaten | 3 of 7 | ~73 min | ~24 min |

**Per-Plan (Phase 02):**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| 02-01 | 3 min | 2 | 2 |
| 02-02 | 50 min | 3 | 18 |
| 02-03 | 20 min | 3 | 14 |

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
| Phase 02 P02 | 50min | 3 tasks | 18 files |

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
- [Phase 02-stammdaten]: 02-01: EF Core 10 NAMED query filters — tenant filter renamed to "Tenant", new "NotArchived" filter for IArchivable; two independent filters, never &&-combined (a second unnamed HasQueryFilter would silently overwrite the first — data-leak-class bug). IgnoreQueryFilters() no-arg drops all → RLS sole control (integration tests unaffected).
- [Phase 02-stammdaten]: 02-01: archival is app-level only — RLS deliberately does NOT filter archived rows (an archived row is still the tenant's own data, RESEARCH.md Pattern 1); IArchivable.ArchivedAt (DateTimeOffset?) drives the NotArchived filter.
- [Phase 02-stammdaten]: 02-02: ONE BusinessPartner entity with IsCustomer/IsSupplier flags (not separate Customer/Supplier) — a dual-role partner is one master record; addresses are EF owned value types embedded in the partners row (no separate table/RLS/join), shipping optional/nullable.
- [Phase 02-stammdaten]: 02-02: CRM RLS policies are hand-written per table via migrationBuilder.Sql (reflective ITenantEntity discovery never emits policies — the #1 silent-leak trap); customer/supplier numbers are unique per tenant only among non-archived rows (partial unique index) so archived numbers are reusable.
- [Phase 02-stammdaten]: 02-02: USt-IdNr validation is offline-only (DE format + ISO 7064 MOD 11,10 checksum, EU shape check) and never gates a save on VIES; the reflective snake-caser was made owned-type-safe (skip owned PK columns, navigation-prefix owned value columns).
- [Phase 02-stammdaten]: 02-03: ONE CatalogItem carries both Product and Service kinds (CatalogItemKind flag drives the default unit C62 vs HUR); store only the UN/ECE Rec 20 code (BT-130) — the curated allowed set + German labels live in code/frontend, not a DB code table.
- [Phase 02-stammdaten]: 02-03: catalog prices are plain decimal [Precision(19,4)] columns (not an owned Money value object) — Money-precision numeric(19,4) with no owned-type mapping; article number unique per tenant only among non-archived rows (partial unique index), reusable across tenants and after archival; catalog_items RLS policy hand-written via migrationBuilder.Sql. The second Phase-2 table deliberately lands in its own wave so the two ef-migrations-add runs chain cleanly through the snapshot (no ordering race).

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

Last session: 2026-07-11
Stopped at: Completed 02-03-PLAN.md (Phase 2 wave 3) — Numera.Modules.Catalog (CatalogItem + UnitOfMeasure UN/ECE Rec 20 helper) and _Catalog migration (chained after _Crm) with per-table RLS + partial-unique article number. Build 0/0, integration 25/25, unit 43/43.
Open caveats: no git remote yet, so the GitHub Actions workflow has never run live — validated locally only. Docker stack (postgres + keycloak) left running. Keycloak BFF client secret lives in dotnet user-secrets, not in the repo. Git Bash still resolves SDK 8.0.303 unless the user-local dotnet dir (/c/Users/Admin/AppData/Local/Microsoft/dotnet) is prepended to PATH with DOTNET_ROOT + DOTNET_MULTILEVEL_LOOKUP=0 — note $LOCALAPPDATA is empty under Git Bash, use the absolute path. gsd-tools `state` subcommands cannot parse this narrative STATE.md format — STATE.md is maintained by hand.
Resume file: None
Next: Execute 02-04-PLAN.md (Phase 2 wave 4).
