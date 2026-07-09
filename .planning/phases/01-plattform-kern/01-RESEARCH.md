# Phase 1: Plattform-Kern - Research

**Researched:** 2026-07-09
**Domain:** Multi-tenant SaaS platform foundation (.NET 10 modular monolith + PostgreSQL 18 RLS + React 19 PWA) for German accounting/e-invoicing
**Confidence:** HIGH on stack/versions, RLS, audit, money, i18n, PWA; MEDIUM-HIGH on Keycloak-vs-Identity recommendation and entitlement library choice (both involve a judgment call documented below)

## Summary

This phase builds the non-retrofittable foundation: tenant isolation (Postgres RLS + EF global query filters), exact money arithmetic (decimal + EN 16931 rounding), an immutable audit log, auth/registration, i18n (DE/EN), a PWA shell, and tier feature-gates. Everything downstream depends on these seams being correct, so the phase is about *getting the plumbing right*, not shipping user-visible features. The stack is fully LOCKED by project research (STACK.md / ARCHITECTURE.md) — this research fills in the concrete "how" for each locked choice with current (Nov 2025 GA) versions.

All core versions are confirmed GA and LTS-aligned as of research date: **.NET 10 (GA 2025-11-11, LTS to 2028-11-10), C# 14, EF Core 10 (LTS), Npgsql.EntityFrameworkCore.PostgreSQL 10.0, PostgreSQL 18, React 19, Vite 7, Keycloak 26.x**. Notable: Npgsql EF Core 10 translates `Guid.CreateVersion7()` to Postgres 18's native `uuidv7()` — use UUIDv7 for tenant/entity PKs (time-ordered, index-friendly, non-guessable).

**Primary recommendation:** Build the `platform/` shared kernel first as prescribed in ARCHITECTURE.md build-order step 1. Enforce tenancy with **RLS as the primary control** (DB-level) + **EF Core global query filters as defence-in-depth** (app-level), wire the tenant GUC via a **`DbConnectionInterceptor` using `SET LOCAL` inside a parameterized command with `RESET` on connection-return-to-pool**, and prove isolation with a **Testcontainers cross-tenant suite** that is a hard CI gate. Use **Keycloak 26 with a single realm + Organizations** for auth (recommendation below), the **BFF pattern** for the React SPA, a **DB-enforced append-only audit table** (REVOKE + trigger; hash-chain deferred), and a **`Money` value object over `decimal` with explicit per-VAT-category EN 16931 rounding** validated by golden-file tests.

---

## Standard Stack

### Core (all LOCKED by project research — versions verified current)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET / ASP.NET Core | 10.0 (LTS, GA 2025-11-11) | Backend runtime + Web API | LTS to 2028-11-10; e-invoicing ecosystem; `decimal` primitive |
| C# | 14 | Language | Ships with .NET 10; records/pattern-matching suit money+domain |
| EF Core | 10.0 (LTS) | ORM + migrations | Global query filters, interceptors, migrations. Requires .NET 10 SDK |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0 | Postgres provider | `SET LOCAL` support; `Guid.CreateVersion7()`→`uuidv7()` on PG18; cube/JSON |
| PostgreSQL | 18 (18.3+) | Database | Native RLS (isolation linchpin); `numeric` for money; `uuidv7()` |
| React | 19 | Frontend | Ecosystem depth; PWA-first |
| Vite | 7 | Build tool | Fast; app-shell PWA path |
| Keycloak | 26.x | IAM | Single realm + Organizations = native multi-tenancy; OIDC + ASP.NET Core; self-hosted DSGVO |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| NodaMoney | 2.7.0 | Money value object (currency + rounding) | Public domain money type; keep raw `decimal` for intermediate calc |
| FluentValidation | 12.x | Request/domain validation | Registration, company, all input validation |
| Microsoft.FeatureManagement.AspNetCore | latest | Feature-gate framework | Tier gates via a **custom per-tenant plan feature filter** (see Entitlements) |
| Hangfire | latest (Postgres storage) | Background jobs | Ships in Phase 1 as worker tier skeleton (email dispatch); persists to Postgres |
| Serilog | latest | Structured logging | Tenant-scoped structured logs; distinct from audit log |
| Testcontainers (for .NET) | latest | Integration test infra | Real Postgres for RLS/cross-tenant isolation tests — **CI gate** |
| xUnit | latest | Test framework | Backend unit + integration |
| react-i18next / i18next | 15.x / 25.x | Frontend i18n | DE+EN, namespaces, lazy-loaded bundles |
| TanStack Query | 5.x | Server-state | Data fetching, cache; pairs with PWA |
| TanStack Table | 8.x | Headless tables | Later data grids; assemble now with shadcn primitives |
| vite-plugin-pwa | 1.x (0.21+) | Service worker, manifest, install | Workbox-powered; app-shell caching |
| shadcn/ui (Radix + Tailwind v4) | current | UI components | Code-owned components |
| react-hook-form + zod | 7.x / 3.x | Forms + schema validation | Registration/login/settings forms |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Keycloak | ASP.NET Core Identity (in-app) | Simpler infra for v1, but you rebuild SSO/MFA/org-tenancy/invitations; Steuerberater cross-tenant (Phase 8) fits Keycloak's model far better. **Recommend Keycloak** — see decision below |
| Microsoft.FeatureManagement | Hand-rolled entitlement service (plan→capability map) | ARCHITECTURE.md describes a pure entitlements service. FeatureManagement gives you `[FeatureGate]`/`IVariantFeatureManager` plumbing but you still need a custom filter that reads the tenant's plan. Either is fine; recommend FeatureManagement for the middleware/attribute ergonomics, backed by a DB plan→capability map |
| Hash-chained audit rows now | Plain append-only audit now | Hash-chaining is defence against DB-admin tampering; adds write-ordering complexity. **Defer to a later GoBD-archive phase**; ship REVOKE+trigger append-only now |

