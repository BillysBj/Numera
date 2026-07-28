# Phase 8: CRM-Ausbau & Steuerberater-Zugang - Research

**Researched:** 2026-07-28
**Domain:** Multi-tenant authorization (read-only role enforcement), CRM child entities (tasks + file attachments), Keycloak Organizations invitations
**Confidence:** HIGH (grounded in existing code; only the Keycloak invitation endpoint is version-sensitive — MEDIUM)

> No CONTEXT.md exists for this phase (`has_context: false`). There are no locked user decisions; every recommendation below is Claude's discretion and is flagged where a human decision is genuinely required (see **Open Questions**).

## Summary

Phase 8 adds three features onto a mature, disciplined stack: partner tasks (CRM-04), partner file attachments / Kundenakte (CRM-05), and team invitations with role enforcement — most critically a **read-only Steuerberater (TaxAdvisor) role** (PLAT-03). The CRM pieces are low-risk: they are new child entities of `BusinessPartner` and follow an already-proven pattern (`ITenantEntity` + hand-written per-table RLS in a migration + a `/api/partners/{id}/…` sub-resource group + a React feature). The file storage decision is effectively pre-made by the codebase: binaries already live in Postgres `bytea` (company logo, rendered PDFs, e-invoice XML, inbound originals), and `bytea` inherits tenant isolation from RLS for free. Use `bytea`.

The hard, novel problem is PLAT-03 enforcement. Two facts drive the design. First, the `MembershipRole` enum (Owner=1, Employee=2, TaxAdvisor=3) and the `membership` table already exist and are already **app-authoritative** — `MeEndpoints` and `EntitlementService` both read role/plan straight from the DB, not from a Keycloak token claim. Second, **RLS does not help here at all**: RLS isolates tenants, but a TaxAdvisor is a legitimate member of the tenant, so RLS will happily let them INSERT/UPDATE/DELETE their own tenant's rows. Intra-tenant authorization is a 100% application-level concern and must be added deliberately. Today every endpoint is a Minimal API group with a bare `.RequireAuthorization()` (authenticated-only); there is no role policy anywhere in the codebase yet.

**Primary recommendation:** Enforce the read-only role with a **default-deny-writes global middleware** (a "write guard" that, for a TaxAdvisor, rejects every unsafe HTTP method — POST/PUT/PATCH/DELETE — with 403, with a tiny allow-list for `/api/auth/logout`). This is server-authoritative and, crucially, **automatically covers every future endpoint** without anyone remembering to annotate it. Layer a second, narrow `RequireOwner` authorization policy on the new team-management endpoints (only the Inhaber may invite members / change roles). Resolve the current user's role once per request via a new scoped `ICurrentUserRole` seam that reads `membership` (RLS-scoped, memoised — exactly mirroring `EntitlementService`). Gate the whole team-management feature behind the existing `Capability.MultiUser` (M-plan and up).

## Standard Stack

Everything needed already exists in the repo. No new NuGet or npm packages are required.

### Core (already in the solution)
| Library / Component | Where | Purpose in Phase 8 |
|---------|-------|--------------|
| ASP.NET Core Minimal APIs (.NET 10) | `src/Numera.Api/Endpoints/*` | New endpoint groups (tasks, files, team) |
| EF Core 10 + Npgsql | `NumeraDbContext` | New `ITenantEntity` types, reflectively discovered |
| Postgres 18 RLS | hand-written in migrations | Tenant isolation for new tables (NOT role auth) |
| FluentValidation | `src/Numera.Api/Validators/*` | Request validation, auto-registered from the Api assembly |
| `Microsoft.FeatureManagement` + `PlanFeatureFilter` | `Numera.Platform.Entitlements` | Gate team features on `Capability.MultiUser` |
| Keycloak Admin API (via `IHttpClientFactory`) | `RegistrationService` | Create/find user + add org member on invite |
| Hangfire (Postgres-backed) | `Program.cs` | ONLY if email reminders are chosen (deferred — see below) |
| React 19 + Vite + Tailwind + shadcn + TanStack Query + RHF/zod + i18next | `web/src/features/*` | Team-management, tasks, and file UIs |

