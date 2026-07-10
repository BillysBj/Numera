---
phase: 01-plattform-kern
plan: 06
subsystem: auth
tags: [keycloak, oidc, bff, pkce, cookie-auth, rls, hangfire, feature-management, npgsql, multi-tenancy]

# Dependency graph
requires:
  - phase: 01-plattform-kern (01-01)
    provides: NumeraDbContext + TenantConnectionInterceptor (app.current_tenant GUC) + ICurrentTenant/TenantContext
  - phase: 01-plattform-kern (01-02)
    provides: FORCE RLS on every tenant table + least-privilege numera_app / numera_migrator roles
  - phase: 01-plattform-kern (01-04)
    provides: append-only audit_events + ICurrentUser seam + in-transaction AuditWriter
  - phase: 01-plattform-kern (01-05)
    provides: IEntitlementService + PlanCapabilityMap + PlanFeatureFilter
provides:
  - BFF authentication (confidential Keycloak OIDC client, tokens server-side, HttpOnly session cookie)
  - Registration (Keycloak user + Organization mirrored to tenants + Owner membership + tenant.created audit)
  - TenantResolutionMiddleware (organization-id claim -> ICurrentTenant so RLS engages per request)
  - /api/me + /api/me/entitlements + a server-gated /api/features/einvoicing-check
  - Api + Worker DI wiring (DbContext, tenancy, audit, entitlements, Hangfire) + welcome-email job
  - Reproducible keycloak/realm-numera.json (organization feature, numera-bff client, full OIDC scopes)
affects: [01-08, frontend, e-invoicing, every tenant-scoped feature]

# Tech tracking
tech-stack:
  added: [Microsoft.AspNetCore.Authentication.OpenIdConnect, Hangfire.AspNetCore, Hangfire.PostgreSql, Microsoft.FeatureManagement (scoped)]
  patterns:
    - "BFF: SPA holds no tokens; confidential OIDC client stores tokens server-side (SaveTokens) and issues an HttpOnly SameSite cookie"
    - "Keycloak Organizations as tenant source of truth; app DB mirrors org->tenants and sub->membership so RLS/joins work without live IAM calls"
    - "Runtime connects as least-privilege numera_app (RLS-subject); Hangfire gets its own schema-owning connection"
    - "organization membership mapper emits the org UUID (addOrganizationId) == tenants.id for claim->tenant resolution"

key-files:
  created:
    - src/Numera.Api/Auth/KeycloakBffExtensions.cs
    - src/Numera.Api/Auth/TenantResolutionMiddleware.cs
    - src/Numera.Api/Auth/CurrentUser.cs
    - src/Numera.Api/Services/RegistrationService.cs
    - src/Numera.Api/Endpoints/AuthEndpoints.cs
    - src/Numera.Api/Endpoints/MeEndpoints.cs
    - src/Numera.Api/Jobs/WelcomeEmailJob.cs
    - keycloak/realm-numera.json
  modified:
    - src/Numera.Api/Program.cs
    - src/Numera.Worker/Program.cs
    - src/Numera.Api/appsettings.Development.json
    - src/Numera.Worker/appsettings.json
    - docker-compose.yml

key-decisions:
  - "App runtime connects as numera_app (NO BYPASSRLS), not the numera superuser, so RLS is the real per-request isolation control; Hangfire uses a separate schema-owning connection because its tables are infrastructure, not tenant data"
  - "Feature filters that consume scoped services require AddScopedFeatureManagement() (not AddFeatureManagement()) or DI scope-validation rejects the singleton filter"
  - "The realm must NOT pin an explicit clientScopes array to a subset — doing so suppresses Keycloak's built-in scopes; bake the full set in, and enable addOrganizationId so the organization claim carries the tenant UUID"

patterns-established:
  - "BFF token custody: tokens live in the encrypted server-side cookie ticket; the browser only ever sees numera.session (HttpOnly)"
  - "Tenant flows from the OIDC organization claim -> TenantResolutionMiddleware -> ICurrentTenant -> interceptor GUC -> RLS"
  - "enqueue-after-commit background jobs re-establish tenant context in their own DI scope"

# Metrics
duration: 45min
completed: 2026-07-10
---

# Phase 01 Plan 06: Auth + BFF + Tenant Resolution Summary

**Keycloak-26 BFF (confidential OIDC + PKCE, tokens server-side, HttpOnly session cookie) with registration that mirrors a Keycloak Organization into RLS-scoped tenants/membership, org-claim->tenant middleware, /api/me + entitlements, and a Hangfire welcome job — verified live end-to-end.**

