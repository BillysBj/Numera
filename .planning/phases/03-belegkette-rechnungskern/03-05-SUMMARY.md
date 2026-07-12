---
phase: 03-belegkette-rechnungskern
plan: 05
subsystem: api
tags: [finalize, numbering, en16931, vat, open-items, domain-events, postgres, ef-core, gobd]

# Dependency graph
requires:
  - phase: 03-01
    provides: CompanyProfile issuer master data (§14) snapshotted onto the invoice
  - phase: 03-02
    provides: sales_documents schema, number_sequences/document_number_formats, open_items, status-guarded immutability + child triggers, partial-unique document_number index
  - phase: 03-03
    provides: VatCalculationService (BG-23 bucketing) + Pflichttext + RoundingPolicy
  - phase: 03-04
    provides: /api/documents draft surface, SalesDocumentContracts/Validators, the app-layer 409 non-draft guard
provides:
  - "POST /api/documents/{id}/finalize — the core v1 transaction (INV-01/INV-02/OPDN-01)"
  - "NumberingService — atomic INSERT..ON CONFLICT..DO UPDATE..RETURNING per-(tenant,doc_type,year) counter"
  - "In-process domain-event seam (IDomainEventPublisher + InvoiceFinalized/InvoiceCancelled/CreditNoteIssued) — the ledger-readiness floor (Q8)"
  - "§14 finalize completeness gate (issuer, >=1 line, recipient address, AE/K require recipient VatId)"
affects: [03-06, 03-08, 04-pdf, 05-e-invoice, 06-payments]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Finalize = one BeginTransaction: breakdown + business columns written while status still Draft (first SaveChanges), status flipped LAST (second SaveChanges) so the child immutability trigger permits child writes (Pattern 4 / Pitfall 1)"
    - "Race-safe numbering via a single upsert-returning statement on a per-series counter row, run on the DbContext connection + ambient transaction (Pattern 3 — no SEQUENCE, no MAX+1)"
    - "In-process domain-event publisher resolving IDomainEventHandler<T> from scope; zero handlers = no-op seam (no MediatR)"
    - "Domain-event side-effects fired AFTER commit; authoritative work (open item, audit) stays inside the transaction"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/Numbering/NumberingService.cs
    - src/modules/Numera.Modules.Sales/Events/IDomainEventPublisher.cs
    - src/modules/Numera.Modules.Sales/Events/SalesDomainEvents.cs
  modified:
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
    - src/Numera.Api/Validators/SalesDocumentValidators.cs
    - src/Numera.Api/Program.cs

key-decisions:
  - "Numbering claim uses a raw ADO DbCommand (cmd.CreateParameter) enlisting the ambient transaction, not db.Database.SqlQuery<long> — EF's SqlQuery wraps the statement in a subquery and cannot execute an INSERT..RETURNING DML; the LOCKED ON CONFLICT single-statement semantics are unchanged"
  - "Open item is created only for DocumentType.Rechnung at finalize; Storno/Gutschrift cancel/negate logic is deferred to 03-06"
  - "Due date net days resolve partner.PaymentTermsNetDays -> company_profile.DefaultPaymentTermsNetDays -> 14"
  - "§14 completeness is a loaded-aggregate gate (FinalizeValidation.Check), not a request-body validator, since finalize takes only an id; returns 422 with per-field-group messages"

patterns-established:
  - "Finalize two-SaveChanges ordering: child rows + all frozen columns pre-flip, status flip last, audit before the final SaveChanges, all inside one BeginTransaction"
  - "Number-collision unique violation surfaced as a clean 409 (mirrors CatalogEndpoints.IsDuplicateNumber)"

# Metrics
duration: 22min
completed: 2026-07-13
---

# Phase 3 Plan 05: Invoice Finalize Summary