### Supporting patterns (copy, don't invent)
| Pattern | Reference file | Use for |
|---------|---------|-------------|
| Partner child sub-resource CRUD | `PartnerEndpoints.cs` (`MapNotes`, `MapContacts`) | Tasks + files endpoints under `/api/partners/{id}/…` |
| `bytea` upload/download w/ size+type guard | `CompanyProfileEndpoints.cs` (`/logo`), `InboundDocumentEndpoints.cs` | Kundenakte file upload/download |
| Due-date / overdue list query | `OpenItemEndpoints.cs` (`overdueOnly`) | "offene/fällige Aufgaben" list |
| Scoped, memoised DB-read of tenant state | `EntitlementService.cs` | New `ICurrentUserRole` seam |
| Keycloak user+org bootstrap | `RegistrationService.cs` | Member invitation flow |
| Per-request tenant push | `TenantResolutionMiddleware.cs` | Sibling middleware for the write guard |

### Alternatives Considered (enforcement mechanism)
| Instead of (recommended) | Could Use | Tradeoff |
|------------|-----------|----------|
| Global default-deny-writes middleware | Per-endpoint role checks in each handler | Rejected: easy to forget on a new endpoint → silent authz hole. Not "hard to forget." |
| Global default-deny-writes middleware | A `RequireAuthorization("NotReadOnly")` policy applied to every mutating group | Works, but must be manually added to each `MapGroup` — same forget-risk. Good as *defence in depth*, not the primary control. |
| Role from DB (`ICurrentUserRole`) | Role as a Keycloak token claim (org-role mapper) | Rejected for v1: the codebase is already DB-authoritative for role (`MeEndpoints`, `EntitlementService`); a token claim adds a second source of truth and a re-login-on-role-change problem. DB read is cheap and memoised. |

## Architecture Patterns

### Where new code lands
```
src/modules/Numera.Modules.Crm/
├── PartnerTask.cs            # NEW — CRM-04 entity (ITenantEntity, child of BusinessPartner)
├── CustomerFile.cs           # NEW — CRM-05 entity (ITenantEntity, bytea, child of BusinessPartner)
└── PartnerEnums.cs           # extend: PartnerTaskStatus, maybe PartnerActivityType.TaskCreated/TaskCompleted

src/platform/Numera.Platform.Tenancy/
└── ICurrentUserRole.cs       # NEW — per-request role seam (nullable, like ICurrentUser)

src/Numera.Api/Auth/
├── CurrentUserRole.cs        # NEW — reads membership.role, RLS-scoped, memoised
└── ReadOnlyWriteGuardMiddleware.cs  # NEW — default-deny unsafe methods for TaxAdvisor

src/Numera.Api/Endpoints/
├── PartnerEndpoints.cs       # extend: MapTasks(g), MapFiles(g)  (or new files)
└── TeamEndpoints.cs          # NEW — /api/team members list + invite + role change (Owner-only)

src/Numera.Api/Services/
└── InvitationService.cs      # NEW — Keycloak find/create user + add org member + membership row

src/platform/Numera.Platform.Db/Migrations/
└── XXXX_CrmTasksFilesAndInvites.cs  # NEW — chains on 20260728145309_RecurringInvoices

web/src/features/
├── partners/  (extend PartnerDetailPage.tsx: tasks tab, files tab)
├── tasks/      (optional cross-partner "meine offenen Aufgaben" list)
└── team/       (NEW — TeamPage: member list, invite form, role selector)
```

### Pattern 1: New tenant-scoped child entity (CRM-04 / CRM-05)
**What:** Add a `sealed class : ITenantEntity` with a client-set UUIDv7 PK, a `TenantId`, a `PartnerId` FK (plain `Guid`, NO navigation), and a `[Table]`/`[Index(nameof(TenantId), nameof(PartnerId))]` attribute. It is discovered reflectively by `NumeraDbContext.RegisterModuleTenantEntities` — **no DbSet and no DbContext edit needed** (mirrors `InboundDocument`).
**Critical:** Persist with `db.Add(entity)`, never via a collection navigation. The `InboundDocument` docstring is explicit: a client-set UUIDv7 PK added through a collection nav is tracked `Modified` → a 0-row UPDATE.

```csharp
// Source: existing PartnerNote.cs / InboundDocument.cs pattern
[Table("partner_tasks")]
[Index(nameof(TenantId), nameof(PartnerId), nameof(DueDate))]
public sealed class PartnerTask : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid PartnerId { get; init; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }                 // Fälligkeit
    public DateTimeOffset? RemindAt { get; set; }          // Erinnerung (query-driven in v1)
    public PartnerTaskStatus Status { get; set; } = PartnerTaskStatus.Open;
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
```