## Performance

- **Duration:** ~45 min (live-verification continuation; code tasks were committed pre-checkpoint)
- **Completed:** 2026-07-10T11:09Z
- **Tasks:** 3 (all committed pre-checkpoint) + live verification + 3 deviation fixes
- **Files modified:** 13

## Accomplishments
- BFF auth wired: confidential `numera-bff` OIDC client, code flow + PKCE, `SaveTokens` server-side, HttpOnly `numera.session` cookie — the SPA never receives a token.
- Registration creates a Keycloak user + Organization and mirrors them into `tenants` (Plan S) + Owner `membership` + a `tenant.created` audit row in one transaction, then enqueues the welcome-email job after commit.
- `TenantResolutionMiddleware` maps the `organization` claim (org UUID) to `ICurrentTenant`, so the connection interceptor sets `app.current_tenant` and RLS filters every request.
- `/api/me`, `/api/me/entitlements`, and a server-gated `/api/features/einvoicing-check` all work against a live login.
- Reproducible realm import validated by deleting + reimporting the realm from the artifact alone.

## Task Commits

Code tasks (committed by the pre-checkpoint executor):

1. **Task 1: Keycloak realm import + BFF OIDC/cookie auth + platform DI wiring** - `d95b1c1` (feat)
2. **Task 2: Registration (Keycloak org+user -> tenant+membership) + tenant middleware** - `aa27371` (feat)
3. **Task 3: /api/me + /api/me/entitlements + server-gated feature example** - `5ea81fe` (feat)

Deviation fixes (this continuation, from live verification):

4. **RLS runtime role + scoped feature management** - `101d7b6` (fix)
5. **Realm import: full OIDC scopes + organization-id claim** - `e4807f6` (fix)

**Plan metadata:** committed with STATE.md (docs)

## Files Created/Modified
- `src/Numera.Api/Auth/KeycloakBffExtensions.cs` - Cookie + OIDC BFF wiring; tokens server-side; API XHR gets 401/403 not 302
- `src/Numera.Api/Auth/TenantResolutionMiddleware.cs` - org claim (UUID) -> ICurrentTenant per request
- `src/Numera.Api/Auth/CurrentUser.cs` - ICurrentUser from the authenticated `sub`
- `src/Numera.Api/Services/RegistrationService.cs` - Keycloak Admin API user+org creation, DB mirror, audit, job enqueue
- `src/Numera.Api/Endpoints/AuthEndpoints.cs` - register / login (OIDC challenge) / logout
- `src/Numera.Api/Endpoints/MeEndpoints.cs` - /api/me, /api/me/entitlements, gated einvoicing-check
- `src/Numera.Api/Jobs/WelcomeEmailJob.cs` - trivial enqueue-after-commit job re-establishing tenant context
- `src/Numera.Api/Program.cs` - DI wiring; numera_app runtime conn; Hangfire on separate conn; AddScopedFeatureManagement
- `src/Numera.Worker/Program.cs` - mirrored DbContext/tenancy/Hangfire wiring
- `src/Numera.Api/appsettings.Development.json` / `src/Numera.Worker/appsettings.json` - numera_app Default + Hangfire superuser conn
- `keycloak/realm-numera.json` - realm import: org feature, numera-bff client, full OIDC scopes, org-id mapper

## Decisions Made
- **Runtime as numera_app, not superuser** — see deviation 1. Makes RLS the unconditional isolation control at runtime, matching the 01-02 design.
- **Hangfire on its own connection** — Hangfire installs/owns a schema (DDL) and stores non-tenant infrastructure data, so it uses a schema-owning connection while the tenant DbContext stays least-privilege.
- **Full scope set baked into the realm** — an explicit partial clientScopes array suppresses Keycloak built-ins; the artifact now carries the complete set plus the org-id mapper.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1/2 - Bug/Security] Runtime app bypassed RLS by connecting as a superuser**
- **Found during:** Live verification (pre-flight DB role inspection)
- **Issue:** `ConnectionStrings:Default` used the `numera` superuser (BYPASSRLS), silently defeating Row-Level Security — the plan's primary tenant-isolation control and success criterion ("tenant context flows into RLS on every request"). Only the EF query filter was isolating; RLS was inert.
- **Fix:** Switched the tenant-scoped `NumeraDbContext` to `numera_app` (NO BYPASSRLS, the documented runtime role in `scripts/db-roles.sql`) and gave Hangfire its own superuser connection (`ConnectionStrings:Hangfire`) so it can install its schema.
- **Files modified:** src/Numera.Api/appsettings.Development.json, src/Numera.Worker/appsettings.json, src/Numera.Api/Program.cs, src/Numera.Worker/Program.cs
- **Verification:** As `numera_app`, the correct tenant GUC sees its `tenants/membership/audit` rows (1/1/1), a different GUC sees 0/0/0, and an unset GUC fails closed; registration inserts still pass the RLS WITH CHECK. Hangfire installed its schema on its own connection and the welcome job ran under RLS.
- **Committed in:** `101d7b6`

