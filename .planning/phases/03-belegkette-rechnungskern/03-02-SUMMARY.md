---
phase: 03-belegkette-rechnungskern
plan: 02
subsystem: database
tags: [postgres, ef-core, rls, triggers, gobd, immutability, en16931, sales-documents, numbering]

# Dependency graph
requires:
  - phase: 03-01
    provides: "Numera.Modules.Sales project (wired into sln/Api/tests) + CompanyProfile issuer master data"
  - phase: 01-plattform-kern
    provides: "FORCE-RLS pattern, numera_app no-BYPASSRLS role, PostgresFixture Testcontainers harness, Money/TaxCategory/RoundingPolicy, audit append-only trigger pattern"
provides:
  - "sales_documents polymorphic aggregate (document_type discriminator, status, nullable document_number, chain links, jsonb issuer/recipient snapshots, EN-16931 dates, persisted decimal totals)"
  - "sales_document_lines (BG-25) + sales_document_tax_breakdown (BG-23) children with parent-status immutability"
  - "number_sequences + document_number_formats (per-tenant numbering substrate) + open_items (offener Posten)"
  - "DB-enforced GoBD immutability: status-guarded parent trigger + parent-status child trigger (drafts mutable, finalized frozen)"
  - "partial unique (tenant, doc_type, document_number) making number reuse impossible"
affects: [03-04, 03-05, 03-06, 03-07, 03-08, 03-09, 03-10, finalize, numbering, open-items, storno, e-rechnung]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Status-guarded immutability trigger (not blanket REVOKE): drafts stay editable, finalized rows freeze business columns via IS DISTINCT FROM, whitelist lifecycle columns"
    - "Parent-status child trigger blocks INSERT/UPDATE/DELETE on lines/breakdown once parent is non-Draft (finalize writes children while parent still Draft, flips status last)"
    - "[ForeignKey] on a collection navigation binds it to the intended FK column, avoiding an EF shadow FK — annotation-only, no NumeraDbContext edit"
    - "Partial unique index for number-reuse prevention (no gapless logic — einmalig, not lückenlos)"

key-files:
  created:
    - "src/modules/Numera.Modules.Sales/SalesDocument.cs"
    - "src/modules/Numera.Modules.Sales/SalesDocumentLine.cs"
    - "src/modules/Numera.Modules.Sales/SalesDocumentTaxBreakdown.cs"
    - "src/modules/Numera.Modules.Sales/NumberSequence.cs"
    - "src/modules/Numera.Modules.Sales/DocumentNumberFormat.cs"
    - "src/modules/Numera.Modules.Sales/OpenItem.cs"
    - "src/modules/Numera.Modules.Sales/SalesEnums.cs"
    - "src/platform/Numera.Platform.Db/Migrations/20260712151258_SalesDocuments.cs"
    - "tests/Numera.IntegrationTests/SalesRlsTests.cs"
  modified:
    - "src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs"
    - "src/platform/Numera.Platform.Db/Sql/rls_policies.sql"

key-decisions:
  - "GoBD immutability is a STATUS-GUARDED trigger, not a blanket REVOKE (audit-style): drafts (status=0) stay fully mutable, only finalized rows freeze — a delete of a non-draft raises, and any change to a frozen business column (type, number, tenant, partner, snapshots, dates, totals, currency) raises via IS DISTINCT FROM"
  - "The child trigger keys off the PARENT status, so finalize can insert breakdown/lines while the parent is still Draft then flip status last (the child INSERT into a finalized parent is what gets blocked)"
  - "[ForeignKey(nameof(child.DocumentId))] on the Lines/TaxBreakdown collection navigations binds them to document_id; without it EF convention invents a shadow sales_document_id column (DocumentId does not match the {PrincipalType}Id convention). Annotation-only — NumeraDbContext untouched"
  - "document_number uniqueness is a PARTIAL unique index on (tenant_id, document_type, document_number) WHERE document_number IS NOT NULL — drafts carry NULL; number reuse is impossible with no brittle gapless logic"

patterns-established:
  - "Sales schema = ONE polymorphic sales_documents table (RESEARCH.md Pattern 1) + shared line/breakdown children"
  - "Every new sales table gets hand-written ENABLE+FORCE+tenant_isolation RLS in a foreach loop in the migration (6 tables), proven by one RLS test per table"

# Metrics
duration: 22min
completed: 2026-07-12
---

# Phase 3 Plan 02: Sales-Document Schema + DB Immutability Summary

**The polymorphic sales_documents schema (6 RLS-isolated tables) with DB-enforced GoBD immutability — a status-guarded parent trigger + parent-status child trigger freeze finalized documents while drafts stay editable, and a partial unique index makes invoice-number reuse impossible.**

## Performance

- **Duration:** ~22 min
- **Started:** 2026-07-12T16:58Z (approx.)
- **Completed:** 2026-07-12T17:20Z
- **Tasks:** 3
- **Files modified:** 11 (9 created, 2 modified)

