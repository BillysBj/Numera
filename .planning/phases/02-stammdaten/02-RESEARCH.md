# Phase 2: Stammdaten (Master Data — Partners/CRM + Product Catalog) - Research

**Researched:** 2026-07-10
**Domain:** German B2B master-data (customers/suppliers CRM + product/service catalog) on the locked Numera platform kernel
**Confidence:** HIGH on how to build on Phase 1 (verified against actual code) and on EN 16931 / EF Core 10 facts; MEDIUM on VIES/VAT-ID operational choices and frontend library exact versions.

> No CONTEXT.md exists (no `/gsd:discuss-phase` was run). The stack and all Phase-1 platform decisions are LOCKED. This research is about how to build master data **on top of** that foundation, not whether to change it. A "Locked Foundation" section replaces the usual `## User Constraints` block.

---

## Summary

Phase 2 is deliberately "boring CRUD" — but in this codebase CRUD is the acceptance test for the Phase-1 multi-tenancy plumbing, and it must be built so that Phase 3 (invoicing), Phase 5 (e-invoicing / EN 16931), and Phase 8 (CRM expansion) plug in without rework. The two big modelling decisions are: **(1) one `BusinessPartner` entity with role flags (customer AND/OR supplier), not two entities** — matching how sevDesk/Lexware model contacts and how a real partner is often both; and **(2) archive-never-delete via soft-delete**, because master data will be referenced by immutable invoices and must survive under GoBD.

The single highest-leverage technical detail: **EF Core 10 named query filters**. The current `NumeraDbContext` applies an *unnamed* tenant query filter reflectively. Adding a second unnamed `HasQueryFilter` silently **overwrites** the first. You must convert the tenant filter to a named `"Tenant"` filter and add a named `"NotArchived"` filter, so both compose and each can be disabled independently. The existing integration tests call `IgnoreQueryFilters()` with **no arguments** (disables *all* named filters), so they keep making RLS the sole control — no test breakage.

The second non-obvious pitfall, learned from Phase 1's actual migrations: **the reflective entity discovery only adds the EF query filter + tenant-leading index. It does NOT create the Postgres RLS policy.** Every new tenant table in Phase 2 MUST get its own hand-written `ENABLE`/`FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation` SQL in the migration's `Up()` (and reversal in `Down()`), exactly as `20260710021521_InitialPlatform.cs` does. Forgetting this = a silent cross-tenant leak that the EF filter hides in dev but that fails the mandatory RLS integration gate.

**Primary recommendation:** Two new module projects `Numera.Modules.Crm` and `Numera.Modules.Catalog` (entities only, mirroring `Numera.Modules.Ledger`); one shared EF migration in `Platform.Db` per table group with explicit RLS SQL; minimal-API endpoints in `Numera.Api/Endpoints/*` following the existing style; FluentValidation on the backend and (newly added) shadcn/ui + TanStack Table + react-hook-form + zod on the frontend; VAT-ID validated offline-only in v1 with an optional best-effort VIES seam.

---

## Locked Foundation (from Phase 1 — build ON these, do not change)

Verified by reading the actual code, not just the summaries:

| Concern | Locked mechanism | File(s) |
|---|---|---|
| Tenancy runtime | `ICurrentTenant`/`TenantContext` (scoped) → `TenantConnectionInterceptor` sets `app.current_tenant` GUC via parameterized `set_config` on open, `RESET` on close | `src/platform/Numera.Platform.Tenancy/*` |
| Tenant entity marker | `ITenantEntity` (`Guid TenantId`) drives reflective registration, `(tenant_id, id)` index, and the EF global query filter | `src/platform/Numera.Platform.Db/ITenantEntity.cs`, `NumeraDbContext.cs` |
| DB isolation (primary) | Postgres `ENABLE`+`FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation USING/WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid)` — **hand-written per table in the migration** | `Migrations/20260710021521_InitialPlatform.cs`, `Sql/rls_policies.sql` |
| DB isolation (defence-in-depth) | Reflective EF global query filter mirroring RLS (currently **unnamed**) | `NumeraDbContext.ApplyTenantQueryFilters` |
| Runtime DB role | App connects as `numera_app` (NO BYPASSRLS, NO SUPERUSER); migrations as `numera_migrator` | `scripts/db-roles.sql`, `appsettings.Development.json` |
| PK convention | UUIDv7 `Guid.CreateVersion7()`, `ValueGeneratedNever()` | `Entities/Membership.cs` |
| Schema convention | snake_case applied in `OnModelCreating`; tables named explicitly | `NumeraDbContext.ApplySnakeCaseNaming` |
| Money | `Numera.Platform.Money` (`Money` readonly record struct, `TaxCategory` enum S/AE/K/E/Z/G/O, `RoundingPolicy`); DB `numeric(19,4)`; **never float** | `src/platform/Numera.Platform.Money/*` |
| Audit | `IAuditWriter.RecordAsync` appends an `AuditEvent` into the **caller's** `NumeraDbContext` (no own transaction) → atomic with the change; `audit_events` DB-enforced append-only | `src/platform/Numera.Platform.Audit/*` |
| Actor seam | `ICurrentUser.UserId` (nullable) in `Platform.Tenancy`; `CurrentUser` reads `sub` claim | `src/Numera.Api/Auth/CurrentUser.cs` |
| Entitlements | `IEntitlementService` + `PlanCapabilityMap` + `PlanFeatureFilter` (`[FilterAlias("Plan")]`, scoped feature management) | `src/platform/Numera.Platform.Entitlements/*` |
| Module wiring | Module `.csproj` references `Platform.Db`; **`Numera.Api` references the module** so its DLL deploys next to `Platform.Db` for reflective discovery + design-time migrations; migrations run with `--startup-project Numera.Api` | `Numera.Modules.Ledger.csproj`, `Numera.Api.csproj` |
| Endpoints | Minimal APIs, static `MapXxxEndpoints(this IEndpointRouteBuilder)` classes, `.RequireAuthorization()` | `src/Numera.Api/Endpoints/MeEndpoints.cs` |
| Frontend | React 19 + Vite 7 + vite-plugin-pwa (app-shell precache, `/api` NetworkOnly + `navigateFallbackDenylist [/^\/api/]`); token-less cookie client `web/src/lib/api.ts`; react-i18next namespaces `common`/`auth`; TanStack Query installed | `web/*` |
| Tests | `tests/Numera.Platform.Tests` (unit, xUnit, **plain asserts** — no Fluent Assertions); `tests/Numera.IntegrationTests` (Testcontainers postgres:18, runs as `numera_app`, `IgnoreQueryFilters()` so RLS is sole control); CI hard gate on RLS + audit suites | `tests/*`, `.github/workflows/ci.yml` |

