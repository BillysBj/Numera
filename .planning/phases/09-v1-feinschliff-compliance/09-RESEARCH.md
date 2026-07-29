# Phase 9: v1-Feinschliff & Compliance - Research

**Researched:** 2026-07-29
**Domain:** DSGVO data-portability export, PWA install/offline hardening, tarif-gate UX consistency, GoBD conformance pass (docs + wording + cross-tenant RLS proof)
**Confidence:** HIGH (code-grounded); MEDIUM on the two legal/compliance framings (verified against current German sources)

## Summary

Phase 9 is the launch-readiness/compliance capstone. Four of its five work-streams are **cross-cutting polish + audit artefacts** over an already-mature .NET 10 / React 19 modular monolith; only **one is a genuinely new subsystem — the DSGVO tenant-data export (PLAT-08)**. The good news from reading the code: the hard scaffolding already exists. `Capability.DataExport` is defined and granted at every tier (S+); the PWA service worker is already configured with the correct financial-data-safe posture (`NetworkOnly` for `/api`, precache app-shell only); the tarif-gate mechanism (`IEntitlementService` → `PlanCapabilityMap` → `PlanFeatureFilter`) is production-grade and the frontend `UpgradeHint` + `useEntitlements` pattern is established; every tenant table already carries hand-written RLS (ENABLE + FORCE + `tenant_isolation` policy). Phase 9 is therefore about **closing specific, enumerable gaps**, not building new foundations.

The export subsystem is the main build. Data spans four modules and ~25 RLS-scoped tables plus five bytea blob columns. The right shape is a **ZIP archive of per-entity JSON files + the binary files**, produced by iterating each RLS-scoped table under the current tenant's context — reusing the exact `ICurrentTenant.SetTenant` job pattern proven by `RenderDocumentPdfJob`. Everything the planner needs to build it prescriptively (table list, blob list, archive layout, gating, delivery, tests) is below. The other three streams are audits with concrete, code-located checklists: a **tarif-gate audit** (two capabilities — Dunning and EInvoicing — are neither server-gated nor FE-hinted; plus no current-plan indicator exists), a **cross-tenant RLS test gap list** (eight phase-6→8 tables have functional tests but no dedicated isolation test), and a **GoBD Verfahrensdokumentation draft + wording audit** (the codebase is already clean of "zertifiziert" claims — verified).

**Primary recommendation:** Build the DSGVO export as a **synchronous, streamed `ZipArchive` over the HTTP response** (Owner-only, `DataExport`-gated, audit-logged) for v1 simplicity; close the two tarif-gate gaps by server-gating Dunning + EInvoicing and adding `UpgradeHint`s; add a consolidating cross-tenant RLS test covering the eight uncovered tables; and author `docs/gobd-verfahrensdokumentation.md` as a versioned draft. Flag the sync-vs-async and Owner-only decisions to the user.

## User Constraints (from CONTEXT.md)

> **No CONTEXT.md exists for Phase 9** (`/gsd:discuss-phase` has not run). The constraints below are extracted from the phase brief / roadmap success criteria and the house rules, not from a discussion artefact. **The planner should treat the "Open Questions / Decisions" section as requiring user confirmation** — several are genuine product decisions (export delivery, Owner-only, whether to add Dunning/EInvoicing server gating).

### Locked constraints (from the phase brief & success criteria)
- **Finalisierung bleibt online-gebunden** — offline scope MUST NOT include invoice finalize, e-invoice generation, or send. These already require the server; do not build offline mutation/sync.
- **No "zertifiziert"/"certified" claim** — "GoBD-konform" phrasing is acceptable; a certification claim is not. (Codebase is currently clean — see Wording Audit.)
- **TaxAdvisor (read-only Steuerberater) export** — the 08-01 global write-guard + read allow-list default-denies any new `/api/export` path. Keep it denied unless the user explicitly decides otherwise (see Decision D3).
- **House rules (non-negotiable):** every tenant table has hand-written RLS (ENABLE+FORCE+policy) per migration; `ITenantEntity`; UUIDv7 for `db.Add`; build with the user-local .NET 10 SDK at `C:\Users\Admin\AppData\Local\Microsoft\dotnet` (PATH default is the wrong .NET 8); `TreatWarningsAsErrors=true` (0 warnings); Testcontainers `postgres:18`; frontend Vite/Tailwind/shadcn/TanStack/RHF+zod/i18next with **German authoritative** + numeric enum mirrors.

### Claude's Discretion (recommendations made below, pending user confirmation)
- Export format details (JSON-only vs JSON+CSV), archive folder layout, sync vs async delivery.
- Which capabilities get server gating added in this phase (Dunning, EInvoicing).
- Whether the consolidating cross-tenant test is one new file or per-table additions.

