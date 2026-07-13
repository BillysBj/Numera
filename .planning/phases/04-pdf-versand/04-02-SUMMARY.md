---
phase: 04-pdf-versand
plan: 02
subsystem: rendering
tags: [questpdf, pdf, skiasharp, invoice, en16931, §14-ustg, i18n, culture, snapshot]

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern
    provides: "Finalized SalesDocument with frozen IssuerSnapshot/RecipientSnapshot jsonb + persisted Lines + BG-23 TaxBreakdown (incl. ExemptionReasonText Pflichttexte) + totals"
provides:
  - "SnapshotReader.FromDocument — stateless map of a finalized document's frozen snapshot → flat InvoicePdfModel (zero live master-data reads)"
  - "InvoiceDocument — one resource-driven QuestPDF §14 A4 layout rendering to byte[] in DE (default) or EN"
  - "PdfLabels — DE/EN label set, each carrying its formatting CultureInfo"
  - "InvoicePdfModel — presentation-ready render contract for the render job (04-03) and email job (04-04)"
affects: [04-03 (render job persists InvoiceDocument.Render output), 04-04 (email attaches the rendered PDF), 05-e-rechnung (PdfA/ZUGFeRD wraps the same Document.Create)]

# Tech tracking
tech-stack:
  added: [QuestPDF 2026.7.1 (Sales module; bundles SkiaSharp)]
  patterns:
    - "Render ONLY from the frozen snapshot — never live CompanyProfile/BusinessPartner (GoBD)"
    - "One resource-driven layout for both languages (no DE/EN layout drift)"
    - "Explicit CultureInfo from the label set drives all money/date formatting (never ambient culture)"
    - "Verbatim frozen Pflichttexte — stay German even under the English label set"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/Pdf/InvoicePdfModel.cs
    - src/modules/Numera.Modules.Sales/Pdf/SnapshotReader.cs
    - src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs
    - src/modules/Numera.Modules.Sales/Pdf/PdfLabels.cs
    - tests/Numera.Platform.Tests/Sales/InvoiceDocumentTests.cs
  modified:
    - src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj

key-decisions:
  - "SnapshotReader parses the frozen jsonb CASE-INSENSITIVELY — production freezes with JsonSerializerDefaults.Web (camelCase), not the PascalCase the plan assumed"
  - "QuestPDF added to the Sales MODULE (not the Api host) so the render unit is self-contained and host-agnostic; the community license is set by the caller/host, not the layout"
  - "English layout = English LABELS over verbatim German legal Pflichttexte (LOCKED); en-GB culture for label-set date order, EUR symbol formatted explicitly"
  - "No PdfA/ZUGFeRD introduced — the single Document.Create preserves the Phase-5 WithSettings(PdfA=true) seam"

patterns-established:
  - "PDF render unit as a pure package-level unit (no DbContext, no jobs) — unit-tested by rendering real snapshot fixtures to bytes"
  - "Assert on the render model + byte-stream validity (%PDF magic) + culture helper, never on rendered pixels"

# Metrics
duration: 9min
completed: 2026-07-13
---

# Phase 4 Plan 02: §14 Invoice PDF Render Unit Summary

**A finalized document's frozen snapshot (issuer/recipient jsonb + lines + BG-23 breakdown + totals) deterministically renders to a professional, culture-correct, §14-complete PDF byte[] in German or English via QuestPDF — the reusable render unit the render (04-03) and email (04-04) jobs will call.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-07-13T13:03:25Z
- **Completed:** 2026-07-13T13:12:49Z
- **Tasks:** 3
- **Files modified:** 6 (5 created, 1 modified)

## Accomplishments
- `SnapshotReader.FromDocument` maps a finalized `SalesDocument`'s frozen snapshot to a flat `InvoicePdfModel` with zero live master-data reads — stateless, host-agnostic, defensive case-insensitive jsonb parse.
- `InvoiceDocument` (QuestPDF `IDocument`) renders ONE resource-driven A4 §14 layout: logo/imprint header, recipient block, doc-meta band, lines table, totals, BG-23 VAT breakdown, verbatim frozen Pflichttexte, bank/payment block, imprint footer — DE (default) and EN.
- All money/dates/percentages format under the label set's explicit `CultureInfo` (de-DE default → `1.234,56 €` / `13.07.2026`), never ambient culture.
- 7 unit tests render real snapshot fixtures to valid PDF bytes (`%PDF`, >1KB) in DE + EN, prove the Pflichttext survives verbatim in both languages, and lock de-DE grouping. Full platform suite green (61 = 54 prior + 7 new).
- Phase-5 seam preserved: single `Document.Create`, no PdfA/ZUGFeRD package.

