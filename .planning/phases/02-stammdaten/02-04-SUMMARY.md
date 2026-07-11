---
phase: 02-stammdaten
plan: 04
subsystem: api
tags: [minimal-api, fluentvalidation, crm, partners, audit, rls, rfc7807]

# Dependency graph
requires:
  - phase: 02-02
    provides: Numera.Modules.Crm (BusinessPartner + owned Address, PartnerContact, PartnerNote, PartnerActivity, offline VatId validator) + _Crm RLS migration
  - phase: 02-01
    provides: named "Tenant" + "NotArchived" query filters and IArchivable soft-delete
  - phase: 01-04
    provides: IAuditWriter append-in-caller's-transaction audit seam
  - phase: 01-07
    provides: BFF cookie auth + TenantResolutionMiddleware (organization claim -> ICurrentTenant)
provides:
  - Partner management HTTP API — /api/partners CRUD + archive/unarchive (no hard delete)
  - Contacts + notes sub-resource CRUD per partner (CRM-03 notes)
  - Read-only per-partner activity timeline (CRM-02 read) fed by partner_activities
  - FluentValidation DI (AddValidatorsFromAssemblyContaining<Program>) scanning the Api assembly
  - Atomic mutate->audit->activity write pattern (single SaveChanges) for every partner mutation
affects: [02-05, 02-06, phase-03-invoicing]

# Tech tracking
tech-stack:
  added: [FluentValidation.DependencyInjectionExtensions 12.1.1 (Apache-2.0)]
  patterns:
    - "RFC 7807 ValidationProblem from IValidator<T> result.ToDictionary()"
    - "Atomic mutate->audit->activity: one SaveChangesAsync commits change + AuditEvent + PartnerActivity"
    - "archived toggle via IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter]) — keeps tenant filter + RLS"
    - "IAuditEvent adapter record (PartnerAuditEvent) supplies action/before/after; writer stamps tenant+actor"

key-files:
  created:
    - src/Numera.Api/Contracts/PartnerContracts.cs
    - src/Numera.Api/Validators/PartnerValidators.cs
    - src/Numera.Api/Endpoints/PartnerEndpoints.cs
  modified:
    - src/Numera.Api/Program.cs
    - src/Numera.Api/Numera.Api.csproj

key-decisions:
  - "IAuditWriter takes IAuditEvent (not the plan's (action,id,before,after,ct) overload) — added a small internal PartnerAuditEvent : IAuditEvent adapter; before/after are JSON snapshots of the partner"
  - "GET by id and all mutations IgnoreQueryFilters([NotArchived]) so archived partners stay readable/editable/unarchivable by id; only the default list hides them"
  - "Contacts and notes are hard-deletable child data (not GoBD records); the archive-never-delete rule is partner-only"
  - "Notes/contacts write no audit event (GoBD-irrelevant); only partner create/update/archive/unarchive do — note create still appends a NoteAdded activity to the timeline"

patterns-established:
  - "Sub-resources under MapGroup('/api/partners').RequireAuthorization() with ownership asserted via PartnerId+Id (RLS covers tenant)"
  - "Validators live in the Api assembly so the assembly scan auto-discovers later modules' validators (02-05 needs no DI edit)"

# Metrics
duration: 10min
completed: 2026-07-11
---

# Phase 2 Plan 04: Partner Backend API Summary

**Minimal-API partner management surface — /api/partners CRUD + archive, contacts, notes and a read-only activity timeline — validated by FluentValidation and audited via an atomic mutate->audit->activity SaveChanges.**

## Performance

- **Duration:** 10 min
- **Started:** 2026-07-11T11:45:56Z
- **Completed:** 2026-07-11T11:55:23Z
- **Tasks:** 3
- **Files modified:** 5 (3 created, 2 modified)

## Accomplishments
- Partner list with server-side pagination, text search (Name), role filter (customer/supplier) and an archived toggle, returning an `{items,page,pageSize,total}` envelope
- Partner create/read/update with RFC 7807 ValidationProblem on invalid input; "must be customer or supplier" enforced; offline EU VAT-ID plausibility check (never VIES)
- Archive/unarchive endpoints (POST) with NO partner hard-delete route; every partner mutation writes an AuditEvent + a PartnerActivity in one SaveChanges
- Contacts + notes full CRUD per partner; note create also lands a NoteAdded timeline entry; read-only `/activities` timeline ordered newest-first (CRM-02/03)
- FluentValidation registered via `AddValidatorsFromAssemblyContaining<Program>()` — the assembly scan will also pick up the 02-05 Catalog validators with no further DI edit

## Task Commits

Each task was committed atomically:

1. **Task 1: DTOs + FluentValidation validators + DI registration** - `9f38b9a` (feat)
2. **Task 2: Partner CRUD + archive endpoints** - `b8ac581` (feat)
3. **Task 3: Contacts, notes, activity-timeline sub-resources** - `3820a42` (feat)