### Pattern 2: Hand-written RLS in the migration (MANDATORY, non-negotiable)
**What:** Reflective discovery gives the tenant query filter + index, but **never** creates the DB RLS policy. Every new table needs `ENABLE` + `FORCE` + a `tenant_isolation` policy, hand-written via `migrationBuilder.Sql`, exactly like the `_Crm` migration.
```csharp
// Source: Migrations/20260711022645_Crm.cs (lines 146-154)
foreach (var t in new[] { "partner_tasks", "customer_files" })
{
    migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
    migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
    migrationBuilder.Sql(
        $"CREATE POLICY tenant_isolation ON {t} " +
        "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
        "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
}
```
Also update the canonical reference `src/platform/Numera.Platform.Db/Sql/rls_policies.sql` (it documents intent and is diffed). Migration chains on the latest snapshot **`20260728145309_RecurringInvoices`**. Build/scaffold with the user-local .NET 10 SDK (`C:\Users\Admin\AppData\Local\Microsoft\dotnet`), NOT the PATH default (.NET 8).

### Pattern 3: Per-request role seam (the enforcement foundation)
**What:** A new scoped service resolving the caller's `MembershipRole` from the `membership` table, memoised for the scope, deny-safe when absent — a structural clone of `EntitlementService`.
```csharp
// New: src/platform/Numera.Platform.Tenancy/ICurrentUserRole.cs
public interface ICurrentUserRole { Task<MembershipRole?> GetAsync(CancellationToken ct = default); }

// New: src/Numera.Api/Auth/CurrentUserRole.cs — reads membership (RLS self-scopes to tenant),
// filters by ICurrentUser.UserId, memoises. Registered AddScoped in Program.cs.
// NOTE: MembershipRole lives in Numera.Platform.Db.Entities; if the seam interface must
//       live in Tenancy, either move the enum or return an int/bool IsReadOnly — see Open Q5.
```
The role read is the SAME query `MeEndpoints` already runs (`Memberships.Where(m => m.TenantId == … && m.UserId == sub).Select(m => m.Role)`).

### Pattern 4: Default-deny-writes middleware (the primary PLAT-03 control)
**What:** A terminal-ish middleware registered **after** `TenantResolutionMiddleware` (so tenant + principal are set) that, for an authenticated TaxAdvisor, blocks unsafe HTTP methods.
```csharp
// New: ReadOnlyWriteGuardMiddleware — pseudo
var method = context.Request.Method;
var isUnsafe = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
if (isUnsafe && context.User.Identity is { IsAuthenticated: true })
{
    var role = await currentUserRole.GetAsync(ct);
    var path = context.Request.Path;
    var allowListed = path.StartsWithSegments("/api/auth"); // logout/login
    if (role == MembershipRole.TaxAdvisor && !allowListed)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "read_only_role" });
        return;
    }
}
await next(context);
```
**Why this shape:** all current mutations are POST/PUT/PATCH/DELETE; all current reads/exports/PDF-downloads are GET (`PartnerEndpoints`, `OpenItemEndpoints`, `CompanyProfileEndpoints /logo`, inbound original download). So "read-only = GET-only" holds today and is the natural contract to keep going forward. The middleware needs zero per-endpoint wiring — a new mutating endpoint is denied to TaxAdvisor the moment it is added. Pair it with an integration test (see Pitfalls) that asserts a TaxAdvisor gets 403 on a representative POST/PUT/DELETE.

### Pattern 5: Owner-only team management (second, narrow layer)
Team endpoints (invite, change role, remove member) must be **Owner-only** — an Employee is not read-only but also must not manage the team. Add a real authorization policy:
```csharp
// Program.cs
builder.Services.AddAuthorization(o =>
    o.AddPolicy("RequireOwner", p => p.RequireAssertion(_ => true))); // real check via requirement/handler reading ICurrentUserRole
// TeamEndpoints:
app.MapGroup("/api/team").RequireAuthorization("RequireOwner");
```
Because role is DB-resolved (not a claim), implement `RequireOwner` as an `IAuthorizationHandler` + `IAuthorizationRequirement` that awaits `ICurrentUserRole`, OR (simpler) do an explicit `role == Owner` check at the top of each team handler returning 403. The write-guard middleware already blocks TaxAdvisor; `RequireOwner` additionally blocks Employee.