**Not yet installed (Phase 2 introduces):** shadcn/ui, Tailwind, TanStack Table, react-hook-form, zod (STACK.md expects them). `Numera.Platform.Money` still targets `net8.0` (per 01-03) — retarget to `net10.0` when Catalog references it (cross-TFM reference works but retargeting is cleaner).

---

## Standard Stack (additions for Phase 2)

### Backend
| Library | Version | Purpose | Why standard |
|---|---|---|---|
| FluentValidation | 12.x (12.1.1 current) | Request-DTO validation (partner/catalog create/update) | STACK.md choice; **Apache-2.0, still free** (verified — *not* the Fluent Assertions relicensing). Readable rule chains. |
| (existing) EF Core 10 + Npgsql 10 | 10.0.* | Entities, migrations, named query filters | Locked. |
| (existing) Numera.Platform.Money | — | Catalog net/cost price + TaxCategory | Locked. |

Do **not** add MediatR (moved commercial) or Fluent Assertions (moved commercial). Keep hand-wired services + plain xUnit asserts, consistent with Phase 1.

### Frontend
| Library | Version | Purpose | Notes |
|---|---|---|---|
| tailwindcss | 4.x | Styling base for shadcn/ui | Vite 7 plugin `@tailwindcss/vite`. |
| shadcn/ui | latest CLI | Accessible component primitives (Table, Dialog, Form, Input, Select, DropdownMenu, Badge, Sonner/toast) | Code-owned (Radix + Tailwind), no runtime dep. Install per-component via CLI. |
| @tanstack/react-table | 8.x | Headless data grid for list views | Server-side pagination/sort/filter via `manualPagination`/`manualSorting`/`manualFiltering`. |
| react-hook-form | 7.x | Partner/catalog forms | Pairs with zod resolver. |
| zod | 3.23+ (or 4.x) | Client-side schema validation | `@hookform/resolvers/zod`. Mirror server FluentValidation rules. |
| (existing) @tanstack/react-query | 5.x | Server state, pagination caching | Use `placeholderData: keepPreviousData` for smooth paging. |

**Install (backend):** `dotnet add src/Numera.Api package FluentValidation.DependencyInjectionExtensions`
**Install (frontend):** `npm i @tanstack/react-table react-hook-form zod @hookform/resolvers` + `npm i -D tailwindcss @tailwindcss/vite` + `npx shadcn@latest init` then `npx shadcn@latest add table dialog form input select badge dropdown-menu sonner`.

---

## Architecture Patterns

### Recommended module/project structure
Mirror `Numera.Modules.Ledger` exactly (it is the reference for "a module = entities referencing Platform.Db, discovered reflectively"):

```
src/modules/
├── Numera.Modules.Crm/            # NEW — references Platform.Db (+ Platform.Money if a partner default price ever needed; not now)
│   ├── BusinessPartner.cs         # ITenantEntity, IArchivable; owned Address(es)
│   ├── PartnerContact.cs          # ITenantEntity (child table)
│   ├── PartnerNote.cs             # ITenantEntity (child table)
│   ├── PartnerActivity.cs         # ITenantEntity (append-only timeline seam)
│   ├── PartnerRole / PaymentTerms / Address (value objects/enums)
├── Numera.Modules.Catalog/        # NEW — references Platform.Db + Platform.Money
│   ├── CatalogItem.cs             # ITenantEntity, IArchivable
│   └── UnitOfMeasure.cs           # curated UN/ECE Rec 20 code list
src/Numera.Api/
├── Endpoints/PartnerEndpoints.cs      # NEW  MapPartnerEndpoints
├── Endpoints/CatalogEndpoints.cs      # NEW  MapCatalogEndpoints
├── Validators/…                        # FluentValidation validators (or co-located)
└── (Program.cs: reference both modules, register validators, map endpoints)
src/platform/Numera.Platform.Db/
├── ITenantEntity.cs
├── IArchivable.cs                      # NEW marker (Guid? ArchivedAt or DateTimeOffset? ArchivedAt)
└── Migrations/…_Stammdaten.cs          # NEW migration(s): tables + RLS SQL per table
```