## Task Commits

Each task was committed atomically:

1. **Task 1: QuestPDF + SnapshotReader + InvoicePdfModel** - `3ff868f` (feat)
2. **Task 2: InvoiceDocument QuestPDF layout (DE/EN) + PdfLabels** - `3ce5228` (feat)
3. **Task 3: Unit tests — render fixtures to PDF bytes in DE + EN** - `e8d58c1` (test)

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj` - Added QuestPDF 2026.7.1 package reference.
- `src/modules/Numera.Modules.Sales/Pdf/InvoicePdfModel.cs` - Flat presentation record (issuer/recipient/header/lines/breakdown/totals/flags + logo + language).
- `src/modules/Numera.Modules.Sales/Pdf/SnapshotReader.cs` - Stateless jsonb-snapshot → model mapper; case-insensitive `System.Text.Json` parse; zero live reads.
- `src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs` - The QuestPDF §14 layout + culture-correct formatting + `static Render(model) → byte[]`.
- `src/modules/Numera.Modules.Sales/Pdf/PdfLabels.cs` - DE/EN label set, each with its `CultureInfo`.
- `tests/Numera.Platform.Tests/Sales/InvoiceDocumentTests.cs` - Render + model + culture assertions (Community license set once).

## Decisions Made
- **Case-insensitive snapshot parse.** The plan assumed the frozen jsonb was PascalCase, but `SalesDocumentEndpoints.SerializeIssuer/SerializeRecipient` serialize with `JsonSerializerDefaults.Web` → camelCase. `SnapshotReader` looks up properties case-insensitively so it correctly reads the real frozen production data (and tolerates either casing). See Deviations.
- **QuestPDF lives in the Sales module**, keeping the render unit self-contained and unit-testable in isolation; setting `QuestPDF.Settings.License = Community` is the host/caller's job (done in the test's static ctor here; Api host will do it in 04-03).
- **English = English labels over verbatim German Pflichttexte** (LOCKED); the exemption notes in `TaxBreakdown.ExemptionReasonText` are rendered verbatim regardless of label language.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Snapshot jsonb is camelCase, not PascalCase**
- **Found during:** Task 1 (SnapshotReader)
- **Issue:** The plan directed the reader to "tolerate the exact PascalCase shape emitted by SerializeIssuer/SerializeRecipient". The freezer actually uses `AuditJson = new(JsonSerializerDefaults.Web)`, so the frozen snapshot is camelCase (`legalName`, `vatId`, `bank.iban`, …). A PascalCase-only parse would have silently returned an all-null issuer/recipient block against real finalized documents — a §14 correctness failure.
- **Fix:** `SnapshotReader` looks up every property via a case-insensitive `EnumerateObject` match, tolerating both casings.
- **Files modified:** src/modules/Numera.Modules.Sales/Pdf/SnapshotReader.cs
- **Verification:** `SnapshotReader_maps_the_frozen_snapshot_without_live_master_data` builds the fixture with Web-defaults (camelCase) jsonb and asserts issuer/recipient/bank fields populate correctly.
- **Committed in:** `3ff868f` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** The case-insensitive parse is required for the render unit to read real frozen data. No scope creep; no architectural change.

## Issues Encountered
- The machine's default `dotnet` shim resolves no compatible SDK; used the user-local SDK 10 (`C:\Users\Admin\.dotnet10`) with `DOTNET_ROOT` + `DOTNET_MULTILEVEL_LOOKUP=0` per STATE.md. No impact on deliverables.

## User Setup Required
None - no external service configuration required. (The QuestPDF Community license is set in-code by the host; no account/key needed under the $1M threshold.)

## Next Phase Readiness
- The render unit is ready for 04-03 (render job): call `SnapshotReader.FromDocument(doc, logoBytes, logoContentType, language)` then `InvoiceDocument.Render(model)` and persist the `byte[]` — the Api host must set `QuestPDF.Settings.License = Community` at startup (Pitfall 1).
- 04-04 (email) attaches the same rendered bytes.
- Phase 5 (e-Rechnung): the single `Document.Create` is ready to be wrapped with `WithSettings(new DocumentSettings{ PdfA = true })` + the ZUGFeRD package with no layout rewrite.
- Coordination: this plan touched ONLY `Pdf/` + the module `.csproj` + `tests/Numera.Platform.Tests/Sales`; it did not touch `Rendering/`, `Email/`, `CompanyProfile.cs`, the EF migration/snapshot, or `tests/Numera.IntegrationTests` (04-01's territory).

---
*Phase: 04-pdf-versand*
*Completed: 2026-07-13*
