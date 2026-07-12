---
phase: 03-belegkette-rechnungskern
plan: 04
subsystem: api
tags: [minimal-api, sales-documents, draft-lifecycle, document-chain, open-items, fluentvalidation, audit, rls]

# Dependency graph
requires:
  - phase: 03-02
    provides: "sales_documents polymorphic schema + lines/breakdown children + open_items + DB immutability triggers (Draft=0 guard)"
  - phase: 03-01
    provides: "Numera.Modules.Sales project wired into Api; mutate->audit->single-SaveChanges idiom source"
  - phase: 02-05
    provides: "CATL-02 CatalogLineItem picker seam the client snapshots onto lines"
provides:
  - "GET/POST/PUT/DELETE /api/documents draft lifecycle with a 409 app-guard mirroring the DB immutability trigger"
  - "POST /api/documents/{id}/convert copy-forward chain (Angebot -> AB -> Lieferschein -> Rechnung) setting source_document_id"
  - "GET /api/documents list (type/status/paged) + detail (header + lines + breakdown + chain links)"
  - "GET /api/open-items OP-Uebersicht paged list (due-date sorted, overdue flag) — read half of OPDN-01"
affects: [03-05, 03-06, 03-07, 03-08, 03-09, 03-10, finalize, storno, frontend-invoice-editor]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "App-layer 409 guard (Results.Problem 409) mirroring the DB status-guarded immutability trigger — a clean Conflict before the raw trigger exception (RESEARCH.md Pattern 2)"
    - "Draft preview: persist line nets + provisional TotalNet; TotalTax/TotalGross stay 0 until finalize computes the BG-23 breakdown (plan 03-05)"
    - "Copy-forward convert: new Draft, fresh line ids, same snapshot fields + order, source_document_id set — no number/status/totals-as-final copied"
    - "Delete-and-re-add lines on PUT (parent is Draft so the child trigger permits it)"

key-files:
  created:
    - "src/Numera.Api/Contracts/SalesDocumentContracts.cs"
    - "src/Numera.Api/Contracts/OpenItemContracts.cs"
    - "src/Numera.Api/Validators/SalesDocumentValidators.cs"
    - "src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs"
    - "src/Numera.Api/Endpoints/OpenItemEndpoints.cs"
  modified:
    - "src/Numera.Api/Program.cs"

key-decisions:
  - "The PUT/DELETE non-draft guard is an app-layer 409 (Results.Problem, StatusCodes.Status409Conflict) mirroring the DB sales_document_immutable trigger, so a user editing a finalized doc gets a clean Conflict instead of a raw Postgres trigger exception (RESEARCH.md Pattern 2)"
  - "Drafts persist line LineNetAmount = round(qty x price, 4) half-away-from-zero + a provisional TotalNet; TotalTax/TotalGross/AmountDue stay 0 for drafts — the VAT breakdown + final totals are computed only at finalize (03-05) via VatCalculationService"
  - "convert accepts ANY target type from ANY source status (a finalized Angebot can become a Rechnung draft) — RESEARCH.md permits free conversion in v1; only source-not-found (RLS-scoped) returns 404"
  - "Lines cross the wire already carrying the catalog snapshot (name/unit/net price/tax category/rate); CatalogItemId is provenance only and the server never re-reads the catalog (RESEARCH.md Pitfall 2 / the CATL-02 seam)"
  - "open_items endpoint is read-only (paged, due-date sorted, overdue = dueDate<today && status Open/PartiallyPaid); creation is finalize (03-05), payment recording is Phase 6 — neither exposed here"

patterns-established:
  - "Sales-document endpoints mirror CatalogEndpoints exactly: MapGroup('/api/documents').RequireAuthorization(), an internal SalesDocumentAuditEvent : IAuditEvent adapter, mutate->audit->single-SaveChanges"
  - "Validators auto-register via AddValidatorsFromAssemblyContaining<Program>() — zero DI edits; both new endpoint groups registered with the sole wave-3 Program.cs edit"

# Metrics
duration: 15min
completed: 2026-07-12
---

# Phase 3 Plan 04: Sales-Document HTTP Surface (Drafts + Chain + OP-Übersicht) Summary

**The sales-document API without finalize: draft create/edit/delete (with a 409 app-guard mirroring the DB immutability trigger), the copy-forward `convert` chain (Angebot → AB → Lieferschein → Rechnung), list + detail reads, and the read-only OP-Übersicht list — DOCS-01 and the read half of OPDN-01, ready for finalize to layer on.**