**Installation (backend):**
```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.*
dotnet add package NodaMoney --version 2.7.0
dotnet add package FluentValidation.AspNetCore
dotnet add package Microsoft.FeatureManagement.AspNetCore
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.PostgreSql
dotnet add package Serilog.AspNetCore
dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect
# tests:
dotnet add package Testcontainers.PostgreSql
dotnet add package xunit
```
```bash
# frontend
npm create vite@latest web -- --template react-ts
npm i i18next react-i18next @tanstack/react-query @tanstack/react-table react-hook-form zod
npm i -D vite-plugin-pwa
# shadcn/ui + tailwind v4 per their setup
```

---

## Architecture Patterns

### Recommended Project Structure (prescriptive)

Modular monolith, vertical-slice modules, shared platform kernel. This mirrors ARCHITECTURE.md's layout mapped onto a .NET solution. **In .NET, each "module" is a class-library project; each module exposes only a `PublicApi` project — modules never reference another module's internals, only its `PublicApi` or domain events.**

```
Numera.sln
src/
├── Numera.Api/                       # ASP.NET Core host (Web API + BFF for React); Program.cs, DI wiring, controllers/minimal endpoints
├── Numera.Worker/                    # Hangfire worker host (background jobs: email now; PDF/e-invoice later)
├── platform/                         # shared kernel — everything depends on this
│   ├── Numera.Platform.Tenancy/      # TenantContext, ICurrentTenant, RLS SET LOCAL interceptor, tenant-resolution middleware
│   ├── Numera.Platform.Auth/         # OIDC/Keycloak integration, membership (user↔tenant), roles seam (Phase 8)
│   ├── Numera.Platform.Entitlements/ # plan→capability map, feature filter, IEntitlementService guard
│   ├── Numera.Platform.Audit/        # append-only audit writer + IAuditEvent
│   ├── Numera.Platform.Money/        # Money value object, EN 16931 rounding, tax-category enum
│   └── Numera.Platform.Db/           # DbContext base, migrations, RLS policy migrations, connection config
├── modules/
│   ├── Numera.Modules.Ledger/        # INERT in Phase 1: accounts/journal_entries/postings schema + PostingSource interface only
│   └── (crm, catalog, sales … later phases)
├── i18n/                             # server-side resource bundles (.resx) for validation msgs + email templates (DE/EN)
└── web/                              # React 19 + Vite 7 SPA (PWA); src/, i18n/ (react-i18next), public/manifest
tests/
├── Numera.Platform.Tests/            # unit tests (Money rounding golden files, entitlement resolution)
└── Numera.IntegrationTests/          # Testcontainers: RLS cross-tenant suite (CI GATE), audit immutability, migrations
docker-compose.yml                    # postgres:18, keycloak:26 (+ later KoSIT sidecar)
.github/workflows/ci.yml              # build + unit + Testcontainers integration
```

