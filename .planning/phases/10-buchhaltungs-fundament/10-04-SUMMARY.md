---
phase: 10-buchhaltungs-fundament
plan: 04
subsystem: ledger
tags: [ledger, invoice, posting, storno, gobd]

requires:
  - phase: 10-03
    provides: PostingEngine + InvoicePostingSource + AccountResolver
  - phase: 10-02
    provides: per-tenant chart + ledger_settings
provides:
  - "Automatic balanced booking of finalized receivable invoices (debtor / revenue / USt)"
  - "Generalumkehr (reversing booking) on Storno, atomic with cancellation"
affects: [ledger-reads, festschreibung, ustva, reports]

tech-stack:
  added: []
  patterns:
    - "Inline posting inside the finalize transaction (atomic legal projection)"
    - "Graceful skip when a tenant has no LedgerSettings (pre-ledger v1 tenants)"

key-files:
  created:
    - tests/Numera.IntegrationTests/LedgerInvoicePostingTests.cs
  modified:
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs

key-decisions:
  - "Post inline in FinalizeCoreAsync (atomic) rather than via the InvoiceFinalized event"
  - "Only receivable invoice types book; commercial Gutschrift booking is out of scope for Phase 10"
  - "Storno posts an exact mirror-reversal of the persisted original legs"
  - "Idempotent on (SourceType=Invoice, SourceRef=doc.Id); backward-compatible FinalizeCoreAsync overload"

patterns-established:
  - "Auto-posting hooks resolve ChartVariant via TryResolveChartVariant and skip gracefully without ledger setup"

completed: 2026-08-03
---

# Phase 10-04: Auto-booking of finalized invoices

**A finalized receivable invoice now creates its balanced Soll/Haben booking (Debitor / Erlöse / USt) atomically inside the finalize transaction; a Storno produces a Generalumkehr — never an edit.**

## Accomplishments

- Posted the invoice booking inline in `FinalizeCoreAsync` from the frozen `SalesDocumentTaxBreakdown` (one revenue+USt leg pair per breakdown row; single debtor leg = gross), honoring the partner `DebtorAccount` override.
- Restricted booking to receivable invoice types (`IsReceivableInvoice`); commercial Gutschrift booking left out of scope per plan.
- Added a `TryResolveChartVariant` graceful skip so tenants without `LedgerSettings` still finalize (no hard dependency on ledger setup for existing v1 tenants).
- Made booking idempotent on `(SourceType=Invoice, SourceRef=doc.Id)`.
- Posted a Generalumkehr in the Storno transaction (mirror-reversal of the persisted original legs, `PostingType=Storno`, `ReversesEntryId` set).
- Kept the post-commit `InvoiceFinalized` dispatch unchanged; added a backward-compatible `FinalizeCoreAsync` overload.

## Verification

- `dotnet build Numera.sln --configuration Release --no-restore`: 0 warnings, 0 errors (SDK 10.0.301).
- Integration suite (Testcontainers, least-privilege numera_app): 17/17 pass — `LedgerInvoicePostingTests` (19%; split 19%+7%; atomicity rollback leaves no JournalEntry; Storno reversal nets to zero; tenant without ledger_settings finalizes with no booking; idempotency) plus existing `SalesFinalizeTests` + `SalesStornoTests` with no regression.

## Deviations from Plan

None. The `must_haves` Gutschrift-booking truth was already removed during planning revision; commercial Gutschrift booking stays out of scope.

## Issues Encountered

None.

---
*Phase: 10-buchhaltungs-fundament*
*Completed: 2026-08-03*
