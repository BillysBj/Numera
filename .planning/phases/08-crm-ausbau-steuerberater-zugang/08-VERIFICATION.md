---
phase: 08-crm-ausbau-steuerberater-zugang
status: passed
verified_by: claude (reviewer of the Codex-delegated implementation)
verified_on: 2026-07-28
method: independent build + full integration/platform/vitest re-runs with the .NET 10 SDK; goal-backward mapping; human-verify checkpoint approved
---

# Phase 8 Verification — CRM-Ausbau & Steuerberater-Zugang

**Goal:** Nutzer kann Kundenbeziehungen aktiv managen und seinen Steuerberater kontrolliert einbinden.

**Result:** PASSED. All 4 success criteria delivered and backed by tests on real Postgres 18. The 08-05 human-verify checkpoint (incl. logging in AS a Steuerberater and confirming read-only both ways) was approved by the user on 2026-07-28.

## Success criteria → evidence

1. **Aufgaben mit Erinnerungen/Fälligkeiten anlegen und verfolgen.**
   - `PartnerTask` child of BusinessPartner (title, description, due date, Open/Done status, optional assignee), per-table RLS; `/api/partners/{id}/tasks` CRUD + complete + openOnly/overdueOnly filters (overdue = DueDate < today && Status==Open). React Aufgaben tab with overdue badges. Email reminders deferred (query-driven, decision 5). (08-03)
   - Tests: `PartnerTaskTests` — CRUD/complete persistence, overdue filter, RLS.

2. **Dateien am Kunden ablegen (Kundenakte).**
   - Append-only `CustomerFile` (Postgres bytea + filename/content-type/size/uploader), RLS (ENABLE+FORCE+tenant_isolation) + REVOKE UPDATE/DELETE + a `customer_file_immutable` trigger; endpoints upload / list (metadata) / download only — NO delete/edit (decision 4). Upload guard: 20 MB + content-type allow-list + DisableAntiforgery. React Dateien tab (no delete control). (08-04)
   - Tests: `CustomerFileTests` — byte round-trip, metadata-only list, raw UPDATE/DELETE both raise the append-only exception, RLS.

3. **Teammitglieder einladen und Rollen vergeben (Inhaber/Mitarbeiter/Steuerberater).**
   - `InvitationService` direct-adds via the Keycloak Admin API (find/create user, add to org, upsert membership with role, audited); `/api/team` list/invite/change-role/remove is Owner-only (`RequireOwner`) AND MultiUser(M+)-gated (403 upgrade on S); last-Owner guard (422) prevents lockout. React team feature + UpgradeHint. (08-02)
   - Tests: `TeamManagementTests` — invite/role-change/remove persistence, last-Owner guard, RLS.

4. **Ein Steuerberater hat LESENDEN Zugriff auf Belege/Auswertungen, kann aber nichts verändern.**
   - RLS does NOT gate intra-tenant role authz (a TaxAdvisor is a legitimate member), so enforcement is app-level + global: `ICurrentUserRole` resolves the role from the DB membership per request; `ReadOnlyAccessPolicy` (pure) denies every unsafe method for a TaxAdvisor and allows GET only for the Belege/Auswertungen allow-list (/api/documents, /api/open-items, /api/inbound-documents, /api/me, /api/auth — segment-safe), default-denying CRM master data + config; `ReadOnlyWriteGuardMiddleware` applies it GLOBALLY (after auth+tenant resolution) so future endpoints are covered by construction; `RequireOwner` gates team management. (08-01)
   - Tests: `ReadOnlyAccessPolicyTests` (32-case pure allow/deny matrix — TaxAdvisor denied all writes + non-allow-listed reads, allowed Belege/Auswertungen GETs); `CurrentUserRoleTests` (role resolves from membership, RLS-scoped → null across tenants). Human-verify confirmed the middleware end-to-end (advisor reads documents/open-items/inbound, is 403'd on every write and on partners/catalog/tasks/files/team reads).

## Verification runs (final committed state)
- Full solution build: **0 warnings / 0 errors** (.NET 10 SDK).
- Integration suite: **137/137** on Testcontainers postgres:18. Platform suite: **131/131**. Frontend: `tsc -b` clean, **vitest 56/56**.

## Notes / accepted boundaries (non-blocking)
- The read-only guard MIDDLEWARE end-to-end (an actual HTTP 403 for a logged-in Steuerberater) is not automated — the codebase has no `WebApplicationFactory`. Covered instead by the exhaustive pure-policy matrix + RLS-scoped role-resolution tests + the human-verify checkpoint. Optional follow-up: a WebApplicationFactory-based guard e2e test.
- Team invitations are direct-add (decision 3); email-invite flow deferred.
- Kundenakte files are append-only (decision 4); no delete/edit by design.
- The TaxAdvisor read allow-list is explicit in `ReadOnlyAccessPolicy.ReadPrefixes`; any future report/Auswertungen surface must be added there deliberately (documented in-code).