**2. [Rule 1 - Bug] Feature filter failed DI scope validation**
- **Found during:** Live verification (first real host startup in Development)
- **Issue:** `AddFeatureManagement().AddFeatureFilter<PlanFeatureFilter>()` registers the filter as a singleton, but it consumes the scoped `IEntitlementService`. The host threw at startup: "Cannot consume scoped service ... from singleton". Prior "builds" only compiled and never started the host, so this was never caught.
- **Fix:** `AddScopedFeatureManagement().AddFeatureFilter<PlanFeatureFilter>()` so the feature manager and filters live in the request scope.
- **Files modified:** src/Numera.Api/Program.cs
- **Verification:** Host starts; `/api/features/einvoicing-check` returns 403 (S) and 200 (XL).
- **Committed in:** `101d7b6`

**3. [Rule 1 - Bug] Realm import broke OIDC login and tenant resolution**
- **Found during:** Live verification (scripted OIDC login)
- **Issue:** (a) An explicit `clientScopes: [organization]` array suppressed Keycloak's built-in scopes, so the BFF's `openid profile email organization` request was rejected (`invalid_scope`) and the login challenge 500'd. (b) The organization membership mapper emitted only the org alias, so `TenantResolutionMiddleware` could not derive `tenant_id` and `/api/me` returned 409.
- **Fix:** Baked the full default scope set into the realm (profile, email, roles, web-origins, acr, address, phone, microprofile-jwt) alongside organization, and enabled `addOrganizationId` + `userinfo.token.claim` on the org mapper so the claim carries the org UUID (== `tenants.id`) and reaches the principal.
- **Files modified:** keycloak/realm-numera.json
- **Verification:** Deleted + reimported the realm from the artifact alone (no manual admin tweaks): register -> OIDC login (HttpOnly cookie, no client tokens) -> `/api/me` resolves `{tenant, role: Owner}` -> entitlements `[DataExport]` (S) -> gated endpoint 403 (S) / 200 (XL).
- **Committed in:** `e4807f6`

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 bug+security). **Impact:** All were correctness/security defects that only surface at runtime (not at compile time); none expanded scope. The plan's artifacts are unchanged in intent — the fixes make them actually work and be reproducible.

## Authentication Gates

- **Keycloak confidential client secret (`Keycloak:ClientSecret`)** — provided by the user (Option B: regenerated in the admin console) and stored via `dotnet user-secrets` for the Api project (`UserSecretsId=numera-api`), never committed. After each realm reimport during verification the secret was restored to the same value via the Keycloak Admin API so it stays in sync with the user-secrets store.

## Issues Encountered
- Scripting the OIDC browser flow over plain HTTP was non-trivial: Keycloak/ASP.NET set `Secure` cookies which a real browser sends to `localhost` (a trustworthy origin) but curl/urllib do not; a manual cookie client (ignoring Secure/domain) reproduced browser behavior. Keycloak Organizations also uses an identity-first login (username -> password) and a first-login `VERIFY_PROFILE` step.

## Known Follow-ups (not blocking)
- **First-login VERIFY_PROFILE:** registration creates the Keycloak user with only email/password (no first/last name), so the default realm prompts to complete the profile on first login. Login still completes. If undesired, either collect a name at registration or make firstName/lastName optional in the realm user-profile config. Deferred as a product decision, not a correctness issue.

## Next Phase Readiness
- Success criterion 1 (register -> company -> login -> persistent session) is live and reproducible; RLS receives a tenant on every request; entitlements are exposed and server-enforced.
- Containers (postgres, keycloak with the reimported `numera` realm) are left running for wave 5 (01-08). The Api's client secret is in user-secrets; Keycloak's `numera-bff` secret matches it.

---
*Phase: 01-plattform-kern*
*Completed: 2026-07-10*