`Numera.Api.csproj` must add `<ProjectReference>` to both new modules (so their DLLs deploy next to `Platform.Db` for reflective discovery + design-time `dotnet ef`). This is the exact wiring `Numera.Api.csproj` already does for `Modules.Ledger`.

Keep **entities in modules, endpoints + services + validators in `Numera.Api`** — consistent with Phase 1 (the Ledger module is entities-only; `RegistrationService` lives in the Api). Don't introduce a per-module service/DI convention that Phase 1 didn't establish.

### Pattern 1: Named query filters (tenant + archive) — EF Core 10
**What:** EF Core 10 adds *named* query filters so an entity can carry multiple filters that compose and can be individually disabled. Prior to EF 10 you combined with `&&` in a single unnamed filter and could not disable one selectively.

**Why load-bearing here:** The current `NumeraDbContext.ApplyTenantQueryFilters` calls the **unnamed** `HasQueryFilter`. A second unnamed call **replaces** it. So to add "hide archived by default" you MUST switch to named filters.

```csharp
// Source: https://learn.microsoft.com/en-us/ef/core/querying/filters  (EF Core 10 named filters)
// In NumeraDbContext, replace the unnamed tenant filter with a named one, and add an
// archive filter for IArchivable entities. Names centralised as constants.
public const string TenantFilter = "Tenant";
public const string NotArchivedFilter = "NotArchived";

// tenant filter (all ITenantEntity):
modelBuilder.Entity(clr).HasQueryFilter(TenantFilter,
    e => EF.Property<Guid>(e, nameof(ITenantEntity.TenantId)) == _currentTenant.TenantId);

// archive filter (only IArchivable):
modelBuilder.Entity(clr).HasQueryFilter(NotArchivedFilter,
    e => EF.Property<DateTimeOffset?>(e, nameof(IArchivable.ArchivedAt)) == null);
```

- **List "active" (default):** normal query → both filters apply.
- **List including archived:** `db.Partners.IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter])` → keeps tenant isolation, shows archived.
- **Integration tests:** they already call `IgnoreQueryFilters()` **with no args**, which disables *all* named filters → RLS remains the sole control. **No test change needed**, and this is exactly why RLS (not the EF filter) must enforce both tenancy AND — note — RLS does NOT enforce archive; archive is app-level only, which is fine (archived rows are still the tenant's own data).

Confidence: HIGH (verified against Microsoft Learn + multiple 2025 write-ups; the reflection-vs-named-filter interaction verified against the actual `NumeraDbContext`).

### Pattern 2: Archive-never-delete (GoBD/DSGVO)
**What:** `IArchivable { DateTimeOffset? ArchivedAt }`. "Delete" endpoints are `POST /{id}/archive` / `/{id}/unarchive` that set/clear `ArchivedAt` + write an audit event + a `PartnerActivity`. No `DELETE` route that hard-deletes a partner or catalog item.
**Why:** Phase 3 invoices will FK to partners/catalog snapshots; GoBD forbids destroying data referenced by tax documents; DSGVO erasure applies only to non-tax PII and is a Phase 9 concern. Archiving keeps referential integrity intact.
**Nuance:** In Phase 2 nothing references partners yet, so a hard delete *would* work today — but building archive now avoids a painful retrofit once Phase 3 lands. Recommend archive from day one; optionally allow hard-delete ONLY of a never-referenced draft partner if desired (not required — simpler to always archive).

### Pattern 3: Per-table RLS in the migration (manual, not reflective)
**What:** For every new tenant table (`partners`, `partner_contacts`, `partner_notes`, `partner_activities`, `catalog_items`) the migration `Up()` must include, after `CreateTable`:
```csharp
foreach (var t in new[] { "partners", "partner_contacts", "partner_notes", "partner_activities", "catalog_items" })
{
    migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
    migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
    migrationBuilder.Sql($"CREATE POLICY tenant_isolation ON {t} " +
        "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
        "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
}
```
and `Down()` reverses (`DROP POLICY`, `NO FORCE`, `DISABLE`). Copy the master reference in `Sql/rls_policies.sql` to add the new tables. Owned-type addresses live in the partner row → covered by the partner policy automatically (no separate table).
**Why:** The reflective `RegisterModuleTenantEntities` only adds the EF filter + `(tenant_id, id)` index. It does not touch Postgres RLS. This is the #1 silent-leak trap for Phase 2.

### Pattern 4: Command → DbContext write → audit in same transaction
Follow Phase 1: an endpoint/service mutates entities, calls `IAuditWriter.RecordAsync(...)` (which just `Add`s to the same `NumeraDbContext`), then a single `SaveChangesAsync()` commits the change + audit row + activity row atomically. Never open a second transaction for the audit.

### Anti-patterns to avoid
- **Second unnamed `HasQueryFilter`** → silently overwrites the tenant filter (see Pattern 1). Always name filters in EF 10.
- **`DELETE` that removes a partner/catalog row.** Use archive.
- **Free-text unit of measure.** Store a UN/ECE Rec 20 code (EN 16931 BT-130).
- **`float`/`double`/`real` for prices.** Use `decimal` → `numeric(19,4)` via `Money`.
- **Kleinunternehmer flag on the partner.** That flag is the **tenant's** (issuer's) status and lives on the tenant/settings, not on the counterparty. The partner carries the *counterparty's* USt-IdNr and tax status.
- **Blocking save on VIES.** VIES is flaky; never gate a create/update on it.

---

## Domain Model (prescriptive)

