---
phase: 01-plattform-kern
verified: 2026-07-10T13:37:29Z
remediated: 2026-07-10T14:05:00Z
status: passed
score: 7/7 must-haves verified
resolved_gaps:
  - truth: "CI builds the solution, runs unit + Testcontainers integration suites, and builds the web app; the RLS suite is a hard gate (01-08)"
    status: resolved
    resolution: >
      Numera.Platform.Money and Numera.Platform.Tests were added as Project
      entries to Numera.sln, and the stale net8.0 TargetFramework override was
      removed from Numera.Platform.Money.csproj so it inherits net10.0 from
      Directory.Build.props (the override dated from when only the .NET 8 SDK
      was installed; SDK 10.0.301 is now provisioned).
    verification: >
      The exact CI sequence was replayed locally: dotnet restore Numera.sln;
      dotnet build Numera.sln -c Release --no-restore (0 warnings / 0 errors,
      now including Money + Platform.Tests); dotnet test tests/Numera.Platform.Tests
      -c Release --no-build (16/16 pass from bin/Release/net10.0 -- the step that
      previously aborted the job); dotnet test tests/Numera.IntegrationTests
      -c Release --no-build (8/8 pass -- the RLS + audit + pool-leak hard gate
      that a real CI run never used to reach).
    commit: 3912848
---

# Phase 1: Plattform-Kern Verification Report

**Phase Goal:** Ein Multi-Tenant-Fundament, auf dem jede finanzrelevante Funktion sicher und rechtskonform aufsetzen kann - alle nicht-nachruestbaren Weichen (Mandanten-Isolation, exakte Geldarithmetik, Audit) sind gestellt.
**Verified:** 2026-07-10T13:37:29Z
**Status:** passed (after remediation of 1 gap -- see commit 3912848)
**Re-verification:** Gap remediated by orchestrator on 2026-07-10; full CI sequence replayed green (build 0/0, unit 16/16, integration 8/8).

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer kann sich registrieren, eine Firma anlegen, sich anmelden und bleibt ueber Browser-Sitzungen angemeldet | VERIFIED | RegistrationService.cs creates a Keycloak user + Organization and mirrors tenants/membership rows in one transaction with an audit event; KeycloakBffExtensions.cs wires code-flow+PKCE OIDC with SaveTokens server-side and an HttpOnly, SameSite=Lax, 14-day sliding cookie (numera.session) -- the SPA never receives a token (web/src/lib/api.ts uses credentials:'include' only). Fix commits 101d7b6/e4807f6 document live verification (register -> OIDC login -> /api/me resolves tenant). Human-verify checkpoint for this criterion was approved per 01-08-SUMMARY.md. |
| 2 | Daten sind strikt pro Mandant isoliert -- automatisierter Cross-Tenant-Test | VERIFIED | Ran tests/Numera.IntegrationTests directly against a real postgres:18 Testcontainer as the non-BYPASSRLS numera_app role. RlsIsolationTests (IgnoreQueryFilters() disables the app-level filter so RLS is the sole control under test): Tenant_A_sees_only_its_own_rows_and_zero_Tenant_B_rows, Insert_with_foreign_tenant_id_is_rejected_by_the_WITH_CHECK_policy, Reading_without_a_tenant_context_never_leaks_rows -- all 3 pass. PoolLeakTests (2/2 pass) prove SET/RESET on app.current_tenant prevents cross-tenant leakage over a pooled connection. RLS is ENABLEd + FORCEd on every tenant table (migrations 20260710021521_InitialPlatform.cs, 20260710023110_AuditEvents.cs) and numera_app is created NOBYPASSRLS (scripts/db-roles.sql, PostgresFixture.cs). |
| 3 | Jede finanzrelevante Aenderung landet in einem unveraenderbaren Audit-Log, das nicht editiert werden kann | VERIFIED | Migration 20260710023110_AuditEvents.cs REVOKEs UPDATE/DELETE from numera_app AND creates a BEFORE UPDATE OR DELETE trigger (audit_no_mutate()) that unconditionally RAISE EXCEPTIONs -- a belt-and-braces guarantee independent of grants. AuditImmutabilityTests (run against real Postgres): Insert_and_select_of_an_audit_event_succeed, Update_of_an_audit_event_is_rejected_by_the_database, Delete_of_an_audit_event_is_rejected_by_the_database -- all 3 pass. AuditWriter.RecordAsync adds the AuditEvent to the caller's own scoped NumeraDbContext without a new transaction/scope/SaveChanges, so it commits atomically with the change it records. |
| 4 | Geldbetraege werden systemweit exakt (decimal/numeric, nie float) gerechnet, belegt durch Golden-File-Tests | VERIFIED (code+tests) -- CI gate broken, see gap | Money.cs is a readonly record struct backed by decimal Amount (no float/double anywhere). RoundingPolicy.RoundTax uses Math.Round(..., 2, MidpointRounding.AwayFromZero) (never ToEven); DocumentVatTotal sums per-category rounded amounts (never rounds the grand total), matching EN 16931 BR-CO-14/BR-CO-17. RoundingPolicyTests.cs (146 lines, 7 tests) covers mixed 19%/7%, an allowance/discount case, and a reverse-charge EUR0 line; ran directly with dotnet test (project built standalone) -- 7/7 pass, plus 9/9 entitlement tests (16/16 total). However, this test project is not part of Numera.sln and its CI step fails (see Gaps). |
| 5 | Nutzer kann die UI zwischen Deutsch und Englisch umschalten; die App ist als PWA installierbar | VERIFIED | web/src/i18n/index.ts + de/en locale namespaces (auth.json, common.json); LanguageSwitcher.tsx calls i18n.changeLanguage and persists the choice to localStorage + a 1-year cookie. vite.config.ts configures vite-plugin-pwa (registerType: 'autoUpdate', app-shell globPatterns, navigateFallbackDenylist: [/^\/api/], explicit NetworkOnly runtime rules for /api GET+POST). public/manifest.webmanifest has display:"standalone", 192/512 + maskable icons. npm run build succeeds and emits dist/sw.js + dist/workbox-*.js (10 precached entries); npm test (vitest, 4 i18n tests) passes. Human-verify checkpoint for this criterion was approved per 01-08-SUMMARY.md. |
| 6 | Tarif-Feature-Gates (S/M/L/XL) blenden Funktionen pro Mandant sichtbar ein/aus (ohne Zahlungsabwicklung) | VERIFIED | PlanCapabilityMap.cs is the single source of truth (strictly increasing S/M/L/XL capability sets, FrozenDictionary); EntitlementService resolves the current tenant's Plan column to capabilities; PlanFeatureFilter implements Microsoft.FeatureManagement's filter contract so /api/features/einvoicing-check in MeEndpoints.cs returns 200 only for plans granting EInvoicing (L/XL), else 403 -- proving the server gate is authoritative, not cosmetic. EntitlementServiceTests.cs (9 tests, all pass) covers gate denial and per-tier resolution. No Stripe/payment code found anywhere in the codebase (grep clean). |

