---
phase: 02-stammdaten
verified: 2026-07-12T09:51:30Z
status: passed
score: 4/4 must-haves verified
---

# Phase 2: Stammdaten Verification Report

**Phase Goal:** Nutzer kann seine Geschaeftspartner und sein Leistungsangebot pflegen - die Datenbasis, aus der sich spaeter jeder Beleg speist.
**Verified:** 2026-07-12T09:51:30Z
**Status:** passed
**Re-verification:** No - initial verification

## Build & Test Gate (ran, not trusted from SUMMARYs)

| Command | Result |
| --- | --- |
| dotnet --version | 10.0.301 (confirmed) |
| dotnet build Numera.sln -c Release | Succeeded, 0 warnings, 0 errors (13 projects) |
| dotnet test tests/Numera.Platform.Tests | 43/43 passed |
| dotnet test tests/Numera.IntegrationTests (real postgres:18 via Testcontainers, RLS hard gate) | 25/25 passed |
| cd web and npm run build (tsc -b + vite build) | Succeeded, 230 modules, no type errors |
| npx vitest run | 3 files / 22 tests passed (i18n, catalogSchema, partnerSchema) |

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Nutzer kann Kunden/Lieferanten mit Stammdaten (Anschrift, USt-ID, Zahlungsbedingungen, Kontakte) anlegen, bearbeiten, archivieren | VERIFIED | BusinessPartner.cs has IsCustomer/IsSupplier flags, owned BillingAddress/ShippingAddress, VatId (offline-validated by VatId.IsValidDe), PaymentTermsNetDays/SkontoPercent/SkontoDays. PartnerEndpoints.cs has full CRUD plus /archive and /unarchive (no MapDelete on the partner itself - only sub-resources contacts/notes have DELETE). Frontend PartnerFormPage.tsx/PartnerListPage.tsx/PartnerDetailPage.tsx are substantive (459/204/505 lines), wired via web/src/lib/api/partners.ts, routes registered in App.tsx. |
| 2 | Nutzer sieht pro Kunde/Lieferant eine Historie der zugehoerigen Belege und Aktivitaeten | VERIFIED (activities); documents correctly deferred | PartnerActivity entity plus partner_activities table; every mutation (create/update/archive/unarchive/note-added) calls AddActivity in the same SaveChangesAsync. GET /{id}/activities endpoint exists; PartnerDetailPage.tsx renders the timeline. Belege (documents) do not exist until Phase 3 - the detail page has an explicit Documents placeholder card with a hint text, which is the correct, documented seam (RESEARCH.md: timeline must work before invoices exist, fed by partner_activities not documents), not a stub of a Phase-2 deliverable. |
| 3 | Nutzer kann Notizen an Kunden/Lieferanten anheften | VERIFIED | PartnerNote entity plus partner_notes table (RLS-isolated). Add/edit/delete note endpoints in PartnerEndpoints.cs, each writes a PartnerActivity. PartnerDetailPage.tsx renders notes list with add/edit/delete UI wired to useMutation. |
| 4 | Nutzer kann Standardprodukte/-services mit Preis, Einheit, USt-Satz verwalten und als Positionen in Belege uebernehmen | VERIFIED | CatalogItem.cs: NetPrice/CostPrice as decimal mapped to numeric(19,4) (confirmed in migration column def, precision 19/scale 4), UnitOfMeasure = curated UN/ECE Rec 20 codes (C62, HUR, KGM, ...), TaxCategory plus VatRatePercent. CatalogEndpoints.cs has CRUD plus archive plus picker=true mode returning CatalogLineItem (CATL-02 seam). Frontend catalog.ts exports lookupCatalogItems() calling ?picker=true, explicitly documented as the function Phase-3's invoice line editor will import. |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
| --- | --- | --- | --- |
| src/platform/Numera.Platform.Db/IArchivable.cs | IArchivable marker | VERIFIED | Exists, used by BusinessPartner + CatalogItem |
| src/platform/Numera.Platform.Db/NumeraDbContext.cs | Named Tenant + NotArchived query filters | VERIFIED | TenantFilter = "Tenant", NotArchivedFilter = "NotArchived" constants; HasQueryFilter(TenantFilter, ...) and HasQueryFilter(NotArchivedFilter, ...) both called with explicit names (not the unnamed overload) |
| src/modules/Numera.Modules.Crm/BusinessPartner.cs | Partner entity w/ role flags, addresses, VAT, payment terms | VERIFIED | All fields present, ITenantEntity+IArchivable |
| src/platform/Numera.Platform.Db/Migrations/20260711022645_Crm.cs | CRM tables + hand-written RLS | VERIFIED | Loops partners, partner_contacts, partner_notes, partner_activities then ENABLE + FORCE ROW LEVEL SECURITY + CREATE POLICY tenant_isolation (USING + WITH CHECK) for each |
| tests/Numera.IntegrationTests/StammdatenRlsTests.cs | Per-table cross-tenant RLS proof | VERIFIED | 10 Fact tests, runs as numera_app (no BYPASSRLS) via shared PostgresFixture, uses IgnoreQueryFilters() no-arg so RLS is the sole control under test, covers all 4 CRM tables + archive-filter isolation |
| src/modules/Numera.Modules.Crm/VatId.cs | Offline USt-IdNr validator | VERIFIED | IsValidDe(string?) pure function, no network |
| src/modules/Numera.Modules.Catalog/CatalogItem.cs | Catalog entity, numeric(19,4), UN/ECE unit, tax category | VERIFIED | decimal NetPrice/CostPrice, TaxCategory, VatRatePercent |
| src/modules/Numera.Modules.Catalog/UnitOfMeasure.cs | UN/ECE Rec 20 curated code list | VERIFIED | C62, HUR, KGM, ... Codes list |
| src/platform/Numera.Platform.Db/Migrations/20260711094613_Catalog.cs | catalog_items table + RLS + unique index | VERIFIED | ENABLE/FORCE RLS + CREATE POLICY, ux_catalog_items_tenant_item_number partial unique index (archived_at IS NULL) |
| tests/Numera.IntegrationTests/CatalogRlsTests.cs | Cross-tenant RLS + unique-number proof | VERIFIED | 7 Fact tests incl. duplicate rejection, cross-tenant reuse allowed, archived reuse allowed |
| src/Numera.Api/Endpoints/PartnerEndpoints.cs | Partner CRUD/archive/contacts/notes/activities API | VERIFIED | MapPartnerEndpoints, atomic mutate+audit+activity, RFC 7807 validation via FluentValidation |
| src/Numera.Api/Endpoints/CatalogEndpoints.cs | Catalog CRUD/archive/picker API | VERIFIED | MapCatalogEndpoints, picker mode returns CatalogLineItem[] |
| src/Numera.Api/Contracts/CatalogContracts.cs | CatalogLineItem DTO | VERIFIED | Present, documented as CATL-02 seam |
| web/src/features/partners/* | List/Form/Detail pages, zod schema | VERIFIED | Substantive (459-505 lines each), TanStack Table server-side pagination, RHF+zod forms |
| web/src/features/catalog/* | List/Form pages, units map, zod schema | VERIFIED | Reuses shared DataTable, unit dropdown from units.ts |
| web/src/i18n/locales/de+en partners+catalog json | i18n namespaces | VERIFIED | Both exist, de is fallbackLng |

### Key Link Verification

| From | To | Via | Status | Details |
| --- | --- | --- | --- | --- |
| Crm migration Up() | RLS policies on 4 CRM tables | hand-written migrationBuilder.Sql | WIRED | Confirmed loop over all 4 tables, not reflective |
| Catalog migration Up() | RLS policy on catalog_items | hand-written migrationBuilder.Sql | WIRED | Confirmed |
| src/Numera.Api/Numera.Api.csproj | Numera.Modules.Crm / Numera.Modules.Catalog | ProjectReference | WIRED | Solution builds cleanly with both modules referenced |
| PartnerEndpoints.cs | IAuditWriter.RecordAsync + PartnerActivity insert | atomic single SaveChangesAsync | WIRED | Confirmed in create/update/archive/note handlers |
| Program.cs | MapPartnerEndpoints() / MapCatalogEndpoints() | endpoint mapping | WIRED | App builds and both endpoint groups registered |
| web/src/features/partners/PartnerListPage.tsx | /api/partners | TanStack Query, manualPagination | WIRED | keepPreviousData/placeholderData server-side paging confirmed |
| web/src/App.tsx | partner/catalog routes | react-router | WIRED | /partners, /partners/new, /partners/:id, /partners/:id/edit, /catalog, /catalog/new, /catalog/:id all registered |
| src/platform/Numera.Platform.Db/Sql/rls_policies.sql | migration SQL | reference doc mirrors migrations | WIRED | All 5 tables listed identically |
| web/src/lib/api/catalog.ts lookupCatalogItems | GET /api/catalog-items?picker=true | CATL-02 seam | WIRED | Present, documented for Phase-3 consumption |

### Requirements Coverage

| Requirement | Status | Notes |
| --- | --- | --- |
| CRM-01 | SATISFIED | Partner CRUD + archive, address/VAT/payment terms/contacts all present and tested |
| CRM-02 | SATISFIED (Phase-2 scope) | Activity timeline works; Belege portion correctly deferred to Phase 3 (no documents exist yet), documented seam not a gap |
| CRM-03 | SATISFIED | Notes add/edit/delete on partner, backed by RLS-isolated partner_notes |
| CATL-01 | SATISFIED | Catalog CRUD + archive, decimal price, UN/ECE unit, VAT rate |
| CATL-02 | SATISFIED | Picker endpoint + CatalogLineItem + frontend lookupCatalogItems export - the seam Phase 3 will consume |
| CRM-04 / CRM-05 | Correctly NOT built | No reminder/task or file-attachment entities in Numera.Modules.Crm - deferred to Phase 8 per roadmap, as instructed |

REQUIREMENTS.md tracker table still shows Pending for these rows - that is a roadmap bookkeeping field, not a code gap; it is updated separately by the orchestrator/roadmap-sync step.

### Anti-Patterns Found

None blocking. Grep for TODO/FIXME/XXX/HACK/PLACEHOLDER/not-implemented across all Phase-2 source and frontend files returned only:
- Build artifacts (obj/project.assets.json - irrelevant, not source)
- Legitimate uses of the word placeholder as an HTML input attribute / TanStack Query's placeholderData option
- One documented comment marking a deliberately deferred, out-of-phase feature (Documents card, see truth #2 above)

No stub return values (return null, empty div stand-ins, empty handlers) found in any endpoint or page file inspected.

### Human Verification Required

None required for automated gate - all must-haves are structurally and behaviorally verifiable via code + passing tests. Optional manual UX polish check (not blocking phase goal):

1. Visual layout/spacing of the partner detail page cards
   Test: Open /partners/:id in a browser and visually inspect card layout.
   Expected: Contacts/Notes/Timeline/Documents cards render cleanly, no overlap.
   Why human: Visual appearance cannot be verified via grep/build.

### Gaps Summary

No gaps found. All 7 plans' must-haves are backed by real, substantive, wired code:
- All 5 new tenant tables (partners, partner_contacts, partner_notes, partner_activities, catalog_items) have hand-written ENABLE+FORCE ROW LEVEL SECURITY+CREATE POLICY tenant_isolation in their migrations, confirmed by grep of the actual migration .cs files (not just the SUMMARY claims).
- NumeraDbContext uses named Tenant and NotArchived query filters (not unnamed, avoiding the silent-overwrite trap).
- No DELETE endpoint hard-deletes a partner or catalog item at the top level; only sub-resources (contacts/notes) have DELETE, which is correct per the plan's contract.
- Prices are decimal mapped to numeric(19,4) in the actual migration column definitions, never float.
- Catalog units are UN/ECE Rec 20 codes (C62, HUR, KGM, ...).
- The CATL-02 usable-as-invoice-line seam exists end-to-end: CatalogLineItem DTO, picker=true endpoint, and the frontend lookupCatalogItems() export documented for Phase-3 consumption.
- CRM-04/05 (tasks/reminders, file attachments) are correctly absent - no entities, no endpoints, no UI for either.
- Full build (dotnet build Numera.sln -c Release) succeeded with 0 warnings/0 errors; unit tests 43/43 passed; integration tests against real Postgres 25/25 passed (including the RLS hard gate StammdatenRlsTests and CatalogRlsTests); frontend tsc -b + vite build succeeded; frontend vitest run 22/22 passed.

---

_Verified: 2026-07-12T09:51:30Z_
_Verifier: Claude (gsd-verifier)_
