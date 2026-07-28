# Phase 8 Context — CRM-Ausbau & Steuerberater-Zugang

Decisions locked before planning (4 engineering by Claude-as-architect, 3 product/legal/pricing by the user). No `/gsd:discuss-phase` run; these resolve the open questions from `08-RESEARCH.md`.

## Decisions (LOCKED — plans must honor exactly)

1. **PLAT-03 enforcement — TWO-SIDED (write-guard + read allow-list).** Role is resolved once per request from the DB `membership` (already app-authoritative, as `MeEndpoints`/`EntitlementService` do) via a new scoped `ICurrentUserRole` seam (place it so Tenancy does NOT depend on Db — e.g. the seam in Tenancy, resolution in the Api/Db layer).
   - **Write-guard (global, default-deny):** a global middleware/endpoint-filter rejects EVERY unsafe HTTP method (POST/PUT/PATCH/DELETE) with 403 for a `TaxAdvisor` — server-authoritative and impossible to forget on future endpoints. Allow-list only `/api/auth` (+ `/health`).
   - **Read allow-list (USER decision: "Belege + Auswertungen only"):** a `TaxAdvisor` may READ only Belege + Auswertungen, NOT CRM master data. ALLOW GET for: sales documents `/api/documents/*` (incl. detail + PDF/XRechnung/ZUGFeRD downloads), open items `/api/open-items`, e-invoice artifacts, inbound documents `/api/inbound-documents/*`, any reports/Auswertungen endpoints, plus `/api/me` + `/api/auth`. DENY (403) reads of CRM master data + config: `/api/partners/*` (contacts/notes/activities + the new tasks + files), `/api/catalog/*`, `/api/company-profile`, `/api/dunning/config`, `/api/recurring-templates/*`, `/api/team/*`. (Document detail carries frozen recipient snapshots, so the advisor still sees who a Beleg is for without partner-master read.) Implement as an explicit path allow-list for the TaxAdvisor read role; the planner defines the exact list.
   - A narrow **`RequireOwner`** policy gates team-management endpoints (Employee cannot manage the team either; only Owner invites/assigns roles).

2. **Team features gated on M+ (USER decision).** Inviting members and assigning roles (incl. a Steuerberater) require `Capability.MultiUser` (already M+). An S-plan tenant is single-seat and must upgrade to M to add anyone. Server-authoritative via `IEntitlementService`; upgrade-hint UX.

3. **Invitations = v1 "direct-add".** Reuse `RegistrationService` (minus org creation): admin-token → find/create the Keycloak user → add org member → insert a `membership` row with the chosen role. Synchronous direct add; NO email-invite accept/sync flow in v1 (deferred).

4. **CRM-05 Kundenakte files = Postgres `bytea`, APPEND-ONLY (USER decision).** A `customer_file` child of BusinessPartner (bytea + content-type + size + filename), per-table hand-written RLS, mirroring the `inbound_document` bytea pattern + the CompanyProfile `/logo` upload idiom (IFormFile, type/size guard, `.DisableAntiforgery()`). **No delete** — an append-only immutability trigger (like `payment`/audit) blocks UPDATE/DELETE; endpoints are upload + download + list only. NO external object storage.

5. **CRM-04 tasks = entity + query (no jobs).** A `PartnerTask` child of BusinessPartner (title, description, due date, status Open/Done, optional assignee) with per-table RLS. API: CRUD + a due/overdue list (mirror `OpenItemEndpoints` overdueOnly: `DueDate < today && Status==Open`). Email reminders are DEFERRED (Hangfire is available for a later enhancement).

## Claude's Discretion (freedom areas)
- Exact schema/column names, the precise TaxAdvisor read allow-list paths, plan/wave split, contract shapes, frontend details, which tests to extend.
- Recommended slicing (research): (1) role-enforcement foundation FIRST, (2) team invitation API+UI, (3) CRM-04 tasks ‖ (4) CRM-05 files. Both new migrations chain on `20260728145309_RecurringInvoices`.

## Deferred Ideas (OUT of scope for Phase 8)
- Email-invite accept/self-signup flow (v1 is direct-add).
- Email/push reminders for tasks (v1 is a due-date query/list).
- External object storage for files (v1 is Postgres bytea).
- Deleting/editing Kundenakte files (append-only).
