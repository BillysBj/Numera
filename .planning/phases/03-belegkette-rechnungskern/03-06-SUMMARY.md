---
phase: 03-belegkette-rechnungskern
plan: 06
subsystem: api
tags: [dotnet, minimal-api, ef-core, storno, gutschrift, credit-note, en16931, gobd]

# Dependency graph
requires:
  - phase: 03-05
    provides: "finalize machinery (snapshots + BG-23 breakdown + NumberingService + open item + status-flipped-last), InvoiceCancelled/CreditNoteIssued event records, FinalizeValidation §14 gate"
  - phase: 03-02
    provides: "polymorphic sales_documents schema + status-guarded immutability triggers (whitelist: status, cancelled_by_document_id) + open_items"
  - phase: 03-04
    provides: "SalesDocumentEndpoints (/api/documents surface, copy-forward convert, NonDraftConflict guard, SalesDocumentAuditEvent)"
provides:
  - "POST /api/documents/{id}/storno — finalized negative-mirror Storno (type 384) with its own number, cancels the original (whitelist-only) and closes its open item"
  - "POST /api/documents/{id}/credit-note — Gutschrift (type 381) finalizable draft referencing the original, positive amounts, no positive receivable on finalize"
  - "FinalizeCoreAsync — shared finalize path reused by finalize (03-05) and storno"
  - "StornoResponse DTO"
affects: [03-08, 03-09, "phase-05-erechnung"]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Extract-a-core: FinalizeCoreAsync is the single finalize path (snapshots + breakdown + own-series number + open-item-for-Rechnung-only + status-flipped-last), called by both finalize and storno inside the caller's transaction; the caller owns BeginTransaction/Commit + the post-commit event"
    - "Correction-as-new-document: a Storno/Gutschrift is a NEW finalizable SalesDocument (own number series, own immutability) referencing the original via corrects_document_id — the original is never edited in place, only its whitelisted lifecycle columns change"

key-files:
  created: []
  modified:
    - "src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs — FinalizeCoreAsync extraction + /storno + /credit-note"
    - "src/Numera.Api/Contracts/SalesDocumentContracts.cs — StornoResponse DTO"

key-decisions:
  - "Storno is a full negative mirror: lines copied forward with NEGATED quantity + line net (same tax category/rate → a negated BG-23 breakdown and negative totals); finalized via the SAME FinalizeCoreAsync so it gets its own Storno-series number"
  - "The original invoice is mutated with ONLY whitelisted columns (status=Cancelled, cancelled_by_document_id=storno.id) so the sales_document_immutable trigger passes; its open item is closed (status=Cancelled, open_amount=0) in the same transaction"
  - "Storno re-runs the §14 FinalizeValidation gate (it is itself a legal document) and does NOT create a positive open item (open item is Rechnung-only in FinalizeCoreAsync)"
  - "Gutschrift (kaufmännische, type 381) is a POSITIVE-amount finalizable DRAFT (RESEARCH.md Pattern 6 — not a negative invoice), left for the user to edit/finalize via the normal /finalize; the self-billed VAT Gutschrift (389) is explicitly out of scope for v1"
  - "credit-note 409s when the source is a draft or not a Rechnung; storno 409s unless the source is a finalized/sent Rechnung"

patterns-established:
  - "FinalizeCoreAsync: the single finalize code path shared by finalize + storno"
  - "InvoiceCancelled dispatched AFTER commit; the open-item close + audit happen INSIDE the storno transaction"

# Metrics
duration: 16min
completed: 2026-07-13
---

# Phase 3 Plan 06: Storno + Gutschrift Correction Documents Summary

**Correction documents over a shared finalize core: POST /storno issues a finalized negative-mirror Storno (384) with its own number that cancels the original (whitelist-only) and closes its open item; POST /credit-note issues a Gutschrift (381) finalizable draft referencing the original — the original stays DB-immutable.**

## Performance

