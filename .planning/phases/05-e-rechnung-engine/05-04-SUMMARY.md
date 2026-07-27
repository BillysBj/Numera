---
phase: 05-e-rechnung-engine
plan: 04
subsystem: sales + api
tags: [zugferd, factur-x, pdfa3, en16931, xrechnung, cii, einv-02, questpdf, e-invoice]

# Dependency graph
requires:
  - phase: 05-e-rechnung-engine (05-01)
    provides: XRechnungGenerator.GenerateCiiForZugferd (the EN 16931 / XRECHNUNG CII path) + EInvoiceFormat enum + EInvoiceMapper (one frozen model → one descriptor)
  - phase: 05-e-rechnung-engine (05-03)
    provides: EInvoiceService (GenerateAndValidate / GetOrGenerate / FinalizeFormats), document_einvoice store, GenerateEInvoiceJob iterating FinalizeFormats, EInvoiceEndpoints group
  - phase: 04-pdf-versand (04-02/04-03)
    provides: InvoiceDocument §14 QuestPDF layout + SnapshotReader + the DocumentPdfService live-logo read pattern
provides:
  - "InvoiceDocument.RenderPdfA(model): the byte-unchanged §14 layout rendered as PDF/A-3b via GetSettings → PDFA_Conformance.PDFA_3B (core QuestPDF, no extra package)"
  - "ZugferdGenerator.Generate(model): PDF/A-3b base + embedded factur-x.xml CII (AF Source) + ZUGFeRD XMP — the ZUGFeRD/Factur-X hybrid (EINV-02)"
  - "EInvoiceService.FinalizeFormats now [Ubl, Cii, ZugferdPdfA3] — the finalize job eagerly persists all three formats with NO GenerateEInvoiceJob edit"
  - "EInvoiceService.GenerateZugferdAsync: live logo read + ZugferdGenerator + reuse of the byte-identical CII's KoSIT verdict (no double-validation)"
  - "GET /api/documents/{id}/zugferd: render-if-absent → application/pdf ({number}-zugferd.pdf); 404 unknown, 409 Draft"