### Pattern 6: Invitation flow (Keycloak + membership mirror)
Follow `RegistrationService` almost verbatim, minus org creation. Two viable shapes:
- **6a — Admin-adds-user (RECOMMENDED for v1, synchronous, no webhook):** Owner submits `{ email, role }`. Service gets an admin token → finds or creates the Keycloak user (reuse `CreateUserAsync`; on 409/exists, look up by email) → `POST /organizations/{orgId}/members` (reuse `AddMemberAsync`) → insert a `Membership { TenantId, UserId, Role }` row. Optionally trigger a Keycloak "set password" / verify-email action email. Membership is in sync immediately; `MeEndpoints`/role-guard work on the invitee's first login with no extra plumbing.
- **6b — Email invite (nicer UX, more moving parts):** `POST /admin/realms/{realm}/organizations/{org-id}/members/invite-user` sends an invitation (existing user) or registration link (new user). BUT this endpoint does **not** carry the app-level role, and the `membership` row must be created either at invite time (as a `Pending` row) or synced on first login (a Keycloak event webhook, or a "mirror membership if missing" step added to `TenantResolutionMiddleware`). More robust UX, materially more work. **Recommend deferring 6b; ship 6a.**

`invite-user` endpoint confirmed present in Keycloak 26.x Organizations API (MEDIUM confidence — verify against the running Keycloak version).

### Anti-Patterns to Avoid
- **Relying on RLS for role authorization.** RLS scopes by tenant only. A TaxAdvisor writing their own tenant's data passes every RLS `WITH CHECK`. Role enforcement is 100% app-level.
- **Client-side-only gating.** Hiding buttons/nav for a role in React is cosmetic (like `useEntitlements`). The 403 must come from the server.
- **Adding a DbSet / editing `NumeraDbContext` for the new entities.** They are discovered reflectively; adding a DbSet is unnecessary noise (only `Tenant`/`Membership` are explicit).
- **Collection-navigation `.Add` for UUIDv7 children.** Use `db.Add(child)` (0-row-update trap — see `InboundDocument` remark).
- **Combining query filters with `&&`.** Named filters only (`TenantFilter`, `NotArchivedFilter`).
- **Encoding role in a token claim as the source of truth** while the rest of the app reads it from the DB — two sources of truth diverge on role change.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tenant isolation on new tables | A custom WHERE-tenant filter | The existing RLS `ENABLE+FORCE+tenant_isolation` migration idiom + reflective query filter | Already the house standard; RLS is the primary control |
| File storage | S3/MinIO integration + infra | Postgres `bytea` (`CustomerFile.Bytes`) | Zero new infra; RLS gives tenant isolation for free; matches logo/PDF/inbound |
| Upload validation | Ad-hoc parsing | Copy the `IFormFile` size + content-type guard from `CompanyProfileEndpoints`/`InboundDocumentEndpoints` + `.DisableAntiforgery()` | Proven, handles the BFF multipart POST |
| Role resolution per request | Re-query in every handler | One scoped memoised `ICurrentUserRole` (clone `EntitlementService`) | Single DB read per request; single source of truth |
| Per-endpoint write authz | Annotate each mutating route | One global write-guard middleware | Impossible to forget on future endpoints |
| Due/overdue task list | New scheduler | A `DueDate < today && Status==Open` query (copy `OpenItemEndpoints.overdueOnly`) | Simplest thing that satisfies "anlegen und verfolgen" |
| Member/user creation | New Keycloak client | Reuse `RegistrationService`'s `GetAdminTokenAsync`/`CreateUserAsync`/`AddMemberAsync` | Already handles admin token, 409, Location→GUID |

**Key insight:** Phase 8 is almost entirely *composition of existing patterns*. The only genuinely new architectural element is the read-only role enforcement layer.

## Common Pitfalls

### Pitfall 1: Assuming RLS protects against the Steuerberater
**What goes wrong:** Team ships thinking "tenant isolation is handled," and a TaxAdvisor can quietly POST/PUT/DELETE their own tenant's invoices, partners, etc.
**Why:** RLS `USING/WITH CHECK` only compares `tenant_id` to the GUC; a TaxAdvisor's tenant matches. Intra-tenant role is invisible to RLS.
**How to avoid:** The write-guard middleware + an integration test proving `TaxAdvisor → POST/PUT/DELETE = 403` on multiple endpoints (partners, tasks, files, documents). This test is the success-criteria gate for SC #4.
**Warning sign:** No test in `tests/Numera.IntegrationTests` named like `*ReadOnlyRole*` / `TaxAdvisor*`.