**Rationale (from ARCHITECTURE.md, verified consistent with 2026 .NET modular-monolith consensus):** the kernel centralizes tenancy/auth/entitlements/audit/money so no module re-implements (and mis-implements) isolation or rounding. `PublicApi`-only cross-module references keep boundaries enforceable. The inert ledger schema ships now so invoicing can post later without a rewrite.

### Pattern 1: RLS tenant isolation via connection interceptor (primary control)

**What:** Every tenant table has `tenant_id uuid NOT NULL`. An RLS policy filters by `current_setting('app.current_tenant')`. A `DbConnectionInterceptor` sets it per connection; EF global query filters mirror it in-app.

**When:** Every tenant-scoped table, always.

**Correct implementation (fixes the SQL-injection + pooling flaws in common blog samples):**
```csharp
// Source: bytefish.de pattern, CORRECTED for parameterization + pool safety.
// The widely-copied blog sample uses string interpolation ($"SET app.current_tenant='{name}'")
// which is SQL-injectable and uses session-scoped SET. Do NOT copy that verbatim.
public sealed class TenantConnectionInterceptor(ICurrentTenant tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } id) return;
        await using var cmd = connection.CreateCommand();
        // set_config(name, value, is_local=false at session scope OR use SET LOCAL inside the request txn)
        cmd.CommandText = "SELECT set_config('app.current_tenant', @tenant, false)";
        var p = cmd.CreateParameter(); p.ParameterName = "tenant"; p.Value = id.ToString();
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    // MUST also reset on return-to-pool to avoid stale-context cross-tenant leak:
    public override void ConnectionClosing(DbConnection connection, ConnectionEventData eventData)
        => connection.CreateCommand().Also(c => { c.CommandText = "RESET app.current_tenant"; c.ExecuteNonQuery(); });
}
```
**Migration creating the policy (raw SQL in an EF migration — EF has no fluent API for RLS):**
```csharp
migrationBuilder.Sql("""
    ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
    ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;   -- also apply to table owner
    CREATE POLICY tenant_isolation ON audit_events
      USING (tenant_id = current_setting('app.current_tenant')::uuid)
      WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
""");
```
**EF global query filter (defence-in-depth):**
```csharp
modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => e.TenantId == _currentTenant.TenantId);
```

**Non-negotiable RLS rules (from STACK.md, verified against 2025 RLS best-practice sources):**
1. `SET LOCAL` (or `set_config(..., is_local=true)`) inside the request transaction — **never plain `SET`** that survives pool reuse. If using session-scope `set_config(false)`, you MUST `RESET` on connection close.
2. Make `tenant_id` the **leading column** of every access index (missing composite index ≈ 100× slower RLS).
3. **Do NOT grant `BYPASSRLS`** to the app role. Reserve it for the migration role only. Use `FORCE ROW LEVEL SECURITY` so even the table owner is filtered.
4. Views: `security_invoker = true`.
5. Test with Testcontainers — a leak here is a DSGVO breach.

### Pattern 2: Immutable append-only audit log (DB-enforced)

**What:** A dedicated `audit_events` table (tenant_id, actor_user_id, action, entity_type, entity_id, payload jsonb, occurred_at). App DB role gets **INSERT + SELECT only**; UPDATE/DELETE **REVOKE**d; a `BEFORE UPDATE OR DELETE` trigger raises an exception as belt-and-braces.

**When:** Every finance-relevant state change, written **synchronously in the same transaction** as the change it records.
```sql
REVOKE UPDATE, DELETE ON audit_events FROM numera_app;
GRANT INSERT, SELECT ON audit_events TO numera_app;

CREATE FUNCTION audit_no_mutate() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'audit_events is append-only'; END; $$;
CREATE TRIGGER audit_immutable BEFORE UPDATE OR DELETE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION audit_no_mutate();
```
**Phase 1 scope:** what to log = actor, action, entity, before/after (jsonb), timestamp, tenant. **Hash-chaining: NO for Phase 1** — plain REVOKE+trigger append-only is sufficient for the success criterion ("cannot be edited"). Hash-chain / external anchoring (S3 Object Lock) is a defence against a rogue DB admin and belongs in the later GoBD-archive phase. (Design the table with a nullable `prev_hash`/`row_hash` column reserved so adding the chain later is non-breaking.)