affects: [05-05 (inbound may extract embedded factur-x.xml from these PDFs)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "PDF/A-3b via IDocument.GetSettings() returning PDFA_Conformance.PDFA_3B — the legacy DocumentSettings.PdfA bool is deprecated (would break TreatWarningsAsErrors); the conformance enum is the current API"
    - "DocumentOperation is file-based (qpdf): write base PDF + CII to a private temp dir, LoadFile → AddAttachment → ExtendMetadata → Save, read back, best-effort cleanup"
    - "EINV-02 is STRUCTURAL: ONE frozen InvoicePdfModel → PDF/A base (RenderPdfA) + embedded CII (GenerateCiiForZugferd) — identical values by construction, not reconciled"
    - "Reuse the byte-identical CII's stored KoSIT verdict for the ZUGFeRD artifact rather than re-validate identical bytes (CII precedes ZUGFeRD in FinalizeFormats)"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/EInvoice/ZugferdGenerator.cs
    - tests/Numera.Platform.Tests/Sales/ZugferdGeneratorTests.cs
  modified:
    - src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs
    - src/Numera.Api/Services/EInvoiceService.cs
    - src/Numera.Api/Endpoints/EInvoiceEndpoints.cs

key-decisions:
  - "PDF/A-3b is produced by CORE QuestPDF 2026.7.1 (DocumentSettings.PDFA_Conformance + DocumentOperation.AddAttachment/ExtendMetadata) — RESEARCH Open Question 1 RESOLVED: no QuestPDF.ZUGFeRD, no iText/AGPL, no new PDF package"
  - "Used PDFA_Conformance.PDFA_3B (not the deprecated DocumentSettings.PdfA bool) so the project's TreatWarningsAsErrors stays clean; GetSettings inherits DocumentSettings.Default and only adds the conformance level, leaving the §14 print layout byte-unchanged"
  - "EINV-02 anchored structurally: ZugferdGenerator embeds XRechnungGenerator.GenerateCiiForZugferd(model) — the SAME serialization path as the standalone XRechnung CII — so the printed page and the embedded XML are byte-identical, proven by the extraction test"
  - "ZUGFeRD artifact reuses the standalone CII's KoSIT verdict (byte-identical embedded CII) instead of a second KoSIT call; falls back to validating the CII only if no CII artifact exists yet"
  - "GET /{id}/zugferd serves application/pdf ({number}-zugferd.pdf) — the hybrid is a valid e-invoice AND a human-readable PDF, one download for both; no Program.cs/migration change (disjoint from 05-05)"

patterns-established:
  - "Pattern: ZUGFeRD embed reuses the ONE CII path (GenerateCiiForZugferd) — the hybrid and the standalone XRechnung can never diverge"
  - "Pattern: extract an embedded PDF file with no PDF library by scanning stream…endstream bodies and FlateDecode-inflating each (ZUGFeRD-csharp 18 cannot read a PDF)"

metrics:
  duration: ~35 min
  tasks: 3
  files: 5
  completed: 2026-07-27
---

# Phase 5 Plan 04: ZUGFeRD PDF/A-3 Summary

**One-liner:** A finalized invoice renders as a ZUGFeRD/Factur-X PDF/A-3b — the unchanged §14 QuestPDF layout tagged PDF/A-3b (core QuestPDF, no extra package) carrying the EN 16931/XRECHNUNG CII embedded as `factur-x.xml` (AF Source) — with the embedded XML provably byte-identical to the standalone XRechnung CII (EINV-02), persisted alongside UBL/CII on finalize and downloadable via `GET /api/documents/{id}/zugferd`.

## What shipped

**Task 1 — PDF/A-3 render + ZugferdGenerator (commit 1086dae).** Confirmed the QuestPDF 2026.7.1 API surface by reflection before coding: `DocumentSettings.PDFA_Conformance` (enum `None/PDFA_2A/2B/2U/3A/3B/3U`; the `PdfA` bool is deprecated) and the file-based `DocumentOperation.LoadFile → AddAttachment(DocumentAttachment{FilePath, AttachmentName, MimeType, Relationship, dates}) → ExtendMetadata(xmp) → Save`, with `DocumentAttachmentRelationship.Source`. Extended `InvoiceDocument` with `RenderPdfA` + a `GetSettings()` override that returns `DocumentSettings.Default` for the §14 path (byte-unchanged) and adds only `PDFA_Conformance.PDFA_3B` for the ZUGFeRD base. `ZugferdGenerator.Generate(model)` renders the PDF/A base, serializes the CII via `GenerateCiiForZugferd(model)` (the EINV-02 anchor — same frozen model), writes both to a private temp dir, runs `DocumentOperation` to embed `factur-x.xml` (Source) + the ZUGFeRD/Factur-X XMP (`DocumentType=INVOICE`, `ConformanceLevel=XRECHNUNG`), and reads back the result.

**Task 2 — folded into EInvoiceService + download (commit a6d76c0).** Appended `EInvoiceFormat.ZugferdPdfA3` to `EInvoiceService.FinalizeFormats` (now three entries) — `GenerateEInvoiceJob` iterates it, so the finalize job eagerly produces all three formats with NO job-file edit. `GenerateAndValidate` now branches to a new `GenerateZugferdAsync` for the ZUGFeRD format: reads the tenant logo LIVE (presentation only, exactly as `DocumentPdfService`), builds the model with the logo, calls `ZugferdGenerator.Generate`, and reuses the already-stored `XRechnungCii` artifact's KoSIT verdict (the embedded CII is byte-identical, so a second KoSIT call is redundant — CII precedes ZUGFeRD in `FinalizeFormats`), falling back to validating the embedded CII only if no CII artifact exists yet. `FileName` maps `ZugferdPdfA3 → {number}-zugferd.pdf`. Added `GET /api/documents/{id}/zugferd` to the existing `EInvoiceEndpoints` group (render-if-absent → `application/pdf`; 404 unknown, 409 Draft). No `Program.cs`/migration change.

**Task 3 — value-identity + conformance test (commit d7c6569).** 8 pure tests (mixed S 19/7% + §19 Kleinunternehmer fixtures, no DB/KoSIT): output is `%PDF` and larger than the plain `InvoiceDocument.Render`; embeds `factur-x.xml` with `/EmbeddedFile` + `/AFRelationship` Source + the PDF/A-3 `pdfaid` part-3 marker; and the EINV-02 proof — the embedded CII (extracted by scanning `stream…endstream` bodies and FlateDecode-inflating each, since ZUGFeRD-csharp 18 can only read XML not PDFs) is asserted byte-for-byte equal to `GenerateCiiForZugferd(model)`, and the embedded XML's summation totals equal the model's TotalNet/TotalTax/TotalGross/AmountDue.

## Deviations from Plan

**None affecting scope.** Two decisions worth recording (both within the plan's stated latitude to confirm the real API):

- **PDF/A API (RESEARCH Open Question 1 resolved):** The plan described `WithSettings(new DocumentSettings{ PdfA = true })`. In QuestPDF 2026.7.1 `DocumentSettings.PdfA` is **deprecated** ("use the ConformanceLevel property") and would break the solution's `TreatWarningsAsErrors`. Used the current `PDFA_Conformance = PDFA_Conformance.PDFA_3B` instead (PDF/A-3b is what ZUGFeRD requires for embedding). Confirmed against the shipped assembly by reflection + an empirical render (pdfaid part 3, conformance B present). No extra PDF package was added — PDF/A + attachment are core QuestPDF.
- **veraPDF:** Not wired into CI (heavyweight external tool). Per the plan's explicit allowance, the test asserts the PDF/A-3 XMP marker instead and leaves a documented `TODO(05-verify)` to add a veraPDF CI step. Stated clearly in the test class remarks.

## Verification

- `dotnet build src/modules/Numera.Modules.Sales` → 0 warnings; `dotnet build src/Numera.Api` (at Task-2 commit) → 0 warnings.
- `dotnet test tests/Numera.Platform.Tests --filter Zugferd` → 8/8 pass; full platform suite **95/95** (87 prior + 8 new), 0 warnings.
- `EInvoiceService.FinalizeFormats` now has 3 entries; the ZUGFeRD PDF embeds `factur-x.xml` (Source) byte-identical to the standalone CII; `GET /{id}/zugferd` serves the PDF.

## Coordination note (concurrent 05-05)

05-05 (inbound + frontend) runs in parallel and shares `src/Numera.Api`. At the time of writing, a full-solution build fails **solely** on 05-05's mid-flight untracked files (`src/Numera.Api/Services/InboundEInvoiceService.cs` references an as-yet-undefined `InboundDocumentAuditEvent`) — entirely outside this plan's scope. All 05-04 files compile in isolation (Sales module + Api both built 0 warnings; the platform test build, which references Numera.Api, succeeded) and all 05-04 tests pass. No file overlap with 05-05; no Program.cs/migration touched.

## Self-Check: PASSED

- Files: all 5 present (InvoiceDocument.cs, ZugferdGenerator.cs, EInvoiceService.cs, EInvoiceEndpoints.cs, ZugferdGeneratorTests.cs).
- Commits: 1086dae (Task 1), a6d76c0 (Task 2), d7c6569 (Task 3) all present.