- **Duration:** ~16 min
- **Completed:** 2026-07-13
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Extracted `FinalizeCoreAsync` from 03-05's finalize endpoint — the single finalize path (freeze issuer + recipient jsonb, persist the BG-23 breakdown + frozen totals, assign the race-safe number from the type's OWN series, create the open item for a Rechnung ONLY, flip status LAST) — and re-wired /finalize to call it with zero behavior change.
- `POST /api/documents/{id}/storno`: builds a Draft Storno (type 384) that is a negated mirror of the original (negated quantity + line net → negated breakdown + totals), finalizes it via the shared core (own Storno-series number, no open item), then in the SAME transaction flips the original to Cancelled with a back-link and closes its open item (status Cancelled, open_amount 0). Dispatches `InvoiceCancelled` after commit. Returns 201 + `StornoResponse{id, documentNumber}`.
- `POST /api/documents/{id}/credit-note`: creates a Gutschrift (type 381) Draft referencing the original via `corrects_document_id`, lines copied forward with POSITIVE amounts, to be edited + finalized via the normal /finalize (which assigns a Gutschrift-series number and creates no positive receivable). Returns 201 + id.
- The original invoice is never edited in place — only its whitelisted lifecycle columns change, so the DB `sales_document_immutable` trigger is never tripped.

## Task Commits

Each task was committed atomically:

1. **Task 1: POST /{id}/storno — negative mirror, own number, cancel original** - `0803b56` (feat)
2. **Task 2: POST /{id}/credit-note — Gutschrift (381) draft referencing the original** - `7b302cf` (feat)

**Plan metadata:** committed with STATE.md (docs: complete plan)

## Files Created/Modified
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` - `FinalizeCoreAsync` extraction + `DuplicateNumberConflict` helper; new `/storno` (finalized negative mirror + original cancellation + open-item close + InvoiceCancelled) and `/credit-note` (Gutschrift draft) endpoints.
- `src/Numera.Api/Contracts/SalesDocumentContracts.cs` - `StornoResponse(Guid Id, string DocumentNumber)` DTO.

## Decisions Made
- **Storno = full negative mirror finalized via the shared core.** Copying the lines with negated quantity + line net yields a negated BG-23 breakdown and negative totals for free (rounding is symmetric — RoundTax away-from-zero negates cleanly). The Storno draws its OWN number from the Storno series and is itself immutable once finalized.
- **Original mutated whitelist-only.** Only `status → Cancelled` and `cancelled_by_document_id` change on the original (both on the trigger whitelist), plus its open item is closed (`Cancelled`, `open_amount = 0`) — no positive receivable survives a cancellation (RESEARCH.md Pattern 5).
- **Storno re-runs the §14 gate.** A Storno is itself a legal document, so it re-runs `FinalizeValidation.Check` (422 on incomplete issuer/recipient) — guards against master data deleted since the original was issued.
- **Gutschrift is a positive-amount draft (381), not a negative invoice.** Per RESEARCH.md Pattern 6 the commercial credit note carries positive amounts; it is a finalizable draft the user edits (often partial) before finalizing. The self-billed VAT Gutschrift (389) is explicitly out of scope for v1 (documented in a code comment).
- **Open item is Rechnung-only in `FinalizeCoreAsync`.** This was already true in 03-05; keeping the rule in the shared core means Storno and Gutschrift never create a positive receivable without any extra branching.

## Deviations from Plan

None - plan executed exactly as written.

The plan's frontmatter listed `SalesDocumentValidators.cs` among files_modified, but neither correction endpoint takes a request body (both act on the route id only), so no new validator was required — the §14 gate is reused from 03-05 for the Storno. No file was touched unnecessarily.

## Issues Encountered
None. Build green (0 warnings, 0 errors); full suite 54 platform + 47 integration green (no new automated tests — API-only plan mirroring 03-04/03-05; the Storno/credit-note DB behaviors — negative mirror, whitelist-only original mutation, open-item close, own-series numbering — are covered by the 03-08 finalize/correction integration tests).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- INV-03 + the correction half of DOCS-04 are delivered: a user can cancel a finalized invoice (Storno) and issue a credit note (Gutschrift), each numbered from its own series and referencing the original, while the original stays DB-immutable and its open item is closed.
- 03-08 (finalize/correction integration tests, DOCS-04) can now assert: Storno own-number + negative mirror, original Cancelled + back-link + open item closed, original business columns provably unchanged (trigger not tripped), Gutschrift draft → 381-series number on finalize with no positive open item.
- 03-09 (document detail + finalize/storno/credit-note UI) now has both correction backends to call.

## Self-Check: PASSED

- Commits `0803b56` (Task 1 storno) and `7b302cf` (Task 2 credit-note) present in history.
- `POST /{id}/storno`, `POST /{id}/credit-note`, `FinalizeCoreAsync`, and `StornoResponse` all present in source.
- Build green (0 warnings / 0 errors); 54 platform + 47 integration tests green.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*