### Deferred Ideas (OUT OF SCOPE for v1)
- Programmatic public REST API (`Capability.ApiAccess`, XL) — no API exists yet; do not build it or its gate UI here.
- Offline write/mutation + background sync (explicitly excluded by "Finalisierung bleibt online-gebunden").
- DATEV-format export (the `Capability.DataExport` doc-comment mentions "CSV/DATEV/etc." aspirationally; DATEV export is not a Phase-9 success criterion).
- Actual billing/Stripe/plan-switch payment flow (entitlements are a projection of `tenants.plan`; no payment logic in v1).

---

## The Four Work-Streams (grounded specifications)

### 1. DSGVO Tenant-Data Export (PLAT-08) — the one new subsystem

**Legal framing (verified, MEDIUM–HIGH):** DSGVO Art. 20 requires personal data in a *"strukturiert, gängig und maschinenlesbar"* format; **JSON, CSV, and XML all satisfy this** — the law deliberately leaves the exact format open ("Stand der Technik"). A ZIP of per-entity JSON + the original binary files is squarely conformant and is the common SaaS pattern. The success criterion here is broader than Art. 20 ("**alle** Daten seines Mandanten") — export the whole tenant dataset, which is a superset of the personal-data subset, so it also satisfies Art. 20.

**Exact scope — RLS-scoped tables to include** (all confirmed present with tenant RLS in migrations):

| Module | Tables |
|--------|--------|
| Platform | `tenants` (own row), `membership`, `audit_events` |
| Catalog | `catalog_items` |
| Crm | `partners` (incl. owned company/address value objects), `partner_contacts`, `partner_notes`, `partner_activities`, `partner_tasks`, `customer_files` (bytea) |
| Ledger | `accounts`, `journal_entries`, `postings` |
| Sales | `company_profile` (bytea logo), `document_number_formats`, `number_sequences`, `sales_documents`, `sales_document_lines`, `sales_document_tax_breakdown`, `sales_document_prepayment`, `open_items`, `payment`, `payment_allocation`, `dunning_level_config`, `dunning_notice`, `recurring_invoice_templates`, `recurring_invoice_template_lines`, `document_render` (bytea PDF), `document_email`, `document_einvoice` (bytea XML), `inbound_document` (bytea original) |

**Binary blobs (bytea) — export as real files, not base64-in-JSON:**
- `document_render` → rendered §14 PDFs
- `document_einvoice` → e-invoice XML (XRechnung/ZUGFeRD)
- `inbound_document` → received original bytes (GoBD-immutable)
- `customer_files` → Kundenakte uploads
- `company_profile` → logo

**Recommended archive layout:**
```
numera-export-{tenantSlug}-{yyyyMMdd}.zip
├── manifest.json                 # export date, tenant id/name, schema version, table row counts, tool version
├── data/
│   ├── partners.json
│   ├── sales_documents.json
│   ├── payments.json
│   └── … one JSON file per table (array of rows)
├── files/
│   ├── renders/{documentNumber}.pdf
│   ├── einvoice/{documentNumber}.xml
│   ├── inbound/{id}-{originalName}
│   ├── customer-files/{partnerId}/{fileName}
│   └── company/logo.{ext}
└── README.txt                    # plain-language: what this is, DSGVO Art. 20 note, no "zertifiziert" claim
```

**Delivery — RECOMMENDED: synchronous streamed ZIP (flag as Decision D1).**
- Endpoint: `GET /api/export` → returns `application/zip` with `Content-Disposition: attachment`.
- Write a `ZipArchive` **directly onto the response body stream** (`Response.BodyWriter.AsStream()` / `HttpContext.Response.Body`), adding one entry per table (serialize rows with `System.Text.Json` via `JsonSerializer.SerializeAsync` into the entry stream) and one entry per blob (copy bytea straight into the entry stream). This keeps peak memory **bounded** — never materialize the whole archive in memory.
- Rationale: v1 tenants are small; a sync stream needs **no new table/migration/RLS/polling UI**, so it is dramatically less surface than an async job. The `NetworkOnly` PWA rule already guarantees this GET is never cached.
- **Async alternative (documented upgrade path, not v1):** `POST /api/export` enqueues a Hangfire job (reuse the `RenderDocumentPdfJob` `SetTenant` pattern), which builds the archive into a new RLS-scoped `export_archive` bytea table, then `GET /api/export/{id}/download`. Choose this only if the user expects large tenants — it costs a migration + RLS + status/poll UI.