### Pitfall 2: Forgetting the hand-written RLS policy on a new table
**What goes wrong:** `partner_tasks` / `customer_files` created without `ENABLE+FORCE+policy` → cross-tenant leak.
**Why:** Reflective discovery creates the index + query filter but never a DB policy.
**How to avoid:** Add the `migrationBuilder.Sql` block (Pattern 2) + extend `StammdatenRlsTests` with per-table isolation + `WITH CHECK` rejection assertions (the suite already does exactly this for the 4 CRM tables).
**Warning sign:** New table absent from `rls_policies.sql`.

### Pitfall 3: Role change / logout staleness
**What goes wrong:** Owner demotes a member to TaxAdvisor, but the member's in-flight session still writes.
**Why:** Role is read from the DB per request (good) — but if it were cached in a claim it would be stale until re-login.
**How to avoid:** This is a *reason to keep role DB-resolved*, not claim-based. `ICurrentUserRole` reads live `membership` each request (memoised only within a single request scope), so a role change takes effect on the very next request. Document this as an intentional property.

### Pitfall 4: `bytea` memory + size
**What goes wrong:** Large uploads buffered fully in memory (`MemoryStream` → `ToArray()`), or a download of a huge file.
**Why:** The existing pattern reads the whole file into a `byte[]`.
**How to avoid:** Enforce a size cap on upload (inbound uses 15 MB; pick a Kundenakte cap, e.g. 10–20 MB) and a content-type allow-list. Denormalize `ByteSize` for listings so the list query never `SELECT`s the bytes (mirror `InboundDocument.ByteSize`; project only metadata columns in the list). Stream download via `Results.File(bytes, contentType)`.

### Pitfall 5: MultiUser capability blocks single-seat tenants from ANY team member — including the Steuerberater
**What goes wrong:** An S-plan tenant cannot invite anyone (MultiUser is M+), so they cannot grant their Steuerberater access at all.
**Why:** `Capability.MultiUser` is granted only from Plan M upward (`PlanCapabilityMap`).
**How to avoid:** This is a **product decision** (Open Q4), not a bug. Either (a) accept it — team features (incl. TaxAdvisor) require M+, or (b) carve out a separate capability so Steuerberater read-access is available on lower tiers. Flag before planning; it changes the gating.

### Pitfall 6: Antiforgery on multipart POST
**What goes wrong:** File upload POST returns 400 antiforgery.
**Why:** Minimal API multipart form binding enforces antiforgery by default.
**How to avoid:** `.DisableAntiforgery()` on the upload endpoint (as `CompanyProfile /logo` and inbound upload already do). Cookie-SameSite + same-origin BFF is the CSRF posture.

## Code Examples

### Kundenakte file entity (CRM-05) — bytea, RLS-free isolation
```csharp
// Source: mirrors InboundDocument.cs (bytea + ByteSize) + PartnerNote.cs (partner child)
[Table("customer_files")]
[Index(nameof(TenantId), nameof(PartnerId), nameof(UploadedAt))]
public sealed class CustomerFile : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid PartnerId { get; init; }
    public required byte[] Bytes { get; init; }          // bytea
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long ByteSize { get; init; }                  // denormalized for the list (never SELECT bytes in list)
    public Guid? UploadedByUserId { get; init; }
    public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

### File upload endpoint (CRM-05)
```csharp
// Source: CompanyProfileEndpoints.cs /logo + InboundDocumentEndpoints.cs upload
g.MapPost("/{id:guid}/files", async (Guid id, IFormFile file, NumeraDbContext db,
        ICurrentTenant tenant, ICurrentUser user, CancellationToken ct) =>
{
    if (file.Length <= 0 || file.Length > MaxFileBytes)  return Results.ValidationProblem(/* size */);
    var ct2 = file.ContentType?.Trim().ToLowerInvariant() ?? "";
    if (Array.IndexOf(AllowedFileTypes, ct2) < 0)         return Results.ValidationProblem(/* type */);
    if (!await PartnerExistsAsync(db, id, ct))            return Results.NotFound();
    using var ms = new MemoryStream(); await file.CopyToAsync(ms, ct);
    var bytes = ms.ToArray();
    db.Add(new CustomerFile { TenantId = tenant.TenantId!.Value, PartnerId = id,
        Bytes = bytes, FileName = file.FileName, ContentType = ct2,
        ByteSize = bytes.Length, UploadedByUserId = user.UserId });
    await db.SaveChangesAsync(ct);
    return Results.Created(/* … */, new { /* id */ });
}).DisableAntiforgery();
```

### Due/overdue tasks list (CRM-04)
```csharp
// Source: OpenItemEndpoints.cs overdueOnly branch
var today = DateOnly.FromDateTime(DateTime.UtcNow);
var query = db.Set<PartnerTask>().AsNoTracking().Where(t => t.PartnerId == id);
if (openOnly)    query = query.Where(t => t.Status == PartnerTaskStatus.Open);
if (dueOnly)     query = query.Where(t => t.DueDate != null && t.DueDate < today
                                          && t.Status == PartnerTaskStatus.Open);