**POST /api/documents/{id}/finalize — one transaction that §14-validates, snapshots issuer+recipient as jsonb, persists the BG-23 VAT breakdown, assigns a race-safe INSERT..ON CONFLICT..RETURNING number in the configured format, creates the open item, flips status last, and dispatches InvoiceFinalized after commit.**

## Performance

- **Duration:** ~22 min
- **Tasks:** 3 (committed as 4 atomic commits for buildability)
- **Files modified:** 6 (3 created, 3 modified)

## Accomplishments
- `NumberingService`: the LOCKED Pattern-3 counter — a single `INSERT … ON CONFLICT (tenant_id, doc_type, year) DO UPDATE SET next_value = next_value + 1 RETURNING next_value - 1` run on the DbContext connection inside the finalize transaction; renders `{prefix}{YYYY-}{seq:0padding}` from `document_number_formats` with sane per-type defaults. No SEQUENCE, no MAX+1.
- The finalize endpoint: one `BeginTransactionAsync` doing §14 gate → jsonb issuer+recipient snapshots → BG-23 breakdown persist via `VatCalculationService` → totals via `RoundingPolicy` (net + Σ rounded breakdown tax) → atomic number → due date + `open_item` (Rechnung) → audit → status flipped LAST via a second `SaveChanges`; `InvoiceFinalized` dispatched AFTER commit.
- The domain-event seam: `IDomainEventPublisher` / `IDomainEventHandler<T>` + a tiny reflective `InProcessDomainEventPublisher` (zero handlers = no-op), plus `InvoiceFinalized` / `InvoiceCancelled` / `CreditNoteIssued` records — the Q8 ledger-readiness floor with no MediatR.
- The §14 completeness gate `FinalizeValidation.Check`: issuer profile (legal name, XOR VatId/TaxNumber, complete address), ≥1 line, recipient billing address, and AE/K lines require the recipient VatId — blocks with a 422 listing the missing groups.

## Task Commits

1. **Task 1: NumberingService (atomic upsert-returning counter)** - `b2be533` (feat)
2. **Task 2a: Domain-event seam (publisher + events)** - `49cea7e` (feat)
3. **Task 3: §14 finalize gate + publisher/numbering DI** - `8b3e6b5` (feat)
4. **Task 2b: POST /{id}/finalize (core transaction)** - `75b896a` (feat)

_Task 2 was split into the event seam (2a) and the endpoint (2b), with Task 3 committed between them, so every commit builds green (the endpoint depends on both the seam and the DI/validator)._

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/Numbering/NumberingService.cs` (created) - atomic race-safe number assignment + format rendering.
- `src/modules/Numera.Modules.Sales/Events/IDomainEventPublisher.cs` (created) - publisher/handler interfaces + `InProcessDomainEventPublisher`.
- `src/modules/Numera.Modules.Sales/Events/SalesDomainEvents.cs` (created) - `InvoiceFinalized`/`InvoiceCancelled`/`CreditNoteIssued`.
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` (modified) - added `POST /{id}/finalize` + issuer/recipient snapshot + open-item + duplicate-number helpers.
- `src/Numera.Api/Validators/SalesDocumentValidators.cs` (modified) - added `FinalizeValidation` §14 gate.
- `src/Numera.Api/Program.cs` (modified) - registered `NumberingService` + `IDomainEventPublisher`.