**Gating & authorization:**
- Gate on `Capability.DataExport` via the existing pattern: `await entitlements.HasCapabilityAsync(Capability.DataExport, ct)` → 403 if absent (it is granted S+, so effectively always on, but the gate must exist for consistency).
- **Owner-only (RECOMMENDED — Decision D2):** the full-tenant dump contains every customer's personal data + all financials. Restrict to `MembershipRole.Owner`. Check the caller's role via `ICurrentUserRole` (the same service the write-guard uses).
- **TaxAdvisor stays denied (Decision D3):** `ReadOnlyAccessPolicy.ReadPrefixes` does NOT include `/api/export`, so the write-guard default-denies it for `TaxAdvisor` even though it's a GET. This is the correct default (an external advisor should not pull the whole data-portability dump). Note the `MembershipRole.TaxAdvisor` doc-comment says "read/export focused" — if the user wants advisors to export, that's a deliberate allow-list change; default = deny.
- **Export MUST NOT mutate** (GoBD/DSGVO: a read). Use `AsNoTracking()`, open no write transaction. **Do** write a single `audit_events` row recording the export action (who/when) — this is an *append to the audit log*, not a mutation of business data, and is good compliance hygiene. (Confirm this does not itself trip the read-only guard: the export runs as Owner, not TaxAdvisor, so the guard is a no-op.)

**Testing (Testcontainers `postgres:18`, `PostgresFixture`):**
- Seed a tenant with rows in every table + at least one of each blob type; export; assert the ZIP contains a JSON entry per table with the expected row counts and a file per blob with byte-exact content.
- **RLS proof:** seed a *second* tenant with its own rows/blobs; run the export as tenant A; assert **zero** tenant-B rows/bytes appear anywhere in the archive (this doubles as cross-tenant evidence for criterion 4).
- **Non-mutation:** snapshot row counts/updated-at before and after export; assert unchanged (except the one new audit row).

### 2. PWA Install + Offline (Success Criterion 2)

**Current state (already strong — read `web/vite.config.ts`, `web/public/manifest.webmanifest`, `web/src/pages/Dashboard.tsx`):**
- `vite-plugin-pwa` configured: `registerType: 'autoUpdate'`, precache app-shell globs only, `navigateFallbackDenylist: [/^\/api/]`, `NetworkOnly` runtime rules for `/api` GET+POST, `cleanupOutdatedCaches: true`.
- Valid manifest: name, short_name, description, `start_url`/`scope` `/`, `display: standalone`, theme/background colors, three icons (192, 512, maskable-512) present in `web/public/icons/`.
- Offline signalling exists: `common.offline.banner` i18n key (DE: "Offline – Finanzdaten sind nur online verfügbar."), and `Dashboard.tsx` already tracks `navigator.onLine` + `online`/`offline` events and shows a banner.