var items = await query.OrderBy(t => t.DueDate).ThenBy(t => t.Id)
    .Select(t => new TaskListItem(t.Id, t.Title, t.DueDate, t.Status,
        t.DueDate != null && t.DueDate < today && t.Status == PartnerTaskStatus.Open /*overdue*/))
    .ToListAsync(ct);
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Role stored/checked ad-hoc | App-authoritative `membership.role` read per request (already true for `MeEndpoints`/`EntitlementService`) | Keep DB-authoritative; add `ICurrentUserRole` + middleware |
| Files on filesystem/S3 | Postgres `bytea` with RLS (repo-wide) | Use `bytea`; no object store |
| Keycloak "add member" only | Keycloak 26 Organizations `invite-user` email flow exists | Optional UX upgrade; v1 uses direct add |

**Deprecated/outdated:** none relevant. Keycloak Organizations is GA (26.x); the repo already uses `/organizations` Admin endpoints in `RegistrationService`.

## Open Questions

1. **Invitation UX: direct-add vs email-invite?**
   - Known: `RegistrationService` already does admin-create-user + add-member synchronously. Keycloak 26 also has `members/invite-user` (email).
   - Unclear: whether v1 wants a real email invitation + accept flow.
   - Recommendation: ship **6a (direct add + optional set-password email)** for v1; defer email-invite (6b) — it needs a membership-sync-on-accept mechanism (webhook or login-time mirror).

2. **What exactly may the Steuerberater read?** Requirement says "Belege und Auswertungen."
   - Known: all reads are GETs today; the write-guard makes them read-only automatically.
   - Unclear: should TaxAdvisor be *further* restricted from some GETs (e.g. CRM notes/tasks, team management, partner private data)?
   - Recommendation: v1 = **all GET allowed, all writes denied** (simplest, satisfies "read Belege/Auswertungen"); optionally hide `/api/team` reads from non-Owners. Flag if legal/product wants a narrower read surface.

3. **Are Kundenakte files GoBD records (immutable, no delete)?**
   - Known: notes/contacts are hard-deletable (GoBD-irrelevant); inbound originals are immutable (GoBD).
   - Unclear: whether uploaded customer files are "Belege" requiring retention.
   - Recommendation: default to **hard-delete allowed** (like notes), but flag — if any file could be a Beleg, deletion should be blocked/soft-deleted and retention documented.

4. **Does Steuerberater/team access require the MultiUser (M+) plan?** (See Pitfall 5.)
   - Recommendation: decide before planning. Default: yes, gate `/api/team` on `Capability.MultiUser`. If Steuerberater-access must be available on S, add a distinct capability.

5. **Enum placement for `ICurrentUserRole`.** `MembershipRole` lives in `Numera.Platform.Db.Entities`; `Numera.Platform.Tenancy` (where `ICurrentUser` lives) must not depend on `Db`.
   - Recommendation: either return a role-agnostic `bool IsReadOnly` / `int` from the Tenancy seam, or place `ICurrentUserRole` in a project that references `Db`. Minor; resolve in the plan.

6. **Task reminders: query vs job?**
   - Recommendation: **query-driven due/overdue list only** for v1 (no Hangfire). Email reminders are a clean deferred enhancement (Hangfire already wired) — do NOT build for v1 unless product insists.

## Cross-Cutting: Ordering, Dependencies, Parallelization