### Pattern 3: Money value object + EN 16931 rounding (build now, without invoices)

**What:** A `Money` type over `System.Decimal` (never float/double). Storage `numeric(19,4)` for amounts, `numeric(19,6)` acceptable for unit-price/intermediate. Rounding is **half-away-from-zero (kaufmännisch)** to 2 decimals **per VAT category**, then the document VAT total = **sum of the rounded per-category amounts** (not a re-rounded grand total).

**Why the rounding mode matters:** EN 16931 BR-S-08/BR-CO-17 require `BT-117 = round(BT-116 × BT-119/100, 2)`. German commercial convention and most EN 16931 reference implementations use **round-half-up (AwayFromZero)**, i.e. `Math.Round(x, 2, MidpointRounding.AwayFromZero)` — **not** banker's rounding (`ToEven`, the C# default), which would diverge on midpoints and can produce KoSIT validation failures. Confirm the exact half-rounding direction against a KoSIT-validated reference invoice when e-invoicing lands, but AwayFromZero is the correct default. (LOW-MEDIUM confidence on ToEven-vs-AwayFromZero being *always* AwayFromZero across every field — flag for the e-invoicing phase to lock with golden files; MEDIUM-HIGH that per-category-then-sum is mandatory.)

**Phase 1 deliverable (no invoices yet):** the `Money` type, a `TaxCategory` enum (S/AE/K/E/Z/G/O per EN 16931), a `RoundingPolicy` with the per-category rounding function, and **golden-file tests**: hand-authored input→expected-output fixtures for mixed 19%+7%, a discount/allowance case, and a reverse-charge €0 line, asserting per-category-round-then-sum. These fixtures become the regression net the invoicing engine plugs into later.
```csharp
public static decimal RoundTax(decimal taxableBase, decimal ratePercent)
    => Math.Round(taxableBase * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
// document VAT = categories.Sum(c => RoundTax(c.Base, c.Rate))  — never Round(total, 2)
```

### Pattern 4: Entitlement gate (server-side tier resolution)

**What:** A single `IEntitlementService` maps a tenant's plan (S/M/L/XL) → capability set, evaluated server-side. Wire via `Microsoft.FeatureManagement` with a **custom `IFeatureFilter`/contextual filter** that reads the current tenant's plan from the DB, so `[FeatureGate("BankSync")]` on an endpoint checks `auth ∧ tenant ∧ entitlement`. The frontend calls a `/api/me/entitlements` endpoint to show/hide features — **frontend visibility is cosmetic only; the server guard is authoritative** (client gating is trivially bypassed).

**Phase 1 scope:** the plan→capability matrix (config or DB table `tenant_plans`), the guard middleware/attribute, and the `/me/entitlements` endpoint. **No payment/Stripe** (explicitly out of scope). Model as entitlements so a later billing webhook flips the plan in one place.

### Pattern 5: Auth — Keycloak single realm + Organizations + BFF (recommendation)

See "Keycloak vs ASP.NET Core Identity" decision below. Pattern:
- **One Keycloak realm**, one **Organization per tenant (Mandant)**. Registration creates a user + an Organization; the `organization` OIDC scope carries the org id into the token → maps to `tenant_id`.
- **BFF pattern**: React SPA holds **no tokens**; the ASP.NET Core host is a confidential OIDC client, holds the tokens server-side, issues an **HttpOnly SameSite cookie** to the SPA, and proxies API calls. This satisfies "bleibt über Browser-Sitzungen angemeldet" (persistent session) with the strongest token security and is the current OAuth BCP for browser apps. Avoid storing access/refresh tokens in the SPA (localStorage/JS-readable).
- **Invitations** (Phase 8 seam): Keycloak Organizations has a built-in invitation flow + "Invitation" required action — enable the required action so the seam is ready without extra work now.

### Pattern 6: i18n (DE + EN)