**Plan metadata:** _(this docs commit)_

## Files Created/Modified
- `src/Numera.Api/Contracts/PartnerContracts.cs` - sealed-record request/response DTOs (create/update/list/detail/address/contact/note/activity); decimal money, never float
- `src/Numera.Api/Validators/PartnerValidators.cs` - AbstractValidator rules: role-required, address/currency/country-code, offline VAT-ID plausibility, Skonto 0..100
- `src/Numera.Api/Endpoints/PartnerEndpoints.cs` - MapPartnerEndpoints: partner CRUD + archive, contacts, notes, activities; PartnerAuditEvent adapter + JSON snapshot helper
- `src/Numera.Api/Program.cs` - AddValidatorsFromAssemblyContaining<Program>() + app.MapPartnerEndpoints()
- `src/Numera.Api/Numera.Api.csproj` - FluentValidation.DependencyInjectionExtensions 12.1.1

## Decisions Made
- **Audit API shape:** the plan sketched `audit.RecordAsync("partner.created", p.Id, before, after, ct)`, but the real `IAuditWriter.RecordAsync` takes an `IAuditEvent`. Added a minimal internal `PartnerAuditEvent : IAuditEvent` record and pass JSON before/after snapshots of the partner. Semantics identical; tenant + actor are still stamped by the writer.
- **Archived visibility:** default list hides archived (NotArchived filter); GET-by-id, PUT, archive/unarchive and existence checks `IgnoreQueryFilters([NotArchivedFilter])` so an archived partner remains readable and can be unarchived. The list's `archived=true` toggle reveals archived rows per the plan.
- **Child-data delete policy:** contacts and notes are hard-deletable (GoBD-irrelevant); partners never are (archive-only).
- **TaxCategory namespace:** DefaultTaxCategory is `Numera.Platform.Money.TaxCategory` (not in the Crm namespace) — added the using.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Adapted to the real IAuditWriter signature**
- **Found during:** Task 2 (partner create/update/archive handlers)
- **Issue:** The plan's `audit.RecordAsync(action, id, before, after, ct)` overload does not exist; the seam is `RecordAsync(IAuditEvent evt, CancellationToken ct)`.
- **Fix:** Added an internal `sealed record PartnerAuditEvent(string Action, Guid? EntityId, string? Before, string? After) : IAuditEvent` (EntityType = nameof(BusinessPartner)) and a `Snapshot(partner)` JSON helper for before/after. Writer still stamps tenant + actor from ambient context.
- **Files modified:** src/Numera.Api/Endpoints/PartnerEndpoints.cs
- **Verification:** Build 0/0; audit row + activity are added to the same DbContext and committed by the handler's single SaveChangesAsync.
- **Committed in:** b8ac581 (Task 2 commit)

**2. [Rule 3 - Blocking] Corrected TaxCategory namespace in DTOs**
- **Found during:** Task 1 (build)
- **Issue:** CS0246 — `TaxCategory` lives in `Numera.Platform.Money`, not `Numera.Modules.Crm`.
- **Fix:** Added `using Numera.Platform.Money;` to PartnerContracts.cs.
- **Files modified:** src/Numera.Api/Contracts/PartnerContracts.cs
- **Verification:** Build 0/0.
- **Committed in:** 9f38b9a (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 3 - blocking)
**Impact on plan:** Both were adaptations to the actual codebase API surface; no scope change, no new behavior beyond the plan's intent.

## Issues Encountered
None beyond the deviations above.

## User Setup Required
None - no external service configuration required. FluentValidation is a code dependency; no env vars or dashboards.

## Next Phase Readiness
- **02-05 (Catalog API, same wave):** the `AddValidatorsFromAssemblyContaining<Program>()` scan is in place, so 02-05 only appends its own `app.MapCatalogEndpoints()` and drops validators into the Api assembly — no DI edit. Program.cs was committed cleanly (no conflict).
- **02-06 (frontend):** the list endpoint's `{items,page,pageSize,total}` + `page/pageSize/q/role/archived` params are ready for TanStack Table manualPagination.
- **Phase 3 (invoicing):** the activity timeline reads from `partner_activities` today and can be augmented with a documents tab later without an API break.

## Self-Check: PASSED

- FOUND: src/Numera.Api/Contracts/PartnerContracts.cs
- FOUND: src/Numera.Api/Validators/PartnerValidators.cs
- FOUND: src/Numera.Api/Endpoints/PartnerEndpoints.cs
- FOUND commit: 9f38b9a
- FOUND commit: b8ac581
- FOUND commit: 3820a42

---
*Phase: 02-stammdaten*
*Completed: 2026-07-11*