## Decisions Made
- **Numbering mechanism:** raw ADO `DbCommand` enlisting the ambient transaction rather than `db.Database.SqlQuery<long>`. EF Core's `SqlQuery` composes the SQL as a subquery (`SELECT … FROM (<sql>)`), which cannot execute an `INSERT … RETURNING` DML statement. The raw command runs the exact LOCKED single-statement upsert-returning on the same connection + transaction — the ON CONFLICT semantics and concurrency guarantee are identical; only the invocation API differs.
- **Open item scope:** only `Rechnung` creates a receivable at finalize; Storno/Gutschrift (which cancel/negate) are 03-06.
- **Due date:** `partner.PaymentTermsNetDays ?? company_profile.DefaultPaymentTermsNetDays ?? 14`.
- **§14 gate as an aggregate check** (not a body validator) returning 422 with per-field-group messages.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Numbering claim via raw DbCommand instead of `db.Database.SqlQuery<long>`**
- **Found during:** Task 1 (NumberingService)
- **Issue:** The plan/RESEARCH prescribed running the upsert-returning via `db.Database.SqlQuery<long>($"…")`. EF Core's `SqlQuery` wraps the supplied SQL in a subquery for composition, which is invalid for an `INSERT … ON CONFLICT … RETURNING` DML statement (Postgres cannot `SELECT FROM` an INSERT), so it would fail at runtime.
- **Fix:** Executed the identical single-statement upsert-returning through a raw `DbCommand` created from `db.Database.GetDbConnection()`, with `cmd.Transaction` set to the ambient `CurrentTransaction`, and provider-agnostic parameters via `cmd.CreateParameter()`. The LOCKED ON CONFLICT/DO UPDATE/RETURNING semantics and per-series serialization are preserved verbatim.
- **Files modified:** src/modules/Numera.Modules.Sales/Numbering/NumberingService.cs
- **Verification:** `dotnet build` green; solution build + full suite (54 platform + 47 integration) green; `git grep` confirms `ON CONFLICT` present and no `SEQUENCE`/`MAX(` in code.
- **Committed in:** b2be533 (Task 1 commit)

**2. [Rule 2 - Missing Critical] Due-date fallback through the issuer default**
- **Found during:** Task 2 (finalize endpoint)
- **Issue:** The plan's due-date formula used `partner?.PaymentTermsNetDays ?? 14`, ignoring the issuer's configured `CompanyProfile.DefaultPaymentTermsNetDays`, which would silently drop a tenant-level default when the partner has no term.
- **Fix:** Resolve `partner.PaymentTermsNetDays -> company_profile.DefaultPaymentTermsNetDays -> 14`.
- **Files modified:** src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
- **Verification:** Build + full suite green.
- **Committed in:** 75b896a (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 missing critical)
**Impact on plan:** The blocking fix is a mechanism substitution that preserves the LOCKED numbering semantics exactly; the due-date fallback honors existing issuer config. No scope creep.

## Issues Encountered
- Git Bash in this environment lacks coreutils and has neither git nor dotnet on PATH; used the user-local SDK 10 dir (`DOTNET_ROOT` + `DOTNET_MULTILEVEL_LOOKUP=0`) and the absolute Git for Windows path, per STATE.md. No impact on deliverables.

## Runtime verification note
No new automated tests were added in this plan (matching 03-04's API-only precedent); the DB-level finalize behaviors — number uniqueness, pre-flip child writes, immutability of the finalized row, and re-finalize 409 — are covered by the 03-08 integration suite (the plan's `<verify>` explicitly attributes the UPDATE-blocked/immutability check to 03-08). Build green; full suite 54 platform + 47 integration green.

## Next Phase Readiness
- 03-06 (Storno/Gutschrift): can consume `NumberingService` (per-type series), the `InvoiceCancelled`/`CreditNoteIssued` events, and the open-item cancel/negate seam.
- 03-08 (tests): finalize surface is ready for integration coverage (numbering race, snapshot freeze, immutability, 422/409 paths).
- Phase 4/5/6: `InvoiceFinalized` + the persisted breakdown/snapshots are the PDF/e-invoice/ledger inputs.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*

## Self-Check: PASSED

- All 3 created files present (NumberingService, IDomainEventPublisher, SalesDomainEvents).
- Modified files present (SalesDocumentEndpoints, SalesDocumentValidators, Program.cs).
- All 4 task commits exist: b2be533, 49cea7e, 8b3e6b5, 75b896a.
- Solution build green; full suite 54 platform + 47 integration green.