**Score:** 6/7 must-haves verified (criterion 4's code and tests are fully correct; the CI hard gate that is supposed to enforce it automatically is broken -- tracked as a gap)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| Numera.sln | Solution wiring all projects | PARTIAL | Builds cleanly (dotnet build -c Release: 0 warnings/0 errors) but is missing Numera.Platform.Money and Numera.Platform.Tests as Project entries |
| src/platform/Numera.Platform.Tenancy/TenantConnectionInterceptor.cs | Parameterized tenant GUC interceptor + RESET | VERIFIED | Parameterized set_config, RESET app.current_tenant on close, proven live by PoolLeakTests |
| src/platform/Numera.Platform.Db/NumeraDbContext.cs | DbContext base with tenant filter + interceptor | VERIFIED | Registers TenantConnectionInterceptor, applies ITenantEntity query filters, tenant-leading indexes |
| src/modules/Numera.Modules.Ledger/Posting.cs | Inert double-entry posting entity | VERIFIED | decimal Amount mapped numeric(19,4), debit/credit direction, explicitly documented as inert (no balancing logic) |
| docker-compose.yml | postgres:18 + keycloak:26 --features=organization | VERIFIED | postgres:18, quay.io/keycloak/keycloak:26.2 with start-dev --features=organization --import-realm; both containers running and healthy |
| src/platform/Numera.Platform.Db/Entities/Tenant.cs, Membership.cs | tenants/membership tables | VERIFIED | Plan column, FK cascade, unique (tenant_id, user_id) index |
| scripts/db-roles.sql | numera_app (no BYPASSRLS) + migrator roles | VERIFIED | Both roles NOSUPERUSER NOBYPASSRLS; confirmed live via integration tests running as numera_app |
| src/platform/Numera.Platform.Db/Sql/rls_policies.sql + migrations | RLS ENABLE+FORCE+policy on every tenant table | VERIFIED | Present in 20260710021521_InitialPlatform.cs and 20260710023110_AuditEvents.cs; proven functional by RLS integration tests |
| src/platform/Numera.Platform.Money/Money.cs, RoundingPolicy.cs, TaxCategory.cs | decimal Money + AwayFromZero rounding + EN16931 categories | VERIFIED (code); excluded from sln | Math.Round(..., MidpointRounding.AwayFromZero), per-category-then-sum total; builds/tests pass standalone but not via Numera.sln |
| tests/Numera.Platform.Tests/Money/RoundingPolicyTests.cs | Golden-file rounding suite | VERIFIED (code); not part of sln/CI | 146 lines, 7 tests, all pass when built directly |
| src/platform/Numera.Platform.Tenancy/ICurrentUser.cs | Actor seam | VERIFIED | Guid? UserId, consumed by AuditWriter |
| src/platform/Numera.Platform.Audit/AuditEvent.cs, AuditWriter.cs | Append-only entity + sync in-transaction writer | VERIFIED | PrevHash/RowHash reserved nullable columns; writer adds to caller's context, no new transaction/SaveChanges |
| Migration with REVOKE + trigger + RLS on audit_events | Immutability enforcement | VERIFIED | REVOKE UPDATE, DELETE + BEFORE UPDATE OR DELETE trigger RAISE EXCEPTION; both privilege and unconditional-trigger layers present |
| src/platform/Numera.Platform.Entitlements/PlanCapabilityMap.cs, EntitlementService.cs, PlanFeatureFilter.cs | Plan->capability matrix + resolution + FeatureManagement filter | VERIFIED | Strictly increasing tiers, IContextualFeatureFilter-style filter, 9/9 tests pass |
| src/Numera.Api/Auth/KeycloakBffExtensions.cs | Cookie+OIDC BFF wiring | VERIFIED | HttpOnly cookie, code flow + PKCE, SaveTokens, 401/403 JSON responses for API requests instead of redirects |
| src/Numera.Api/Auth/TenantResolutionMiddleware.cs | org claim -> tenant_id -> ICurrentTenant | VERIFIED | Robust claim-shape parsing (string/array/object), sets ICurrentTenant post-auth |
| src/Numera.Api/Services/RegistrationService.cs | Keycloak org+user -> tenant+membership mirror | VERIFIED | Full flow incl. audit event in the same transaction, welcome-email enqueued only after commit |
| keycloak/realm-numera.json | Realm import: organization feature, confidential client, full scopes | VERIFIED | organizationsEnabled: true, numera-bff confidential client, full default scope set + organization scope (fixed by commit e4807f6) |
| src/Numera.Api/Endpoints/MeEndpoints.cs | GET /api/me + /api/me/entitlements | VERIFIED | Both endpoints present, plus a server-gated example endpoint proving entitlement enforcement |
| web/vite.config.ts | vite-plugin-pwa config | VERIFIED | App shell precache, /api excluded via denylist + NetworkOnly rules |
| web/public/manifest.webmanifest | PWA manifest | VERIFIED | standalone, 192/512 + maskable icons |
| web/src/i18n/index.ts, LanguageSwitcher.tsx | DE/EN i18n + toggle | VERIFIED | react-i18next, persisted via localStorage + cookie |
| web/src/lib/api.ts | credentials:'include' fetch wrapper, no token storage | VERIFIED | No localStorage/sessionStorage token handling; same-origin /api base |
| tests/Numera.IntegrationTests/RlsIsolationTests.cs, AuditImmutabilityTests.cs, PoolLeakTests.cs | Testcontainers suites | VERIFIED | All 8 tests pass against real postgres:18, running as non-BYPASSRLS numera_app |
| .github/workflows/ci.yml | build + unit + integration + web pipeline, RLS suite as hard gate | BROKEN | Builds Numera.sln then runs dotnet test tests/Numera.Platform.Tests --no-build, which fails because that project isn't in the sln (never built). Reproduced locally. The job fails before the integration-test (RLS/audit) step ever runs. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| NumeraDbContext | TenantConnectionInterceptor | AddInterceptors in OnConfiguring | WIRED | Confirmed in source + exercised by integration tests |
| TenantConnectionInterceptor | Postgres app.current_tenant GUC | parameterized set_config | WIRED | Parameterized (no string interpolation); RESET on close |
| EF migration | RLS policies | migrationBuilder.Sql ENABLE/FORCE/CREATE POLICY | WIRED | Present in both migrations, functional per integration tests |
| tenant tables | app.current_tenant GUC | USING/WITH CHECK current_setting(...) | WIRED | Verified functionally (cross-tenant INSERT rejected) |
| RoundingPolicy.RoundTax | MidpointRounding.AwayFromZero | Math.Round(..., AwayFromZero) | WIRED | Confirmed by source + passing golden-file tests |
| document VAT total | per-category rounded sum | buckets.Sum(...) | WIRED | Confirmed by source + MixedRates test |
| audit migration | immutability enforcement | REVOKE + BEFORE trigger raising exception | WIRED | Both layers present and independently proven by tests |
| AuditWriter | audit_events table | DbSet.Add in caller's context | WIRED | No new transaction/SaveChanges -- atomic with caller's unit of work |
| EntitlementService | tenant Plan column | PlanCapabilityMap.For(tenant.Plan) | WIRED | Verified by tests incl. unknown-tenant/plan deny-by-default |
| PlanFeatureFilter | Microsoft.FeatureManagement | custom filter reading entitlements | WIRED | /api/features/einvoicing-check example endpoint exercises the full chain |
| Program.cs | KeycloakBffExtensions | AddAuthentication cookie + OIDC | WIRED | Confirmed in Program.cs and functional per live-verified fix commits |
| TenantResolutionMiddleware | ICurrentTenant | SetTenant from organization claim | WIRED | Confirmed; /api/me resolves tenant per fix commit e4807f6 |
| vite.config.ts (vite-plugin-pwa) | /api exclusion | navigateFallbackDenylist + NetworkOnly | WIRED | Confirmed in config, build succeeds |
| LanguageSwitcher | i18next | i18n.changeLanguage | WIRED | Confirmed in source |
| api.ts | BFF | credentials: 'include' | WIRED | Confirmed in source |
| ci.yml | integration tests | dotnet test running Testcontainers suite | NOT REACHED | The job fails at the preceding Unit tests step (see Gaps); the integration-test step is never executed in an actual CI run even though the suite itself passes when run directly |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|----------------|
| PLAT-01 (register/login/session) | SATISFIED | None |
| PLAT-02 (RLS + cross-tenant tests) | SATISFIED | None (tests pass on real Postgres) |
| PLAT-04 (tier feature gates) | SATISFIED | None |
| PLAT-05 (immutable audit log) | SATISFIED | None |
| PLAT-06 (DE/EN i18n) | SATISFIED | None |
| PLAT-07 (installable PWA) | SATISFIED | None |
| PLAT-09 (exact decimal money, EN16931 rounding) | AT RISK | Code and tests are correct, but the CI pipeline that should continuously enforce this (and the whole build) is broken -- see gap |

### Anti-Patterns Found

None. Scanned all .cs/.ts/.tsx files under src/, tests/, web/src/ for TODO|FIXME|XXX|HACK|PLACEHOLDER|not implemented|coming soon -- no matches. No stub return patterns (return null/empty bodies) found in the reviewed files.

### Human Verification Required

None outstanding -- the two criteria that need human judgment (1: register/login/session persistence; 5: i18n toggle + installable PWA) were already exercised and approved as the Task 3 human-verify checkpoint documented in 01-08-SUMMARY.md.

### Gaps Summary

Every individual capability required by Phase 1 exists in the codebase, is substantively implemented (not a stub), and is functionally correct -- verified by directly running the Money/Entitlements unit suite (16/16 pass) and the Testcontainers integration suite against a real postgres:18 as the non-BYPASSRLS numera_app role (8/8 pass, including the cross-tenant RLS and audit-immutability tests that are the crux of success criteria 2 and 3).

The one concrete gap is a wiring defect, not a logic defect: Numera.Platform.Money and Numera.Platform.Tests were never added as Project entries to Numera.sln. This is a leftover from 01-03, when only the .NET 8 SDK was available and the plan explicitly flagged retargeting/sln-inclusion as future work once .NET 10 was provisioned. The .NET 10 SDK is now installed, but the follow-up was never done, and 01-08's CI workflow was authored against the assumption that dotnet build Numera.sln would produce the Numera.Platform.Tests binary. Reproducing the exact CI command sequence locally confirms the Unit tests step fails with a test-source-file-not-found error, and -- because GitHub Actions steps run sequentially and this step has no continue-on-error -- the Integration tests (RLS + audit + pool-leak) step, which is the actual hard gate for success criterion 2, never runs in a real CI invocation.

Fix is small and low-risk: add the two missing Project entries (with build configurations) to Numera.sln, and drop Numera.Platform.Money.csproj's local net8.0 TargetFramework override so it inherits net10.0 from Directory.Build.props (matching every other project, including Numera.Platform.Tests, which already targets net10.0).

---

_Verified: 2026-07-10T13:37:29Z_
_Verifier: Claude (gsd-verifier)_