## Accomplishments
- 6 sales entities + 3 enums (Draft=0 so the trigger keys off it): the polymorphic `SalesDocument` aggregate with jsonb issuer/recipient snapshots, chain links, EN-16931 dates and persisted `numeric(19,4)`/`(19,6)`/`(5,2)` money columns; lines (BG-25); tax breakdown (BG-23); numbering counter + format; open items.
- `_SalesDocuments` migration (#2 of Phase 3): 6-table RLS loop, the status-guarded `sales_document_immutable` parent trigger, the parent-status `sales_document_child_immutable` trigger on both children, and the 3 numbering unique indexes (partial on document_number). `Down()` fully reverses.
- `SalesRlsTests` (19 tests): per-table cross-tenant read isolation + WITH CHECK insert rejection for all 6 tables, fail-closed read, and the full DB-immutability proof (finalized UPDATE/DELETE/child-INSERT raise; whitelisted lifecycle UPDATE and all draft mutations succeed) — the DOCS-04 hard gate, green on real postgres:18 as `numera_app`.

## Task Commits

Each task was committed atomically:

1. **Task 1: Sales entities + enums** - `bc4f535` (feat)
2. **Task 2: _SalesDocuments migration (RLS + triggers + unique indexes)** - `2fb1119` (feat)
3. **Task 3: SalesRlsTests (isolation + immutability)** - `9ba157b` (test)

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/SalesEnums.cs` - DocumentType / DocumentStatus (Draft=0) / OpenItemStatus enums
- `src/modules/Numera.Modules.Sales/SalesDocument.cs` - polymorphic aggregate root; `[ForeignKey]` on the Lines/TaxBreakdown navigations binds them to `DocumentId`
- `src/modules/Numera.Modules.Sales/SalesDocumentLine.cs` - line (BG-25) snapshot
- `src/modules/Numera.Modules.Sales/SalesDocumentTaxBreakdown.cs` - persisted VAT breakdown (BG-23)
- `src/modules/Numera.Modules.Sales/NumberSequence.cs` - per-(tenant,doc_type,year) counter
- `src/modules/Numera.Modules.Sales/DocumentNumberFormat.cs` - per-(tenant,doc_type) number format
- `src/modules/Numera.Modules.Sales/OpenItem.cs` - offener Posten (Phase-6 payment seam)
- `src/platform/Numera.Platform.Db/Migrations/20260712151258_SalesDocuments.cs` - 6 tables + RLS + immutability triggers + unique indexes
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs` - snapshot with all 6 tables
- `src/platform/Numera.Platform.Db/Sql/rls_policies.sql` - documents the 6 sales tables + a note that the immutability triggers live in the migration
- `tests/Numera.IntegrationTests/SalesRlsTests.cs` - 19-test RLS + immutability hard gate

## Decisions Made
- Immutability enforced by a status-guarded trigger (drafts mutable, finalized frozen), not a blanket REVOKE — see key-decisions.
- Child trigger keys off parent status so finalize can write children pre-flip.
- Partial unique index for number-reuse prevention (no gapless logic).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] EF invented shadow FK columns on both child tables**
- **Found during:** Task 2 (migration scaffold)
- **Issue:** The first `dotnet ef migrations add` scaffold created a shadow `sales_document_id` column (plus its own FK + index) on `sales_document_lines` and `sales_document_tax_breakdown`. EF's relationship convention looks for a `{PrincipalType}Id` FK (`SalesDocumentId`); my intended `DocumentId` did not match, so EF built a duplicate FK instead of using `document_id`.
- **Fix:** Added `[ForeignKey(nameof(SalesDocumentLine.DocumentId))]` / `[ForeignKey(nameof(SalesDocumentTaxBreakdown.DocumentId))]` to the `Lines` / `TaxBreakdown` collection navigations on `SalesDocument` (annotation-only; NumeraDbContext untouched as the plan requires), removed the scaffold, restored the snapshot, and regenerated. The regenerated migration binds the FK to `document_id` (cascade delete) with no shadow column.
- **Files modified:** src/modules/Numera.Modules.Sales/SalesDocument.cs
- **Verification:** Regenerated migration shows `fk_sales_document_lines_sales_documents_document_id` on `document_id` only; `has-pending-model-changes` reports none; 19 tests green.
- **Committed in:** 2fb1119 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The fix was necessary to keep the schema on the intended `document_id` FK; no scope change.

## Issues Encountered
- The initial `dotnet ef migrations remove` reported a build failure (transient — a follow-up module build was clean); resolved by deleting the scaffold files manually and restoring the snapshot from git, then regenerating.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The polymorphic sales schema, its numbering substrate and DB-level immutability are the substrate for: DOCS-01 (the document chain), DOCS-04 (immutability — now DB-proven), INV-02 (race-safe numbering, whose counter row + format config now exist), and OPDN-01 (open items).
- Plan 03-05 (finalize) must sequence its unit-of-work per RESEARCH.md Pattern 4: insert breakdown/lines while the parent is still Draft, assign the number via an upsert-returning on `number_sequences`, then flip `status` LAST — the child trigger permits exactly that ordering.
- Coordination: this plan owns the schema + the sole Phase-3-wave-2 migration/snapshot; plan 03-03 (VAT service) ran in parallel touching only `Numera.Modules.Sales/Vat/` and `Numera.Platform.Tests` — no overlap.

## Self-Check: PASSED

All 10 claimed files exist on disk; all 3 task commits (bc4f535, 2fb1119, 9ba157b) are present in git history.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-12*
