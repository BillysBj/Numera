---
phase: 08-crm-ausbau-steuerberater-zugang
plan: 01
subsystem: auth
tags: [authorization, middleware, rls, postgres, tax-advisor]
requires:
  - phase: 01-platform-foundation
    provides: Tenant-scoped membership persistence and request tenancy
provides:
  - DB-authoritative, per-request membership-role resolution
  - Global two-sided TaxAdvisor access enforcement
  - Owner-only authorization policy for team management
affects: [08-02-team-management, crm, api-security]
tech-stack:
  added: []
  patterns: [pure authorization policy, global default-deny middleware, RLS-scoped role lookup]
key-files:
  created:
    - src/Numera.Api/Auth/ICurrentUserRole.cs
    - src/Numera.Api/Auth/CurrentUserRole.cs
    - src/Numera.Api/Auth/RequireOwnerRequirement.cs
    - src/Numera.Api/Auth/ReadOnlyAccessPolicy.cs
    - src/Numera.Api/Auth/ReadOnlyWriteGuardMiddleware.cs
  modified:
    - src/Numera.Api/Program.cs
key-decisions:
  - "Membership roles remain API-owned so Tenancy has no dependency on Db."
  - "TaxAdvisor access is globally write-denied and read-default-denied outside the explicit Belege/Auswertungen allow-list."
patterns-established:
  - "Resolve authorization facts from the RLS-scoped database once per request, never from role claims."
  - "Put future TaxAdvisor-readable surfaces into the single pure allow-list deliberately."
completed: 2026-07-28
---

# Phase 8 Plan 01: PLAT-03 Enforcement Foundation Summary

**DB-authoritative role resolution with a global TaxAdvisor write guard/read allow-list and an owner-only authorization policy**

## Accomplishments

- Added a scoped, null-memoising role resolver over the current tenant and user membership.
- Added pure access-policy coverage for the complete TaxAdvisor method/path matrix.
- Added global middleware returning RFC-style 403 ProblemDetails for denied requests.
- Registered the `RequireOwner` policy and DB-backed authorization handler.
- Proved role resolution and tenant isolation through real PostgreSQL 18 as `numera_app`.

## Files Created/Modified

- `src/Numera.Api/Auth/ICurrentUserRole.cs` - API-host role-resolution seam.
- `src/Numera.Api/Auth/CurrentUserRole.cs` - memoised RLS-scoped membership query.
- `src/Numera.Api/Auth/RequireOwnerRequirement.cs` - owner requirement and async handler.
- `src/Numera.Api/Auth/ReadOnlyAccessPolicy.cs` - pure two-sided allow/deny rules.
- `src/Numera.Api/Auth/ReadOnlyWriteGuardMiddleware.cs` - global enforcement and 403 response.
- `src/Numera.Api/Program.cs` - DI, policy, handler, and middleware registration.
- `tests/Numera.Platform.Tests/Auth/ReadOnlyAccessPolicyTests.cs` - policy matrix.
- `tests/Numera.IntegrationTests/CurrentUserRoleTests.cs` - PostgreSQL role/RLS proofs.

## Verification

- `dotnet build Numera.sln -c Debug`: succeeded, 0 warnings, 0 errors.
- `dotnet test tests/Numera.Platform.Tests`: 131 passed, 0 failed, 0 skipped.
- `dotnet test tests/Numera.IntegrationTests`: 124 passed, 0 failed, 0 skipped.

## Deviations from Plan

None. The first build found a missing test-file `using Xunit`; it was added before final verification.

## User Setup Required

None.

## Next Phase Readiness

Team-management endpoints can apply `.RequireAuthorization("RequireOwner")`; future endpoints are automatically denied to TaxAdvisor unless their reads are deliberately allow-listed.

---
*Phase: 08-crm-ausbau-steuerberater-zugang*
*Completed: 2026-07-28*