**Recommended plan slicing (3–5 plans):**
1. **PLAT-03 enforcement foundation (do FIRST; everything else depends on it being safe):** `ICurrentUserRole` seam + `CurrentUserRole` + `ReadOnlyWriteGuardMiddleware` wired after `TenantResolutionMiddleware` in `Program.cs` + `RequireOwner` policy + integration tests (TaxAdvisor 403 on writes; Owner/Employee 200). No schema change.
2. **Team invitation API + UI (PLAT-03 rest):** `InvitationService` (Keycloak add + membership row), `TeamEndpoints` (list/invite/change-role/remove, Owner-only), gated on `Capability.MultiUser`; React `team/` feature + nav link + i18n (DE authoritative). Depends on #1.
3. **CRM-04 tasks:** `PartnerTask` entity + migration (RLS) + `MapTasks` endpoints + due/overdue list + React tasks tab on `PartnerDetailPage` (+ optional cross-partner list). Independent of #2.
4. **CRM-05 Kundenakte files:** `CustomerFile` entity + migration (RLS) + upload/download/list/delete endpoints + React files tab. Independent of #2 and #3.

**Parallelization:** #3 and #4 are independent of each other and of #2; they can run in parallel once #1 lands (the write-guard must exist and be tested before new mutating endpoints are trusted). #2 depends on #1. Migrations for #3 and #4 both chain on `20260728145309_RecurringInvoices` — if built in parallel, sequence the two migrations (second rebases on the first) to avoid a branched snapshot.

**Migration note:** `membership` already has `role`; no column change needed for basic invites. If email-invite (6b) or invite metadata (invited_at/status/invited_by) is wanted, that's an added `membership` column — flag with Open Q1.

## Sources

### Primary (HIGH confidence) — existing code, read in full
- `src/platform/Numera.Platform.Db/Entities/Membership.cs` — `MembershipRole` (Owner/Employee/TaxAdvisor), app-authoritative role
- `src/Numera.Api/Auth/{KeycloakBffExtensions,TenantResolutionMiddleware,CurrentUser}.cs` — BFF cookie auth, tenant resolution, `ICurrentUser`
- `src/Numera.Api/Services/RegistrationService.cs` — Keycloak admin token / create user / create org / add member
- `src/Numera.Api/Endpoints/{PartnerEndpoints,CompanyProfileEndpoints,InboundDocumentEndpoints,OpenItemEndpoints,MeEndpoints,AuthEndpoints}.cs` — child-CRUD, bytea upload/download, overdue query, role read, `.RequireAuthorization()` groups
- `src/platform/Numera.Platform.Entitlements/*` — `Capability.MultiUser`, `PlanCapabilityMap`, `EntitlementService`, `PlanFeatureFilter`
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs` + `Sql/rls_policies.sql` + `Migrations/20260711022645_Crm.cs` — reflective `ITenantEntity` discovery, named filters, hand-written RLS idiom
- `src/modules/Numera.Modules.Crm/*` + `EInvoice/Inbound/InboundDocument.cs` — partner children + bytea entity pattern
- `tests/Numera.IntegrationTests/StammdatenRlsTests.cs` — per-table RLS test pattern to extend
- `web/src/{App.tsx,lib/api.ts,lib/entitlements.ts,features/*}` — React feature/routing/entitlement-hook pattern
- `Program.cs` — middleware order, DI, feature management registration

### Secondary (MEDIUM confidence) — verify against running Keycloak version
- Keycloak 26.x Organizations Admin API `POST /organizations/{org-id}/members/invite-user` (invite existing user OR registration link) — https://docs.redhat.com/en/documentation/red_hat_build_of_keycloak/26.0/html/server_administration_guide/managing_organizations and Keycloak docs-api `OrganizationInvitationResource` (26.1.5)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — everything already in the repo, patterns read directly
- Architecture (enforcement + CRM entities): HIGH — grounded in existing code; the write-guard design is new but simple and testable
- Keycloak invitation endpoint: MEDIUM — version-sensitive; direct-add (6a) avoids the dependency entirely
- Pitfalls: HIGH — derived from explicit code comments (RLS scope, 0-row-update, antiforgery, capability map)

**Research date:** 2026-07-28
**Valid until:** ~2026-08-27 (stable; re-verify only the Keycloak invite endpoint if the Keycloak version changes)

## RESEARCH COMPLETE