- **Frontend (react-i18next):** namespaces (`common`, `auth`, `validation`, `settings`), lazy-loaded per-namespace JSON bundles, language switch persisted per user (stored server-side on the user profile + `lng` cookie so SSR/BFF and the SPA agree). Detect via `i18next-browser-languagedetector` but the switch writes the user preference back.
- **Backend (ASP.NET Core localization):** `AddLocalization` + `.resx` resource files, `UseRequestLocalization` with `SupportedCultures = [de, en]`. `AddDataAnnotationsLocalization` (or FluentValidation localization) for **validation messages**; inject `IStringLocalizer<T>` into the **email/templating service** for DE/EN emails. Culture provider order: user-preference cookie → `Accept-Language` → default `de`.

### Pattern 7: PWA shell (vite-plugin-pwa)

- `registerType: 'autoUpdate'`, Workbox `generateSW` for the app shell.
- **Manifest:** name, short_name, theme/background color, `display: standalone`, icons (192/512 + maskable), start_url `/`.
- **Cache the app shell** (JS/CSS/HTML/fonts precached by Workbox). **Do NOT cache financial API data.** Set `navigateFallbackDenylist` for `/api` and give any `/api/*` runtime rule a **`NetworkOnly`** strategy — financial reads/writes must never be served stale from cache. Offline = shell loads + a "you are offline" state; **no offline financial writes** (numbering/immutability need the server as source of truth — see PITFALLS PWA traps).
- Test install + offline-shell with Playwright; test on **real iOS** (iOS PWA storage can be evicted; don't rely on persistent client storage for anything financial).

### Anti-Patterns to Avoid (from PITFALLS.md — these are liability, not bugs)

- **App-only tenant filtering (no RLS):** one forgotten `WHERE` = DSGVO breach. RLS is primary; EF filter is defence-in-depth.
- **Plain `SET` (not `SET LOCAL`/RESET) for tenant GUC:** stale context leaks across pooled connections.
- **String-interpolated tenant id in the interceptor SQL:** injection; parameterize.
- **`BYPASSRLS` on the app role:** silently disables all isolation.
- **Float/double for money:** cent drift → later EN 16931 rejections + un-auditable ledger.
- **Banker's rounding by default for VAT:** use AwayFromZero; ToEven diverges on midpoints.
- **Audit immutability enforced only in app code:** enforce at DB (REVOKE + trigger).
- **Client-side tier gating as the control:** server guard is authoritative.
- **Tokens in the SPA (localStorage):** use BFF + HttpOnly cookie.
- **Postgres `SEQUENCE`/`SERIAL` for future invoice numbers:** non-transactional gaps — not built in Phase 1, but do NOT model number columns as serial in any inert schema.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tenant isolation | Custom `WHERE tenant_id` everywhere | Postgres RLS (primary) + EF global query filter | One miss = breach; DB-enforced is the only safe primary control |
| Auth / SSO / MFA / org membership / invitations | Custom identity | Keycloak 26 (single realm + Organizations) | Rebuilding OIDC/MFA/invites/cross-tenant roles is huge and error-prone |
| Token handling in browser | SPA token storage + refresh | BFF pattern (server holds tokens, HttpOnly cookie) | Current OAuth browser BCP; avoids XSS token theft |
| Money type / rounding | Ad-hoc `decimal` + `Math.Round` scattered | `Money` value object (NodaMoney) + one `RoundingPolicy` | Rounding must be centralized & per-category for EN 16931 |
| Feature gating | Bespoke if/else per plan | Microsoft.FeatureManagement + custom per-tenant filter | Attribute/middleware ergonomics; one plan→capability source |
| Background jobs | Custom queue/threads | Hangfire (Postgres storage) | Retries, dashboard, persistence for free |
| Audit immutability | App-code "don't update" | DB REVOKE UPDATE/DELETE + trigger | App guards get bypassed by raw SQL/ORM |
| i18n | Custom string tables | react-i18next (FE) + IStringLocalizer/.resx (BE) | Pluralization, culture, fallbacks solved |
| PWA service worker | Hand-written SW | vite-plugin-pwa (Workbox) | Precache/update/manifest wired; you control runtime rules |
| Test DB isolation | Mocks / shared dev DB | Testcontainers (real Postgres) | RLS bugs only reproduce on real Postgres |

**Key insight:** In this domain the "boring" cross-cutting concerns (isolation, money, audit, auth) are exactly where correctness is legally load-bearing. Hand-rolling any of them is the single most likely cause of a rewrite or a compliance incident.

---

## Common Pitfalls

### Pitfall 1: Connection-pool stale tenant context
**What goes wrong:** GUC set with plain `SET` (session scope) survives when the pooled connection is handed to another tenant's request → intermittent cross-tenant reads.
**Why:** Npgsql pools connections; session state persists unless reset.
**How to avoid:** Use `SET LOCAL`/`set_config(...,is_local=true)` inside the request transaction, OR set session-scope and `RESET app.current_tenant` on `ConnectionClosing`. Add a Testcontainers test that opens two requests on the same pooled connection and asserts no leak.
**Warning signs:** Isolation passes in single-request tests, fails under concurrency.

### Pitfall 2: Missing composite index → RLS 100× slowdown
**What goes wrong:** RLS predicate can't use an index; sequential scans as tables grow.
**How to avoid:** `tenant_id` is the **leading** column of every primary access index.
**Warning signs:** Fast on empty dev DB, slow at scale.

### Pitfall 3: Banker's rounding on VAT
**What goes wrong:** C# `Math.Round` defaults to `ToEven`; midpoints diverge from EN 16931's expected half-up → future KoSIT rejections.
**How to avoid:** Central `RoundingPolicy` uses `MidpointRounding.AwayFromZero`; golden-file tests lock it.

### Pitfall 4: Rounding order (per-line/per-category vs per-document)
**What goes wrong:** Rounding the grand total instead of summing rounded per-category amounts violates BR-CO-15/BR-S-*.
**How to avoid:** Round each VAT category to 2 dp first, then sum. Golden files include mixed-rate cases.

### Pitfall 5: Caching financial data in the service worker
**What goes wrong:** PWA serves stale invoice/financial data offline → wrong numbers shown as truth.
**How to avoid:** `NetworkOnly` for `/api/*`; `navigateFallbackDenylist` excludes `/api`; only precache the static shell.

### Pitfall 6: Auth session not persistent / tokens in JS
**What goes wrong:** SPA stores tokens in localStorage (XSS risk) or session drops on refresh.
**How to avoid:** BFF + HttpOnly SameSite cookie; refresh handled server-side. Success criterion 1 ("bleibt angemeldet") is met by the cookie session.

### Pitfall 7: Retrofitting RLS/Money later
**What goes wrong:** Adding `tenant_id` + policies or migrating float→decimal after data exists is a full-schema migration.
**How to avoid:** Both land in Phase 1, on every table, from the first migration.

---

## Code Examples

### Testcontainers cross-tenant isolation test (the CI gate)
```csharp
// Boots real Postgres 18, applies migrations + RLS policies, seeds two tenants,
// sets app.current_tenant = A, asserts tenant B's rows are invisible.
public class RlsIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:18").Build();
    public async Task InitializeAsync() => await _pg.StartAsync();

    [Fact]
    public async Task Tenant_A_cannot_see_Tenant_B_rows()
    {
        // arrange: migrate, insert audit_events for tenantA and tenantB
        // act: open connection with app.current_tenant = tenantA
        // assert: SELECT returns only tenantA rows; count of tenantB rows == 0
    }

    [Fact]
    public async Task Audit_row_cannot_be_updated_or_deleted() { /* expect exception */ }
}
```

### Docker Compose (local dev)
```yaml
services:
  postgres:
    image: postgres:18
    environment: { POSTGRES_DB: numera, POSTGRES_PASSWORD: dev }
    ports: ["5432:5432"]
  keycloak:
    image: quay.io/keycloak/keycloak:26.2
    command: start-dev --features=organization
    environment: { KC_BOOTSTRAP_ADMIN_USERNAME: admin, KC_BOOTSTRAP_ADMIN_PASSWORD: admin }
    ports: ["8080:8080"]
  # (later) kosit-validator sidecar
```

### GitHub Actions CI (build + tests incl. Testcontainers)
```yaml
name: ci
on: [push, pull_request]
jobs:
  build-test:
    runs-on: ubuntu-latest   # Docker available → Testcontainers works
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release   # runs unit + Testcontainers RLS/audit suite
      # frontend:
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci --prefix web && npm run build --prefix web && npm test --prefix web
```

### Keycloak OIDC + BFF in ASP.NET Core (sketch)
```csharp
builder.Services.AddAuthentication(o => {
    o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie() // HttpOnly session cookie for the SPA
.AddOpenIdConnect(o => {
    o.Authority = "https://keycloak/realms/numera";
    o.ClientId = "numera-bff"; o.ClientSecret = cfg["Keycloak:Secret"];
    o.ResponseType = "code"; o.UsePkce = true; o.SaveTokens = true;
    o.Scope.Add("organization"); // org id → tenant_id claim
    o.GetClaimsFromUserInfoEndpoint = true;
});
```

---

## Hetzner vs UbiCloud (managed Postgres) — recommendation

STACK.md flagged this decision for Phase 1. **Recommendation: Hetzner Cloud (Nuremberg/Falkenstein) with self-managed Postgres 18 via Docker Compose/systemd for v1**, and revisit **UbiCloud managed Postgres** (hosts on Hetzner, Dutch B.V. → non-US jurisdiction, ~€50/mo, DPA available) *only if* DB operational burden (backups, PITR, upgrades) becomes a distraction. Rationale: Phase 1 is greenfield with no production load; a single self-managed Postgres keeps cost/complexity minimal and preserves full RLS/superuser control needed to set up roles, `BYPASSRLS` migration role, and policies. UbiCloud is the pragmatic upgrade path that keeps EU data sovereignty (the whole reason to avoid AWS/Azure) — abstract nothing special; a connection string swap. **Confidence: MEDIUM** (operational preference, not a hard technical constraint).

---

## Keycloak vs ASP.NET Core Identity — firm recommendation

**Recommendation: Keycloak 26, single realm + Organizations, BFF integration. Confidence: MEDIUM-HIGH.**

| Factor | Keycloak 26 (single realm + Organizations) | ASP.NET Core Identity |
|--------|--------------------------------------------|-----------------------|
| Setup effort v1 | Run one container (docker-compose), configure realm/client/`organization` scope. Moderate one-time cost. | Lower initial infra; but you build registration/login/session/MFA/lockout yourself |
| Tenant modeling | **1 Organization per tenant** in one realm — GA since v26, purpose-built for B2B SaaS. Org id ships in the token. | You model tenants in your own tables; no IAM-level tenancy |
| OIDC + ASP.NET + React | Standard OIDC code flow + PKCE; **BFF** keeps tokens server-side | N/A (in-app auth); you'd hand-roll any external SSO later |
| Invitation flow (Phase 8) | **Built in** (Organization invitations + required action) — the seam is free | Build entirely yourself |
| Steuerberater cross-tenant (Phase 8) | Fits realm/role/organization-membership model (a user in multiple Organizations) | Significant custom work |
| MFA / password policy / social login | Built in, configurable | Build/assemble yourself |
| Downside | Extra infra to run & patch; learning curve | Everything tenancy/SSO/invites is your code to write and secure |

**Why Keycloak wins for Numera specifically:** the LOCKED roadmap includes Phase 8 roles + Steuerberater cross-tenant access and multi-user tenants. Keycloak's Organizations model those natively and the invitation flow is already built — choosing Identity now means re-implementing exactly what Keycloak gives free, right when the product is trying to add finance features. The single realm avoids the realm-per-tenant scaling trap (thousands of small tenants). **Single realm + Organizations, NOT realm-per-tenant.**

**Realm-per-tenant is explicitly the wrong choice here** — it doesn't scale to thousands of freelancer tenants (admin/migration overhead per realm) and Organizations exist precisely to avoid it.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Keycloak realm-per-tenant | Single realm + **Organizations** (GA) | Keycloak 26 (2024/25) | Native single-realm B2B multi-tenancy + invitations; use this |
| SPA holds access/refresh tokens | **BFF** (server-side tokens, HttpOnly cookie) | OAuth browser BCP (ongoing) | Stronger security; also gives persistent session |
| `SET app.current_tenant` (session) | `SET LOCAL`/`set_config(is_local)` + RESET | RLS best-practice consensus | Prevents pool-reuse leaks |
| Sequential/serial IDs | **UUIDv7** (`Guid.CreateVersion7()`→`uuidv7()` on PG18) | Npgsql EF 10 / PG18 | Time-ordered, index-friendly, non-guessable PKs |
| Banker's rounding (C# default) | Explicit **AwayFromZero** for VAT | EN 16931 requirement | Avoids future KoSIT rejections |

**Deprecated/outdated:**
- .NET 9 (STS, EOL 2026-11) for greenfield — use .NET 10 LTS.
- GoCardless/Nordigen bank data — closed to new accounts (not this phase, but noted).

---

## Open Questions

1. **VAT rounding direction per EN 16931 field (AwayFromZero everywhere?)**
   - Know: per-category-round-then-sum is mandatory; AwayFromZero is the correct German default.
   - Unclear: whether every BT field uses the same half-rounding direction in all edge cases.
   - Recommendation: implement AwayFromZero now; **lock with KoSIT-validated golden files in the e-invoicing phase**. Confidence LOW-MEDIUM on universality; HIGH on "not banker's default."

2. **Hangfire scope in Phase 1**
   - Know: worker tier ships as a skeleton (ARCHITECTURE.md build-order step 4 is later, but the seam should exist).
   - Recommendation: wire Hangfire + one trivial job (e.g. welcome email) to prove the enqueue-after-commit + tenant-context-in-job pattern; don't build real jobs yet.

3. **Where the tenant↔user membership lives (Keycloak vs app DB)**
   - Know: Keycloak Organizations hold membership; the app also needs `tenant_id` for RLS.
   - Recommendation: Keycloak is the IAM source of truth; on login, map the `organization` claim → an app-side `tenants`/`memberships` table (mirror) so RLS and joins work without live IAM calls. Confidence MEDIUM.

4. **Entitlement storage: config vs DB**
   - Recommendation: DB table `tenant_plans(tenant_id, plan)` + a static plan→capability map in code (versioned with the app). A later Stripe webhook updates `tenant_plans`. Confidence HIGH.

---

## Sources

### Primary (HIGH confidence)
- Microsoft Learn — EF Core 10 What's New, Multi-tenancy, Localization, FeatureManagement (learn.microsoft.com) — versions, interceptors, IStringLocalizer, feature filters
- Npgsql 10.0 release notes (npgsql.org/efcore/release-notes/10.0.html) — `Guid.CreateVersion7()`→`uuidv7()`, PG18 targeting
- .NET 10 GA announcement (devblogs.microsoft.com/dotnet/announcing-dotnet-10) — GA 2025-11-11, LTS to 2028-11-10
- Keycloak Organizations announcement + docs (keycloak.org/2024/06/announcement-keycloak-organizations) — GA in 26, invitations, org scope
- Project research: STACK.md, ARCHITECTURE.md, PITFALLS.md (.planning/research/) — LOCKED decisions, patterns, liability pitfalls

### Secondary (MEDIUM confidence)
- bytefish.de "Multitenancy with ASP.NET Core and PostgreSQL RLS" — interceptor pattern (**corrected here** for parameterization + pool reset)
- ricofritzsche.me "Mastering PostgreSQL RLS for Multi-Tenancy" — SET LOCAL, composite index, BYPASSRLS
- skycloak.io / phasetwo.io / keycloakpro.com — Keycloak 26 Organizations multi-tenancy + invitation flow
- vite-pwa-org.netlify.app (Workbox/inject-manifest) + vite-plugin-pwa issue #626 — runtimeCaching, API exclusion
- antondevtips.com — .NET modular monolith / vertical-slice structure (2026), PublicApi module boundaries
- Immutable Postgres audit log articles (hoop.dev, appmaster.io, oneuptime) — REVOKE + trigger, hash-chain (deferred)
- Peppol validator / EN16931 error map (peppolvalidator.com) + MidpointRounding docs (learn.microsoft.com) — BR-CO-17/BR-S rounding

### Tertiary (LOW confidence — flagged for validation)
- Exact AwayFromZero-vs-ToEven universality across all EN 16931 BT fields — validate with KoSIT golden files in e-invoicing phase

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all versions GA and cross-verified (Nov 2025 releases)
- Architecture/patterns: HIGH — consistent across project research + current .NET/RLS/Keycloak sources
- Money rounding direction: MEDIUM-HIGH (per-category-then-sum) / LOW-MEDIUM (AwayFromZero universality) — flagged
- Auth recommendation: MEDIUM-HIGH — judgment call, well-supported by roadmap needs
- Pitfalls: HIGH — grounded in PITFALLS.md legal/compliance research

**Research date:** 2026-07-09
**Valid until:** ~2026-08-09 (stack is LTS/stable; re-check Keycloak Organizations API details and vite-plugin-pwa major if >30 days)