## Performance

- **Duration:** ~15 min
- **Completed:** 2026-07-12
- **Tasks:** 3
- **Files modified:** 6 (5 created, 1 modified)

## Accomplishments
- `SalesDocumentContracts` + `OpenItemContracts`: create/update/line request DTOs, list/detail projections and the OP list item (enums serialize as NUMBERS per the Catalog convention; money-adjacent fields are exact `decimal`). Each line already carries the catalog snapshot from the client — `CatalogItemId` is provenance only.
- `SalesDocumentEndpoints` (`/api/documents`, `.RequireAuthorization()`): paged/filterable list (type/status/q), detail with `.Include` of lines + breakdown + chain link ids, `POST` create-draft (snapshotted lines, 1-based `LineNumber`, `LineNetAmount = round(qty×price,4)` half-away-from-zero, provisional `TotalNet`), `PUT`/`DELETE` gated by an app-layer **409** when `Status != Draft` (mirrors the DB trigger), and `POST /{id}/convert` copy-forward (new Draft, fresh line ids, `SourceDocumentId` set). Follows the `mutate → audit → single-SaveChanges` idiom with an internal `SalesDocumentAuditEvent : IAuditEvent`.
- `SalesDocumentValidators`: at-least-one-line + per-line rules (name NotEmpty, quantity > 0, net price ≥ 0, unit NotEmpty, VAT rate in [0,100]) via a nested `SalesLineRequestValidator`; auto-scanned (no DI edit).
- `OpenItemEndpoints` (`/api/open-items`): paged, due-date-sorted, RLS-scoped list with `status`/`overdueOnly` filters and a server-computed `overdue` flag. Read-only.
- `Program.cs`: registered `MapSalesDocumentEndpoints()` + `MapOpenItemEndpoints()` after `MapCompanyProfileEndpoints()` — the sole wave-3 `Program.cs` edit.

## Task Commits

Each task was committed atomically:

1. **Task 1: Contracts + draft CRUD (create/update/delete/list/get) with the 409 guard** - `4b04b99` (feat)
2. **Task 2: Copy-forward convert endpoint (the chain)** - `80d6258` (feat)
3. **Task 3: OP-Übersicht list endpoint + register both endpoint groups** - `e090cb8` (feat)

## Files Created/Modified
- `src/Numera.Api/Contracts/SalesDocumentContracts.cs` - create/update/line requests, convert request, list/detail + line/breakdown DTOs
- `src/Numera.Api/Contracts/OpenItemContracts.cs` - `OpenItemListItem` with server-computed overdue flag
- `src/Numera.Api/Validators/SalesDocumentValidators.cs` - draft + per-line validation
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` - `MapSalesDocumentEndpoints` (CRUD + convert + list/get)
- `src/Numera.Api/Endpoints/OpenItemEndpoints.cs` - `MapOpenItemEndpoints` (OP-Übersicht list)
- `src/Numera.Api/Program.cs` - registered both endpoint groups

## Decisions Made
- App-layer 409 guard mirrors the DB immutability trigger (clean Conflict, not a raw trigger exception).
- Drafts persist line nets + provisional `TotalNet`; tax/gross totals stay 0 until finalize.
- `convert` accepts any target type from any source status; only 404 on source-not-found.
- Lines carry the catalog snapshot from the client; the server never re-reads the catalog.
- The open-items endpoint is read-only (creation is finalize, payments are Phase 6).

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None - no auth gates encountered.

## Issues Encountered
None. `dotnet build Numera.sln` green (0 warnings, 0 errors); full suite green (54 platform + 47 integration = 101 tests) on real postgres:18 via Testcontainers.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The draft lifecycle + chain + reads are the API foundation plan 03-05 (finalize) builds on: finalize must snapshot `company_profile` (issuer) + partner onto the doc, compute the BG-23 breakdown via `VatCalculationService`, assign the number via an upsert-returning on `number_sequences`, create the `open_item`, then flip `status` LAST (the child trigger permits pre-flip child writes).
- The `convert` endpoint realizes DOCS-01 (the document chain); Storno/Gutschrift correction documents (`CorrectsDocumentId`/`CancelledByDocumentId`) land in later plans.
- The frontend invoice-line editor imports `lookupCatalogItems(?picker=true)` (CATL-02) and snapshots `CatalogLineItem` onto `SalesLineRequest` lines.

## Self-Check: PASSED

All 6 claimed files exist on disk; all 3 task commits (4b04b99, 80d6258, e090cb8) are present in git history.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-12*