### Q1 — One `BusinessPartner` with role flags (FIRM recommendation)
A partner can be both a customer and a supplier (a supplier you also sell to). sevDesk and Lexware Office both model a single "contact"/"Kontakt" that can be flagged Kunde and/or Lieferant, with separate customer/supplier numbers. DATEV keeps separate Debitoren/Kreditoren *account* numbers but the underlying business partner is one entity.

**Recommendation:** ONE `BusinessPartner` entity (`partners` table) with:
- `bool IsCustomer`, `bool IsSupplier` (at least one true; enforced by validator).
- `string? CustomerNumber` (Debitor-facing) and `string? SupplierNumber` (Kreditor-facing) — plain strings, unique-per-tenant among non-archived when present. **Not** the gapless invoice-numbering machinery (that's Phase 3, legal numbers only). Auto-suggest a next value or let the user type one.

Rejected alternative: separate `Customer` and `Supplier` entities → duplicates address/contact/notes, and a dual-role partner becomes two disconnected records with divergent master data. Only choose separate entities if customer and supplier data truly never overlap (they do overlap here).

### Q2 — German master-data fields (NOW vs LATER)
Map to EN 16931 buyer group so Phase 3/5 can populate an invoice directly. When *this tenant sells to this partner*, the partner is the EN 16931 **Buyer** (BG-7/BG-8); the fields below cover BT-44..BT-57.

**Build NOW (Phase 2 v1):**
| Field | Type | EN 16931 | Notes |
|---|---|---|---|
| Display name / company | string (required) | BT-44 Buyer name | |
| Legal form (optional) | string? | — | e.g. GmbH; free text v1 |
| Address: street/line1 | string | BT-50 | Owned type `Address` |
| Address: line2/additional | string? | BT-51 | |
| Address: postal code | string | BT-53 | |
| Address: city | string | BT-52 | |
| Address: country | string (ISO 3166-1 alpha-2, default `DE`) | BT-55 | **code, not name** |
| Postfach (PO box) | string? | (maps to line) | optional |
| USt-IdNr (VAT ID) | string? | BT-48 Buyer VAT identifier | offline-validated (Q3) |
| Steuernummer | string? | — | national tax number; not a VAT ID |
| Email | string? | BT-49 (electronic address seam) | |
| Phone / website | string? | BG-9 contact | |
| Contacts (Ansprechpartner) | child collection | BG-9 (BT-56/57) | Q5 |
| Payment terms: net days | int? | BT-20 (invoice-level later) | e.g. 14 |
| Payment terms: Skonto % + days | decimal? / int? | BT-20 text | cash discount |
| Default currency | string (ISO 4217, default `EUR`) | BT-5 | |
| Language (de/en) | enum/string | — | drives future doc language |
| Default TaxCategory (optional) | `TaxCategory`? | BT-151 seam | convenience default |
| IsCustomer / IsSupplier | bool | — | role flags |
| CustomerNumber / SupplierNumber | string? | BT-46 buyer identifier seam | |
| ArchivedAt | timestamptz? | — | soft-delete |

**Defer but add cheap nullable seams NOW (so Phase 5/DATEV don't need a schema break):**
| Field | Why deferred | Seam |
|---|---|---|
| IBAN / BIC | Not needed to create master data; used by payments/dunning (Phase 6). | Add nullable `Iban`/`Bic` columns now (cheap) or in Phase 6. Recommend adding now (encrypt-later noted in PITFALLS). |
| Leitweg-ID (B2G, BT-10 buyer reference) | Only mandatory for public-sector XRechnung (Phase 5). | Add nullable `LeitwegId` column now — trivial and avoids a later migration. |
| Debitoren-/Kreditorenkonto | DATEV export is a later milestone; needs the ledger. | Add nullable `DebtorAccount`/`CreditorAccount` strings now OR in the DATEV phase. Recommend nullable now. |

Rule of thumb: **columns are cheap, migrations touching RLS tables are annoying** — add the nullable B2G/DATEV/bank seams now rather than re-migrating. Do NOT build UI/validation for them yet (leave them out of the v1 form or behind an "advanced" section).

### Q5 — Contacts & addresses (minimum that won't need rework)
- **Contacts:** real child entity `PartnerContact` (`partner_contacts` table, `ITenantEntity`, own RLS policy): `Id, TenantId, PartnerId, Salutation?, FirstName?, LastName, Email?, Phone?, Role/Position?, IsPrimary`. A partner has 0..n contacts. Maps to EN 16931 BG-9 (BT-56 contact name, BT-58 email).
- **Addresses:** Recommend **owned types on the partner** for v1, not a separate addresses table:
  - `Address` (primary/billing) — embedded, covers BG-8 buyer postal address.
  - `Address? ShippingAddress` (nullable, deviating Lieferanschrift) — embedded nullable, covers the Phase-3 Lieferschein deviating-ship-to case.
  Owned types store in the `partners` row → no extra RLS table, no extra join, and adding a full `partner_addresses` child table later (if arbitrary multiple addresses are ever needed) is a non-breaking additive change. This is the minimum that survives Phase 3 (billing + deviating shipping) without over-building a multi-address system nobody has asked for.

### Q6 — Product/service catalog
`CatalogItem` (`catalog_items`, `ITenantEntity`, `IArchivable`):
| Field | Type | EN 16931 | Notes |
|---|---|---|---|
| Id | UUIDv7 | — | |
| TenantId | Guid | — | |
| ItemNumber (Artikelnummer) | string (required) | BT-155 seller's item id | **unique per tenant** — partial unique index `(tenant_id, item_number)` where `archived_at IS NULL` (or plain unique per tenant; see Q11). |
| Name | string (required) | BT-153 item name | |
| Description | string? | BT-154 | |
| Kind | enum Product/Service | — | drives default unit |
| UnitOfMeasure | string (UN/ECE Rec 20 code) | **BT-130** | curated dropdown, store code (see below) |
| NetPrice | decimal → `numeric(19,4)` | BT-146 item net price | `Money`/decimal, never float |
| TaxCategory | `TaxCategory` enum | BT-151 category code | S/AE/K/E/Z/G/O |
| VatRatePercent | decimal? → `numeric(5,2)` | BT-152 rate | e.g. 19.00, 7.00, 0 |
| CostPrice (optional) | decimal? | — | for later margin/EÜR; nullable |
| RevenueAccount (seam) | string? | — | SKR03/04 account hint for DATEV/ledger; nullable |
| ArchivedAt | timestamptz? | — | |

**Unit of measure (BT-130 — must be UN/ECE Rec 20):** store the code, expose a curated German-labelled dropdown. Common codes for a DE goods/services shop:
`C62` = Stück/one (dimensionless), `H87` = piece, `HUR` = Stunde (hour), `DAY` = Tag, `MON` = Monat, `KGM` = Kilogramm, `MTR` = Meter, `MTK` = Quadratmeter, `LTR` = Liter, `KWH` = Kilowattstunde, `LS`/`XPP`… For a solo/SMB tool a curated list of ~12–15 codes is right; you do NOT implement the full code list (sellers only need the units they use). Default `C62` for products, `HUR` for services.
**Price history / valid-from:** **DEFER.** v1 = single current `NetPrice`. Note it as a future additive `catalog_item_prices(valid_from, price)` child if versioned pricing is ever needed — invoices will snapshot the price at line creation anyway (Phase 3), so history on the catalog is a convenience, not a correctness requirement.
**CATL-02 ("as line items in documents"):** Phase 2 delivers the *seam*, not the invoice. Provide a catalog lookup/picker endpoint (`GET /api/catalog-items?q=…`) and a stable DTO (number, name, unit code, net price, tax category, rate) that Phase 3's line editor consumes. Do NOT build invoice lines here.

### Q7 — Activity/document history (CRM-02)
CRM-02 wants a per-partner timeline of "Belege und Aktivitäten". Belege don't exist until Phase 3, so:
- **Recommendation: a dedicated append-only `partner_activities` table** (`ITenantEntity`, own RLS policy), NOT a projection of `audit_events`. Reasons: `audit_events` is a security/compliance log (actor-centric, before/after jsonb, DB-append-only, different retention/immutability semantics) — coupling a user-facing CRM timeline to its internal shape is brittle and mixes concerns. A separate table is the clean seam that Phase 3 (invoice.created/finalized), Phase 6 (payment/dunning), and Phase 8 (tasks/emails) all append to.
- `PartnerActivity`: `Id, TenantId, PartnerId, OccurredAt, Type (enum: PartnerCreated/PartnerUpdated/Archived/NoteAdded/…extensible), ActorUserId?, Summary (string), RefType? + RefId? (polymorphic link to a future document)`. Append-only by convention (v1 need not add a DB trigger; you may reuse the audit append-only pattern later if it must be tamper-evident — probably unnecessary since it's not tax-relevant).
- **In Phase 2** seed it with `PartnerCreated`, `PartnerUpdated`, `Archived/Unarchived`, `NoteAdded`. The partner detail page shows the activity timeline plus a "Belege" section that is empty now but has a stable contract: Phase 3 will give invoices a `partner_id` FK, and the detail page will query `GET /api/partners/{id}/documents` (add the empty endpoint or defer — recommend defer the documents endpoint to Phase 3, but design the timeline UI to accommodate a documents tab).

### Q8 — Notes (CRM-03)
Simple child entity `PartnerNote` (`partner_notes`, `ITenantEntity`, own RLS policy): `Id, TenantId, PartnerId, AuthorUserId, Body (text), CreatedAt, UpdatedAt`. **GoBD-irrelevant** (notes aren't tax records) → **editable and deletable** by the tenant; no immutability, no append-only trigger. On create, also append a `PartnerActivity(NoteAdded)` so it shows on the timeline. Keep it dead simple — no rich text, no attachments (attachments = Kundenakte = CRM-05 = Phase 8, explicitly out of scope).

---

## VAT-ID Validation (Q3) — pragmatic v1

**Recommendation: offline format validation only in v1, with an optional best-effort VIES seam. Do NOT call VIES synchronously and never block a save on it.**

1. **Format (offline, always):** per-country regex + length. German USt-IdNr = `DE` + 9 digits, first digit ≠ 0 (`^DE[1-9]\d{8}$`). Optionally add the German checksum (ISO 7064 MOD 11,10 over the 9 digits) — cheap and catches typos. For other EU countries, at minimum validate the country prefix + length; full per-country checksums can be added incrementally. Store the raw normalised value regardless.
2. **VIES (optional, async, best-effort):** The official EU endpoint now offers REST at `https://ec.europa.eu/taxation_customs/vies/rest-api/check-vat-number` (plus the legacy SOAP `checkVat`). It is free but **flaky** (member-state services go down; p95 latency multiple seconds). If you offer a "VIES prüfen" button, call it from a background/worker job or a separate non-blocking endpoint, cache the result (`vies_checked_at`, `vies_valid`, requested/returned name+address) on the partner, and degrade gracefully on timeout/unavailable. Never make partner creation depend on it.
3. **§18e UStG qualified confirmation (BZSt):** a *qualifizierte Bestätigungsanfrage* additionally matches name/legal-form/city/postcode/street and yields a per-field confirmation you must retain (§147 AO, ~10 years) to substantiate a tax-exempt intra-EU supply. This only matters once you issue **innergemeinschaftliche Lieferungen** (EN 16931 category `K`) — a Phase 3/5 tax concern. **Design a seam** (nullable `vies_*` fields or a small `vat_id_checks` table) but do NOT build the BZSt integration in Phase 2.

**Testing:** unit-test the format+checksum validator (valid DE, invalid checksum, wrong length, non-DE prefixes) — pure functions, no network. Do NOT write tests that hit live VIES.

Confidence: MEDIUM-HIGH (VIES REST endpoint + flakiness + DE format verified via multiple 2025 sources; exact non-DE checksum coverage is a scope decision, not a fact).

---

## API Design (Q9) — conventions + worked example

**Conventions (match `MeEndpoints.cs`):**
- Minimal APIs, one static `MapXxxEndpoints` class per resource, `.RequireAuthorization()` on every route.
- Routes:
  - `GET /api/partners` — list (pagination/filter/sort, below)
  - `POST /api/partners` — create
  - `GET /api/partners/{id}` — detail
  - `PUT /api/partners/{id}` — full update (or `PATCH` for partial; pick one — recommend `PUT` with full DTO for simplicity)
  - `POST /api/partners/{id}/archive` / `POST /api/partners/{id}/unarchive` — never `DELETE`
  - `GET/POST /api/partners/{id}/contacts`, `PUT/DELETE …/contacts/{cid}`
  - `GET/POST /api/partners/{id}/notes`, `PUT/DELETE …/notes/{nid}`
  - `GET /api/partners/{id}/activities`
  - `GET /api/catalog-items`, `POST`, `GET/PUT /{id}`, `POST /{id}/archive`, and a lightweight `GET /api/catalog-items?q=` picker for Phase 3.
- **List query params:** `?page=1&pageSize=25&sort=name&dir=asc&q=<search>&role=customer|supplier&archived=false`. Clamp `pageSize` (e.g. max 100). Default `archived=false` (applies the `NotArchived` filter); `archived=true`/`all` uses `IgnoreQueryFilters([NotArchivedFilter])`.
- **List response envelope:** `{ "items": [...], "page": 1, "pageSize": 25, "total": 137 }`. `total` from a `CountAsync()` (RLS-scoped, cheap with the tenant-leading index). This is exactly what TanStack Table server-side pagination needs (`rowCount`).
- **Validation:** FluentValidation validator per request DTO; invoke via a small endpoint filter or explicitly at the top of the handler → return `Results.ValidationProblem(errors)` (RFC 7807) on failure.
- **Audit:** create/update/archive call `IAuditWriter.RecordAsync("partner.created"/"partner.updated"/"partner.archived", entityId, before, after)` then one `SaveChangesAsync`. Also append a `PartnerActivity`.
- **Money:** map `NetPrice`/`CostPrice` to `numeric(19,4)`; DTOs carry `decimal`, never float; serialize as string or number consistently (decimal is fine in System.Text.Json).

**Worked example (create partner):**
```csharp
// src/Numera.Api/Endpoints/PartnerEndpoints.cs
public static IEndpointRouteBuilder MapPartnerEndpoints(this IEndpointRouteBuilder app)
{
    var g = app.MapGroup("/api/partners").RequireAuthorization();

    g.MapGet("/", async (int page, int pageSize, string? q, string? role, bool? archived,
        NumeraDbContext db, CancellationToken ct) =>
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<BusinessPartner> query = db.Set<BusinessPartner>();
        if (archived == true) query = query.IgnoreQueryFilters([NumeraDbContext.NotArchivedFilter]);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q));
        if (role == "customer") query = query.Where(p => p.IsCustomer);
        if (role == "supplier") query = query.Where(p => p.IsSupplier);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PartnerListItem(p.Id, p.Name, p.IsCustomer, p.IsSupplier, p.ArchivedAt != null))
            .ToListAsync(ct);
        return Results.Ok(new { items, page, pageSize, total });
    });

    g.MapPost("/", async (CreatePartnerRequest req, IValidator<CreatePartnerRequest> validator,
        NumeraDbContext db, IAuditWriter audit, ICurrentTenant tenant, CancellationToken ct) =>
    {
        var v = await validator.ValidateAsync(req, ct);
        if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
        var p = new BusinessPartner { TenantId = tenant.TenantId!.Value, /* map fields */ };
        db.Add(p);
        await audit.RecordAsync("partner.created", p.Id, before: null, after: p, ct);
        // db.Add(new PartnerActivity { ... Type = PartnerCreated ... });
        await db.SaveChangesAsync(ct);   // atomic: partner + audit + activity
        return Results.Created($"/api/partners/{p.Id}", new { p.Id });
    });
    // …
    return app;
}
```

---

## Frontend (Q10)

- **Introduce the UI stack:** Tailwind v4 (`@tailwindcss/vite`), shadcn/ui components, TanStack Table 8, react-hook-form + zod. TanStack Query is already present.
- **List views (TanStack Table, server-side):**
  ```ts
  // Source: https://tanstack.com/table/latest/docs/guide/pagination (manual pagination)
  useReactTable({
    data, columns,
    manualPagination: true, manualSorting: true, manualFiltering: true,
    rowCount: total,                     // from the API envelope
    state: { pagination, sorting },
    onPaginationChange: setPagination,
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
  })
  ```
  Drive a TanStack Query `useQuery` keyed by `['partners', { page, pageSize, sort, dir, q, role, archived }]` with `placeholderData: keepPreviousData` so pages don't flash. Build on the shadcn "data table" pattern (shadcn Table + TanStack Table).
- **Forms:** react-hook-form + `zodResolver`. Mirror the FluentValidation rules in a zod schema (client-side UX only; server stays authoritative). Address as a nested object; contacts as a `useFieldArray`.
- **i18n:** add new namespaces `partners` and `catalog` (DE default/fallback, EN) alongside `common`/`auth`. Follow the `web/src/i18n/locales/{de,en}/*.json` split.
- **Optimistic updates:** for the archive/unarchive toggle, optimistic update + rollback on error is nice UX. For create/edit, prefer `invalidateQueries(['partners'])` on success (simpler, master data isn't latency-critical). Never rely on optimistic state as source of truth.
- **PWA rule (unchanged):** all new endpoints are under `/api`, which the service worker already serves `NetworkOnly` with `navigateFallbackDenylist [/^\/api/]`. Do NOT add runtime caching for `/api`. No change to `vite.config.ts` PWA config is needed for data; only ensure new routes are client-routed (SPA fallback) not intercepted.

Confidence: MEDIUM-HIGH (TanStack manual pagination API is stable v8; exact shadcn/Tailwind v4 install steps move — verify with `npx shadcn@latest` at build time).

---

## Testing (Q11)

**Mandatory (CI hard gate, pattern from `01-08`):** integration tests in `tests/Numera.IntegrationTests`, run as `numera_app` with `IgnoreQueryFilters()`, proving **RLS isolation for every new table**: `partners`, `partner_contacts`, `partner_notes`, `partner_activities`, `catalog_items`. For each: tenant A sees only A's rows / zero B rows; cross-tenant INSERT rejected by `WITH CHECK`; unset GUC fails closed. Add these to `RlsIsolationTests` (or a new `StammdatenRlsTests`) so a missing RLS policy on a new table fails the pipeline. This directly guards the Pattern-3 pitfall.

**Also:**
- **Unit (xUnit, plain asserts — no Fluent Assertions):** VAT-ID validator (DE format + checksum, invalid cases, non-DE prefixes); any payment-terms/Skonto arithmetic; unit-code mapping/defaults.
- **Unique constraint:** integration test that a duplicate `(tenant_id, item_number)` catalog insert is rejected, and that the *same* item number is allowed in a *different* tenant.
- **Archive filter behaviour:** an EF-level test that archived rows are hidden by default and visible via `IgnoreQueryFilters([NotArchivedFilter])`, and that the tenant filter still applies when only the archive filter is ignored (named-filter composition).
- Keep VIES out of automated tests (network flakiness).

---

## Common Pitfalls (Q12) — Phase-2 specific

| Pitfall | Consequence | Prevention |
|---|---|---|
| **New tenant table without RLS SQL in the migration** | Silent cross-tenant leak (EF filter hides it in dev; RLS gate catches it in CI) | Add `ENABLE`/`FORCE`/`CREATE POLICY tenant_isolation` per new table; update `Sql/rls_policies.sql`; RLS integration test per table. |
| **Second unnamed `HasQueryFilter`** (EF < 10 habit) | Overwrites the tenant filter → archive filter replaces tenancy defence-in-depth | Use EF 10 **named** filters (`"Tenant"`, `"NotArchived"`); centralise names as constants. |
| **Hard-deleting partners/catalog** | Breaks Phase-3 invoice references; GoBD violation | Archive via `ArchivedAt`; `POST /archive`, no `DELETE`. |
| **Missing tenant_id-leading index on a child table** | ~100× slower RLS scans | `RegisterModuleTenantEntities` auto-adds `(tenant_id, id)` for `ITenantEntity`; verify child tables (contacts/notes/activities) also carry an index leading with `tenant_id` for their common access path (e.g. `(tenant_id, partner_id)`). |
| **Free-text unit of measure** | EN 16931 BT-130 rejects it in Phase 5 | Store UN/ECE Rec 20 code from a curated dropdown. |
| **`float` for prices** | Rounding drift, later EN 16931 arithmetic failures | `decimal` → `numeric(19,4)`; reuse `Money`. |
| **Kleinunternehmer flag on the partner** | Wrong tax logic later | KU is the tenant's status (issuer); partner holds counterparty's USt-IdNr/tax status only. |
| **Blocking save on VIES / hard-requiring Leitweg-ID** | Broken UX, VIES outages block work; Leitweg-ID is B2G-only | Offline format check only; VIES async best-effort; Leitweg-ID nullable/optional. |
| **Forgetting to reference the new module from `Numera.Api`** | Reflective discovery + design-time migration don't see the entities (`PendingModelChangesWarning`) | Add `<ProjectReference>` to both modules in `Numera.Api.csproj`; run `dotnet ef` with `--startup-project src/Numera.Api`. Integration test project must also reference the modules (01-08 hit this). |
| **Owned-type address forgotten in RLS reasoning** | (Non-issue if aware) | Owned `Address` lives in the `partners` row → covered by the partner policy; no separate table/policy. |
| **`Numera.Platform.Money` still net8.0** | TFM mismatch friction when Catalog references it | Retarget Money to `net10.0` (remove its `TargetFramework` override) as part of Catalog wiring. |

---

## State of the Art / things that changed

| Old approach | Current (use this) | Since |
|---|---|---|
| Single combined `&&` query filter, no selective disable | **EF Core 10 named query filters** (`HasQueryFilter("name", …)`, `IgnoreQueryFilters(["name"])`) | .NET 10 (Nov 2025) |
| VIES SOAP `checkVat` only | VIES **REST** `.../vies/rest-api/check-vat-number` (still free, still flaky) | 2024+ |
| MediatR / AutoMapper / Fluent Assertions as free defaults | Those went commercial — **avoid**; FluentValidation (Apache-2.0) remains free | 2024–2025 |

---

## Open Questions

1. **Customer/Supplier numbering scheme** — auto-assigned per-tenant counter vs user-entered? Recommendation: user-entered with an optional "suggest next" helper; NOT the gapless legal-numbering system (Phase 3). Confirm with product whether auto-numbering is desired. *Low risk either way.*
2. **How much of the B2G/DATEV/bank seam to surface in the v1 form** — recommend adding nullable columns now but hiding the fields (or an "Advanced" collapsible) to avoid over-building UI. Planner decision.
3. **Non-DE VAT-ID checksums** — v1 can ship DE checksum + generic prefix/length for others, adding per-country checksums incrementally. Scope decision, not a blocker.
4. **`partner_activities` tamper-evidence** — v1 treats it as a plain append-by-convention table (not tax-relevant). If later deemed compliance-relevant, reuse the audit append-only trigger pattern. Defer.
5. **shadcn/Tailwind v4 exact init steps** — verify with the current `npx shadcn@latest` CLI at execution time (frontend tooling moves fast).

---

## Sources

### Primary (HIGH)
- Actual repo code: `NumeraDbContext.cs`, `Migrations/20260710021521_InitialPlatform.cs`, `Sql/rls_policies.sql`, `Entities/Membership.cs`, `Modules.Ledger/*`, `Endpoints/MeEndpoints.cs`, `Program.cs`, `web/src/lib/api.ts`, `web/package.json`, `tests/*`, Phase-1 SUMMARYs 01-01…01-08 — build-ON facts.
- [Global Query Filters — EF Core (Microsoft Learn)](https://learn.microsoft.com/en-us/ef/core/querying/filters) — EF Core 10 named filters, `IgnoreQueryFilters([...])`.
- [What's New in EF Core 10 (Microsoft Learn)](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew) — named query filters feature.
- [NuGet FluentValidation 12.1.1](https://www.nuget.org/packages/fluentvalidation/) + [License.txt (Apache-2.0)](https://github.com/FluentValidation/FluentValidation/blob/main/License.txt) — still free.
- `.planning/research/{STACK,ARCHITECTURE,PITFALLS}.md` — locked stack + module layout + legal pitfalls.

### Secondary (MEDIUM — verified against a second source)
- [Named global query filters in EF Core 10 — Tim Deschryver](https://timdeschryver.dev/blog/named-global-query-filters-in-entity-framework-core-10) and [Milan Jovanović](https://milanjovanovic.tech/blog/named-query-filters-in-ef-10-multiple-query-filters-per-entity) — usage patterns, overwrite caveat.
- [EN16931 Business Terms (peppolvalidator.com)](https://peppolvalidator.com/en16931-business-terms) — BT-31/BT-44/BT-46/BT-48/BG-8 buyer fields.
- [BT-130 unit of measure code (invoice-converter.com)](https://www.invoice-converter.com/en/resources/xrechnung/bt-130-invoice-line-quantity-unit-of-measure-code) + EN16931 spec — UN/ECE Rec 20/21 required.
- [Germany VAT Number format & checksum (VAT-Scan)](https://www.vat-scan.com/country-guides/germany) — `DE`+9 digits, first ≠ 0; MOD 11,10 checksum; §18e BZSt qualified confirmation + retention.
- Official VIES REST endpoint `ec.europa.eu/taxation_customs/vies/rest-api/check-vat-number`; flakiness noted by third-party latency measurements (viesvat.com).
- [TanStack Table — Pagination guide](https://tanstack.com/table/latest/docs/guide/pagination) — `manualPagination`/`rowCount` server-side.

### Tertiary (LOW — validate at execution time)
- shadcn/ui + Tailwind v4 install specifics (CLI-driven, moving) — verify with `npx shadcn@latest`.
- Non-DE per-country VAT-ID checksum algorithms — scope-dependent.

---

## Metadata

**Confidence breakdown:**
- Build-on-Phase-1 mechanics (modules, RLS-per-table, named filters, endpoints): **HIGH** — read the actual code and migrations.
- EN 16931 field mapping + unit codes + VAT-ID format: **HIGH/MEDIUM-HIGH** — multiple standard sources.
- VIES operational choice + §18e seam: **MEDIUM** — facts verified; the "offline-only in v1" call is a recommendation.
- Frontend library specifics (shadcn/Tailwind v4 versions/steps): **MEDIUM** — fast-moving.

**Research date:** 2026-07-10
**Valid until:** ~2026-08-10 for library specifics (EF Core 10 / TanStack / shadcn); EN 16931 field IDs and German VAT rules are stable longer.
