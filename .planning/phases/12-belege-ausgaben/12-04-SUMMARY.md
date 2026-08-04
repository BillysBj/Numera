---
phase: 12-belege-ausgaben
plan: 04
subsystem: receipt-capture-pipeline
tags: [dotnet, hangfire, receipts, worm, malware-scanning, einvoice, rls]

requires:
  - phase: 12-belege-ausgaben
    plan: 02
    provides: Receipt aggregate, WORM archive, statuses, and deduplicator
  - phase: 12-belege-ausgaben
    plan: 03
    provides: Receipt extractor and attachment scanner ports with default doubles
provides:
  - "Shared scan-first Tier-B receipt ingest with immutable archive and post-commit extraction enqueue"
  - "RLS-safe Hangfire extraction job that produces Extracted review proposals with confidence"
  - "Authenticated receipt upload, list, detail, and original-download endpoints"
  - "Tier-A inbound e-invoice convergence into a linked Extracted receipt without OCR or duplicate archive"
affects: [12-05-confirm-book, 12-06-email-intake]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-04: Receipt capture pipeline

Plan 12-04 is implemented. Tier-B uploads now pass validation and malware scanning before any
archive write, clean originals are retained byte-for-byte in the WORM archive, and extraction is
queued only after the receipt/archive unit commits. Tier-A e-invoices now enter the same review
queue directly from their structural read model. Neither path books anything.

## Implemented

- Added scoped `ReceiptIngestService` with a 15 MiB cap and PDF/JPEG/PNG/TIFF/HEIF MIME/extension
  validation shared by uploads and future e-mail intake.
- Enforced scan-before-archive through `IAttachmentScanner`. Infected and scanner-error results
  fail closed into an audited `Quarantined` receipt with a content hash, no `receipt_archive`, no
  journal link, and no extraction enqueue.
- Added SHA-256 deduplication before persistence. Exact duplicate originals remain archived and
  visible as `Duplicate`; they are never silently discarded.
- Persisted a clean `ReceiptArchive` and linked `Receipt` in one `SaveChangesAsync`, preserving the
  supplied bytes byte-for-byte and recording filename, normalized MIME type, size, hash, source,
  receipt/user provenance, and receipt timestamp.
- Enqueued `ExtractReceiptJob(tenantId, receiptId)` only after persistence succeeds. Duplicate and
  quarantined review states do not enter OCR.
- Added `[AutomaticRetry(Attempts = 3)]` extraction job using the established fresh-scope plus
  `ICurrentTenant.SetTenant` pattern. It idempotently processes only `Captured`, reads the WORM
  archive under RLS, invokes `IReceiptExtractor`, maps decimal proposals, stores per-field
  confidence JSON (including null confidence for the manual-entry stub), supplier-matches without
  creating a partner, and advances only to `Extracted`.
- Added authenticated `/api/receipts` multipart upload, paged/status-filtered list, detail, and
  original-download endpoints. Tier-B originals stream from `receipt_archive`; Tier-A originals
  transparently stream from the linked immutable `inbound_document`.
- Added list/detail contracts exposing source/status ordinals, supplier/invoice fields, decimal
  amounts, confidence, partner match, filename, provenance, and original metadata.
- Extended `InboundEInvoiceService` to atomically add one linked `Receipt` in `Extracted`, populated
  from `InboundReadModel` seller, invoice, net/tax/gross, currency, date, and supplier match. It
  performs no OCR and creates no second archive.
- Registered `ReceiptDeduplicator`, `ReceiptIngestService`, and `ExtractReceiptJob`, and mapped the
  receipt endpoints beside inbound e-invoice endpoints.
- Added compile-verified real-Postgres integration coverage for benign archive/enqueue, direct stub
  extraction, EICAR quarantine, and ZUGFeRD Tier-A convergence with parsed amounts.

## Files created

- `src/Numera.Api/Services/ReceiptIngestService.cs`
- `src/Numera.Api/Jobs/ExtractReceiptJob.cs`
- `src/Numera.Api/Endpoints/ReceiptEndpoints.cs`
- `src/Numera.Api/Contracts/ReceiptContracts.cs`
- `tests/Numera.IntegrationTests/ReceiptIngestTests.cs`
- `.planning/phases/12-belege-ausgaben/12-04-SUMMARY.md`

## Files modified

- `src/Numera.Api/Services/InboundEInvoiceService.cs`
- `src/Numera.Api/Program.cs`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- API Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Integration-test project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Repository whitespace check for tracked changes: passed; Git reported only its existing LF-to-CRLF
  working-copy notices.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- `Receipt` has a dedicated `Duplicate` status but no separate duplicate flag. The ingest therefore
  uses that status and deliberately does not enqueue OCR for duplicates, because
  `ExtractReceiptJob` is required to process only `Captured` receipts.
- At capture time the requested ingest signature contains no supplier/invoice/date/amount fields.
  `ReceiptDeduplicator.CheckAsync` is still called exactly at capture, but only exact content-hash
  detection can produce a verdict there; business-key suspicion becomes possible only once those
  fields exist in a later review/extraction stage.
- `InboundReadModel` directly exposes `TotalNet`, `TotalTax`, and `TotalGross`, so Tier A mapped those
  values without adaptation. Multi-rate details remain on the linked inbound read model for the
  plan 12-05 booking proposal; the scalar receipt VAT rate is intentionally left null.
- The current production `AuditWriter` requires a non-null authenticated `ICurrentUser`. Uploads
  satisfy that existing endpoint convention, but plan 12-06's background e-mail caller will need an
  explicit system-actor policy before it can use the same audited ingest service; fabricating an
  actor in this plan would violate the audit contract.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left
  untouched.