**Pragmatic v1 scope (RECOMMENDED — do NOT over-scope):**
1. **Reliable installability** — verify the manifest + SW pass a Lighthouse/Chrome "Installability" audit on both desktop and Android (criterion from Phase 1 CR5 re-verified here). Confirm icons load, `start_url` responds, HTTPS/localhost, SW registers.
2. **Offline app-shell** — the SPA boots offline (already precached). Verify hard-refresh while offline renders the shell + the offline banner, not a browser error page.
3. **"Offline-Entwurfsverhalten"** — interpret conservatively: drafts the user is *currently editing in-memory* survive a transient connection drop (the form doesn't crash on a failed `/api` fetch; it shows the offline banner and lets the user keep typing), and already-loaded read views degrade gracefully. **Do NOT** build offline persistence of drafts to IndexedDB or background sync unless the user asks — that contradicts "Finalisierung bleibt online-gebunden" and adds large surface. If any draft persistence is wanted, cap it at localStorage of the in-progress form, online-flush only.
4. **Finalize/e-invoice/send stay online-only** — already true (server-required). Ensure the UI disables/greys those actions with the offline banner rather than throwing.

**Verification:** Chrome DevTools → Application → Manifest (installable), Service Workers (activated), Network → Offline toggle → reload → shell renders; install the app and launch standalone; toggle offline and confirm finalize is blocked with a friendly message, not a stack trace.

**Confidence:** HIGH on current config being correct; MEDIUM on the exact intended meaning of "Offline-Entwurfsverhalten" — flag as Decision D4 (how much offline draft behaviour the user actually wants).

### 3. Tarif-Gate UX Consistency (Success Criterion 3)

**Audit result (from grepping `HasCapabilityAsync`/`Capability.` in `src/**/*.cs` and `UpgradeHint`/`hasCapability` in `web/**/*.tsx`):**

| Capability (min tier) | Server-gated? | Frontend UpgradeHint? | Gap |
|-----------------------|---------------|-----------------------|-----|
| `MultiUser` (M) | ✅ `TeamEndpoints` | ✅ `TeamPage.tsx` | none |
| `DataExport` (S) | ➖ new in this phase | ➖ n/a (always-on S+) | add gate in export endpoint |
| `ForeignCurrencyInvoicing` (L) | ✅ `SalesDocumentEndpoints` | ✅ `DocumentFormPage.tsx` | none |
| `RecurringInvoices` (L) | ✅ `RecurringInvoiceEndpoints` | ✅ `RecurringTemplate*Page.tsx` | none |
| `DownPaymentInvoices` (L) | ✅ `SalesDocumentEndpoints` | ✅ `DocumentFormPage.tsx` | none |
| **`Dunning` (L)** | ❌ **NOT gated** (`DunningEndpoints` has no entitlement check) | ❌ **no hint** (`DunningConfigSettingsPage.tsx`) | **CLOSE** |
| **`EInvoicing` (L)** | ❌ **NOT gated** on real endpoints (`EInvoiceEndpoints`, `InboundDocumentEndpoints`); only the `/api/features/einvoicing-check` *example* uses it | ❌ **no hint** | **CLOSE** |
| `ApiAccess` (XL) | ❌ no public API exists | ❌ n/a | OUT OF SCOPE (deferred) |

**Two concrete gaps to close for "durchgängig":**
1. **Dunning (L+):** add `HasCapabilityAsync(Capability.Dunning)` gating to the dunning endpoints (config + run/notice actions in `DunningEndpoints.cs`) → 403 when absent; add `UpgradeHint` to `DunningConfigSettingsPage.tsx` (and any dunning action buttons) driven by `hasCapability(caps, 'Dunning')`.
2. **EInvoicing (L+):** add `HasCapabilityAsync(Capability.EInvoicing)` gating to `EInvoiceEndpoints.cs` (generate/download) and `InboundDocumentEndpoints.cs` (receive/list) → 403 when absent; add `UpgradeHint` to the inbound + e-invoice UI surfaces.
   - ⚠️ **This is partly a server-gating gap, not just UX** — currently a plan-S tenant can use dunning + e-invoicing. Flag as Decision D5: confirm these should be L+ gated (they are declared L+ in `PlanCapabilityMap`, so gating them is consistent). If the user prefers them ungated, instead *remove* the capability from being "advertised as gated" — but the roadmap treats E-Rechnung as a paid tier feature, so gating is the expected resolution.

**Current-plan indicator (likely missing — verify with the user):**
- `GET /api/me` already returns `tenant.Plan` (S/M/L/XL); `GET /api/me/entitlements` returns the capability set. `Dashboard.tsx` lists enabled features but there is **no explicit "you are on plan X → upgrade to L to unlock Y" indicator**, and no plan display in `settings/`.
- ⚠️ **Note a latent bug to verify:** `Dashboard.tsx` reads `entitlements.data.features` while `web/src/lib/entitlements.ts` defines the response as `{ capabilities }`. This mismatch means the Dashboard feature list may be empty/broken — confirm and fix as part of the plan indicator work.
- **Recommendation:** add a small current-plan badge/indicator (in the app shell header or a `settings/PlanPage`) reading `tenant.plan`, so `UpgradeHint`s have context. Make `UpgradeHint` optionally accept the required tier so copy can say "ab Tarif L" accurately (the component already hard-codes "ab Tarif L" as default — parameterize it since MultiUser is M, not L).

**Pattern to replicate (from `DocumentFormPage.tsx` / `TeamPage.tsx`):**
```tsx
const caps = useEntitlements()
const allowed = hasCapability(caps.data?.capabilities, 'Dunning')
// …
{!allowed && <UpgradeHint requiredTier="L" />}
```

### 4. GoBD Conformance Pass (Success Criterion 4)

Largely a **docs + audit artefact + test-completeness** stream, minimal production code.

**(a) Verfahrensdokumentation DRAFT** — new file `docs/gobd-verfahrensdokumentation.md` (the `docs/` directory does not yet exist; create it). GoBD Verfahrensdokumentation has **four canonical parts** (verified against current German sources; note a **new BMF directive dated 2025-07-14** updated the GoBD, especially for e-invoice structured retention):

1. **Allgemeine Beschreibung** — what Numera is, tenants, scope of records kept.
2. **Anwenderdokumentation** — how a user creates→finalizes→sends an invoice; the "path of a receipt (Belegweg)" from creation through storage to archival.
3. **Technische Systemdokumentation** — architecture: PostgreSQL, RLS multi-tenant isolation, immutable audit log, UUIDv7 keys, Hangfire jobs, e-invoice engine (XRechnung/ZUGFeRD + KoSIT validation), inbound immutability.
4. **Betriebsdokumentation** — backups, retention (10 years / §147 AO), access control (roles: Owner/Employee/TaxAdvisor), change management.

Must-cover Numera specifics (all real, already implemented — cite them): **invoice immutability after finalize** (Storno instead of edit), **race-safe sequential numbering** (partial unique index, `number_sequences`), **append-only audit log** (REVOKE + trigger + RLS), **append-only payment/allocation** (DB trigger raises "append-only (GoBD)"), **inbound original bytes retained immutably** (`inbound_document`), **e-invoice XML retained as the structured original** (matches the 2025-07-14 BMF clarification that the structured XML is the record). Mark the document **"Entwurf / Draft" and versioned** (GoBD requires versioning changes).

**(b) Wording audit — grep results (already clean, HIGH confidence):**
- Search for `zertifiziert|certified|TÜV|geprüft.*GoBD` across `web/src` (i18n + copy), `src/`, marketing/README. **Current hits for "GoBD" are all correct usages** (append-only comments, "immutable received bytes (GoBD)") — **no "zertifiziert"/"certified" claim exists.** The task is to (1) re-run the grep as a verification step, (2) ensure any user-facing copy that mentions compliance says **"GoBD-konform"/"GoBD-compliant"** never "zertifiziert", and (3) add the disclaimer to the export README + Verfahrensdokumentation. Grep commands for the plan:
  ```
  rg -i "zertifiziert|certified|tüv|iso ?27001|gobd" web/src src README* docs
  ```

**(c) Cross-Tenant Security Suite — completeness check (the "nachweislich rechtskonform" proof):**

Existing dedicated RLS-isolation test files and what they cover:

| Test file | Tables proven isolated |
|-----------|------------------------|
| `RlsIsolationTests` | `audit_events` (canonical pattern: read-isolation, WITH CHECK reject, no-tenant fail-closed) |
| `StammdatenRlsTests` | `partners`, `partner_contacts`, `partner_notes`, `partner_activities` |
| `CatalogRlsTests` | `catalog_items` |
| `CompanyProfileRlsTests` | `company_profile` |
| `SalesRlsTests` | `sales_documents`, `sales_document_lines`, `sales_document_tax_breakdown`, `open_items` |
| `DocumentDeliveryRlsTests` | `document_render`, `document_email` |

**Tables with RLS in migrations but NO dedicated cross-tenant isolation test** (phase 6→8 additions — they have *functional* tests but not an isolation/leak proof):
- `payment`, `payment_allocation` (Payments)
- `dunning_level_config`, `dunning_notice` (Dunning)
- `recurring_invoice_templates`, `recurring_invoice_template_lines` (Recurring)
- `sales_document_prepayment` (Abschlag)
- `partner_tasks` (CRM tasks)
- `customer_files` (Kundenakte bytea)
- `document_einvoice` (e-invoice XML bytea)
- `inbound_document` (inbound original bytea)

**Recommendation:** add a single consolidating test file (e.g. `CrossTenantIsolationCompletenessTests.cs`) that, for **each** of the above tables, seeds a row as tenant A and a row as tenant B and asserts (using the canonical `RlsIsolationTests` pattern with `IgnoreQueryFilters()` so RLS alone is under test) that tenant A sees only its own row and a cross-tenant INSERT is rejected by WITH CHECK. This turns "every tenant table has RLS" from *asserted* into *proven* — the auditable evidence criterion 4 demands. The DSGVO-export RLS test (§1) is complementary end-to-end evidence.

---

## Standard Stack

### Core (all already present — no new foundational deps)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.IO.Compression.ZipArchive` | .NET 10 BCL | Build the export ZIP by streaming entries | In-framework, streamable, zero new dependency |
| `System.Text.Json` | .NET 10 BCL | Serialize each table to JSON (`SerializeAsync` into entry stream) | Already the app's serializer; async + streaming |
| Hangfire | (already wired) | *Optional* async export job (only if Decision D1 → async) | `RenderDocumentPdfJob` proves the tenant-scoped job pattern |
| `vite-plugin-pwa` + Workbox | (already in `web/vite.config.ts`) | Installability + offline app-shell + `/api` NetworkOnly | Already configured with correct financial-data posture |
| Testcontainers + `postgres:18` | (already in `PostgresFixture`) | Real-Postgres RLS isolation tests | House rule; RLS can only be proven on real PG |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `CsvHelper` | latest | CSV rendering of tabular data | ONLY if user wants JSON+CSV (Decision D1a). Recommend JSON-only for v1 → avoid the dependency |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Sync streamed ZIP | Async Hangfire job + `export_archive` table | Robust for huge tenants but adds migration+RLS+polling UI; unjustified for small v1 tenants |
| JSON files | CSV / DATEV | CSV needs `CsvHelper` + loses nested structure; DATEV is a separate spec, out of scope |
| One consolidating RLS test | Per-module RLS test files | Consolidating file is less ceremony and reads as a single "nothing leaks" proof |

**Installation:** none required for the recommended path (all BCL / already-installed). CSV path only: `dotnet add package CsvHelper`.

## Architecture Patterns

### Recommended structure (new code)
```
src/Numera.Api/
├── Endpoints/ExportEndpoints.cs        # GET /api/export (Owner-only, DataExport-gated)
├── Services/TenantExportService.cs     # enumerates tables+blobs, streams ZipArchive
docs/
└── gobd-verfahrensdokumentation.md     # versioned draft (4 GoBD parts)
tests/Numera.IntegrationTests/
├── TenantExportTests.cs                # completeness + RLS + non-mutation
└── CrossTenantIsolationCompletenessTests.cs  # 8 uncovered tables
```

### Pattern 1: Tenant-scoped work reusing the proven job/request pattern
**What:** Any code touching tenant data must run with `app.current_tenant` set so RLS applies. In a request this is automatic; in a Hangfire job re-establish it explicitly.
**Example (verbatim pattern from `RenderDocumentPdfJob.cs`):**
```csharp
// Source: src/Numera.Api/Jobs/RenderDocumentPdfJob.cs
using var scope = _scopeFactory.CreateScope();
scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);
// scoped NumeraDbContext now opens its connection with app.current_tenant pushed by the interceptor → RLS applies
```

### Pattern 2: Streamed ZIP export (bounded memory, non-mutating)
```csharp
// GET /api/export handler (sketch — Owner-only, DataExport-gated)
ctx.Response.ContentType = "application/zip";
ctx.Response.Headers.ContentDisposition = "attachment; filename=numera-export.zip";
await using var zipStream = ctx.Response.Body;
using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);
foreach (var (name, rows) in tablesToExport) {           // AsNoTracking IQueryables
    var entry = archive.CreateEntry($"data/{name}.json");
    await using var es = entry.Open();
    await JsonSerializer.SerializeAsync(es, rows, jsonOpts, ct);
}
// blobs: copy bytea straight into files/… entries (never base64 into JSON)
```

### Pattern 3: Canonical cross-tenant RLS test (RLS alone under test)
```csharp
// Source: tests/Numera.IntegrationTests/RlsIsolationTests.cs
await using var contextA = _fixture.CreateAppContext(tenantA);
var rows = await contextA.Set<Payment>().IgnoreQueryFilters().ToListAsync(); // filter OFF → RLS only
Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
Assert.Equal(0, await contextA.Set<Payment>().IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantB));
```

### Anti-Patterns to Avoid
- **Base64-encoding blobs into JSON** — bloats the archive ~33% and defeats "commonly used format"; write real files.
- **Materializing the whole export in memory** (`ToList()` everything, build a `byte[]`) — OOM risk on large tenants; stream entry-by-entry.
- **Caching `/api` responses in the SW** — already correctly prevented; never relax the `NetworkOnly` rule for financial data.
- **Offline write/sync queues** — contradicts "Finalisierung bleibt online-gebunden"; out of scope.
- **A raw 403/stack trace where a feature is gated** — every gated surface must show `UpgradeHint`, not an error (the whole point of criterion 3).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ZIP creation | Custom archive writer | `System.IO.Compression.ZipArchive` (streamed) | BCL, correct, streamable |
| JSON serialization | Manual string building | `System.Text.Json.JsonSerializer.SerializeAsync` | Async, streaming, already used |
| Service worker / offline caching | Hand-written SW | `vite-plugin-pwa` + Workbox (already configured) | Precache/versioning/cleanup handled |
| Tenant isolation in export | Manual `WHERE tenant_id=` | RLS + `ICurrentTenant.SetTenant` | House rule; DB-enforced, cannot be forgotten per-query |
| RLS test harness | New Postgres setup | `PostgresFixture` / `CreateAppContext` / `IgnoreQueryFilters` | Proven pattern; tests RLS not the EF filter |

**Key insight:** Phase 9 has almost no "new technology" — the risk is *scope creep* (over-building offline sync or async export infra). Reuse the existing, proven patterns and keep the new surface minimal.

## Common Pitfalls

### Pitfall 1: Export runs without tenant context → empty or leaking archive
**What goes wrong:** In a Hangfire job (async path) or a mis-scoped service, `app.current_tenant` isn't set; RLS either returns nothing or the code bypasses it.
**How to avoid:** In a request the tenant middleware sets it; in a job call `ICurrentTenant.SetTenant(tenantId)` in a fresh scope (Pattern 1). Assert with the two-tenant RLS test.

### Pitfall 2: Export OOM on large tenants
**How to avoid:** Stream `ZipArchive` onto the response body; serialize each table with `SerializeAsync`; copy blobs directly. Never build a `byte[]` of the whole archive.

### Pitfall 3: TaxAdvisor can reach export because it's a GET
**What goes wrong:** The read-only guard allow-lists GETs by prefix; a new GET might accidentally be added to `ReadPrefixes`.
**How to avoid:** Do NOT add `/api/export` to `ReadOnlyAccessPolicy.ReadPrefixes`; default-deny keeps advisors out. Add a test asserting a TaxAdvisor gets 403 on `/api/export`.

### Pitfall 4: Adding server gating to Dunning/EInvoicing breaks existing tenants/tests
**What goes wrong:** Existing integration tests seed tenants on a plan that may not grant Dunning/EInvoicing; adding a 403 gate makes them fail.
**How to avoid:** Seed those tests on plan L/XL, or grant the capability in the fixture. Audit `Dunning*Tests`, `EInvoice*Tests`, `InboundEInvoiceTests` when adding the gate.

### Pitfall 5: `autoUpdate` service worker serving a stale shell after deploy
**What goes wrong:** Users keep an old app-shell across a deploy.
**How to avoid:** `registerType: 'autoUpdate'` + `cleanupOutdatedCaches: true` are already set; verify a new SW activates on reload and prompt/refresh behaves. Include a manual install+offline+reload verification step.

### Pitfall 6: Wording drift ("zertifiziert")
**How to avoid:** Add the grep (`rg -i "zertifiziert|certified"`) as an automated/manual verification step; keep compliance copy to "GoBD-konform".

### Pitfall 7: `Dashboard.tsx` reads `.features`, API returns `.capabilities`
**What goes wrong:** The plan/feature list may render empty due to the field-name mismatch.
**How to avoid:** Reconcile `web/src/lib/entitlements.ts` (`capabilities`) with `Dashboard.tsx` (`features`) when building the plan indicator.

## State of the Art

| Area | Current guidance | Note |
|------|------------------|------|
| GoBD | **New BMF directive 2026-relevant, dated 2025-07-14** updated the GoBD | e-invoice: the **structured XML is the record**; a separate PDF copy is no longer mandatory — Numera already retains `document_einvoice` XML, so this is satisfied; cite it in the Verfahrensdokumentation |
| DSGVO Art. 20 | Format left open; JSON/CSV/XML all valid ("Stand der Technik") | JSON-in-ZIP is conformant and common |

**Deprecated/outdated:** none relevant. The PWA config already reflects current `vite-plugin-pwa`/Workbox practice (precache shell, runtime NetworkOnly, navigateFallbackDenylist).

## Open Questions / Decisions (FLAG TO USER)

1. **D1 — Export delivery: sync stream vs async job.**
   - Known: Hangfire + tenant-job pattern exist; sync stream needs no new table/UI; v1 tenants small.
   - Recommendation: **synchronous streamed ZIP** for v1. Confirm before planning; async only if large tenants expected.
   - **D1a:** JSON-only vs JSON+CSV. Recommend **JSON-only** (avoids `CsvHelper`; DSGVO-conformant).

2. **D2 — Who may export: Owner-only vs any member.**
   - Recommendation: **Owner-only** (full personal-data + financial dump). Confirm.

3. **D3 — TaxAdvisor export.**
   - Current default-deny blocks it. Role doc-comment says "read/export focused."
   - Recommendation: **keep denied** (advisor should not pull the portability dump). Confirm.

4. **D4 — Offline scope / "Offline-Entwurfsverhalten".**
   - Recommendation: installability + offline app-shell + graceful degradation of in-progress forms; **no** IndexedDB draft persistence / background sync. Confirm the interpretation.

5. **D5 — Server-gate Dunning + EInvoicing (currently ungated).**
   - They are declared L+ in `PlanCapabilityMap` but not enforced. Closing criterion 3 "durchgängig" means adding the gate + UpgradeHint.
   - Recommendation: **add L+ gating** to `DunningEndpoints`, `EInvoiceEndpoints`, `InboundDocumentEndpoints` + frontend hints. Confirm (this changes behaviour for any plan-S tenant currently using these).

6. **D6 — Current-plan indicator + `UpgradeHint` tier parameterization + `Dashboard` `.features`/`.capabilities` bug.**
   - Recommendation: add a plan badge, parameterize `UpgradeHint` with the required tier, fix the field mismatch. Confirm scope.

## Suggested Plan Decomposition (for the planner — 4–6 plans)

1. **09-01 DSGVO Export subsystem** — `ExportEndpoints` + `TenantExportService` (streamed ZIP, all tables + blobs, Owner-only, DataExport-gated, audit-logged) + `TenantExportTests` (completeness + RLS + non-mutation + TaxAdvisor-403).
2. **09-02 Tarif-gate consistency** — server-gate Dunning + EInvoicing + Inbound; add `UpgradeHint`s; parameterize `UpgradeHint` tier; fix `Dashboard` capabilities field; add current-plan indicator.
3. **09-03 Cross-tenant RLS completeness** — `CrossTenantIsolationCompletenessTests` over the 8 uncovered tables (isolation + WITH CHECK reject).
4. **09-04 GoBD Verfahrensdokumentation draft** — `docs/gobd-verfahrensdokumentation.md` (4 parts, versioned "Entwurf") + wording audit (grep + copy fixes) + export README disclaimer.
5. **09-05 PWA install/offline hardening + human-verify** — installability audit, offline app-shell + graceful-degradation checks, finalize-blocked-offline UX, manual install/offline verification checkpoint.
- (Optional 09-06 if D1 → async: `export_archive` table + migration + RLS + Hangfire job + poll UI.)

**Ordering/parallelization:** 09-01, 09-02, 09-03, 09-04 are largely independent and can be planned in parallel; 09-05 (human-verify) and the final wording/RLS-green check should come last as the closing gate. 09-03 (RLS proof) and the 09-01 export RLS test together constitute criterion 4's "Cross-Tenant-Sicherheitssuite grün."

## Sources

### Primary (HIGH confidence — the codebase)
- `src/platform/Numera.Platform.Entitlements/{Capability,PlanCapabilityMap,EntitlementService,IEntitlementService,PlanFeatureFilter}.cs` — tarif-gate mechanism + tier matrix
- `src/Numera.Api/Endpoints/{Team,SalesDocument,RecurringInvoice,Dunning,EInvoice,InboundDocument,Me}Endpoints.cs` — gating audit
- `src/Numera.Api/Auth/{ReadOnlyWriteGuardMiddleware,ReadOnlyAccessPolicy}.cs` — TaxAdvisor allow-list (export default-deny)
- `src/Numera.Api/Jobs/RenderDocumentPdfJob.cs` — tenant-scoped job pattern
- `src/platform/Numera.Platform.Db/Migrations/*.cs` + `NumeraDbContextModelSnapshot.cs` — full table + RLS inventory
- `tests/Numera.IntegrationTests/RlsIsolationTests.cs` + the `*RlsTests.cs` set — RLS test pattern + coverage gap
- `web/vite.config.ts`, `web/public/manifest.webmanifest`, `web/src/pages/Dashboard.tsx`, `web/src/lib/entitlements.ts`, `web/src/features/shared/UpgradeHint.tsx` — PWA + FE gate state
- `src/platform/Numera.Platform.Db/Entities/Membership.cs` — roles (Owner/Employee/TaxAdvisor)

### Secondary (MEDIUM confidence — verified current sources)
- DSGVO Art. 20 format requirement (JSON/CSV/XML valid): dsgvo-gesetz.de, activemind.de, dsgvo-portal.de
- GoBD Verfahrensdokumentation 4 parts + 2025-07-14 BMF update (e-invoice XML retention): hamburger-software.de, lexware.de, datenschutzberater.nrw

## Metadata

**Confidence breakdown:**
- DSGVO export subsystem (tables/blobs/pattern): HIGH — enumerated directly from migrations + proven job/RLS patterns.
- Tarif-gate audit: HIGH — derived from exhaustive grep of server + frontend gate usages.
- Cross-tenant RLS gap list: HIGH — cross-referenced migration RLS arrays vs test-file coverage.
- PWA scope: HIGH on current config correctness; MEDIUM on intended offline-draft depth (Decision D4).
- Legal framing (DSGVO/GoBD): MEDIUM–HIGH — verified against multiple current German sources incl. the 2025-07-14 BMF directive.

**Research date:** 2026-07-29
**Valid until:** ~2026-08-28 (stable; re-check the GoBD/BMF guidance if compliance copy is challenged)

## RESEARCH COMPLETE
