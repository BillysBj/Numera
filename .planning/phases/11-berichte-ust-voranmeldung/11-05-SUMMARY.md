---
phase: 11-berichte-ust-voranmeldung
plan: 05
subsystem: reporting-pdf
tags: [questpdf, ust-va, euer, de-de, no-fabrication]

requires:
  - phase: 11-02
    provides: USt-VA report model with producible Kennziffer lines and Zahllast
  - phase: 11-03
    provides: Anlage-EÜR report model with grouped lines and incompleteness caveat
provides:
  - "A4 QuestPDF print for USt-VA review reports"
  - "A4 QuestPDF print for Anlage-EÜR-shaped reports"
  - "Explicit de-DE report labels and money/date formatting"
  - "Pure PDF smoke tests with PdfPig text extraction"
affects: [report-export, ust-va-print, euer-print]

tech-stack:
  added: []
  patterns:
    - "QuestPDF IDocument plus static Render(report) byte-array seam"
    - "Explicit CultureInfo(de-DE), independent of ambient worker culture"
    - "Render only report-supplied lines; do not synthesize omitted report positions"

key-files:
  created:
    - src/Numera.Api/Pdf/ReportPdfLabels.cs
    - src/Numera.Api/Pdf/UstVaDocument.cs
    - src/Numera.Api/Pdf/EuerDocument.cs
    - tests/Numera.IntegrationTests/ReportPdfTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-05-SUMMARY.md
  modified: []

key-decisions:
  - "The USt-VA table iterates report.Lines exactly once and highlights the supplied Kz 83 row without adding omitted Kennziffern"
  - "Nullable USt-VA amounts render blank rather than as fabricated zero values"
  - "The EÜR warning is rendered verbatim only when IsExpenseDataIncomplete and Hinweis are present"
  - "Both layouts use an explicit white A4 page color for deterministic PDF and image previews"

completed: 2026-08-04
---

# Phase 11-05: QuestPDF report prints

**The USt-VA and EÜR report models now render as polished A4 QuestPDF documents with explicit German formatting, model-only line output, and an unmistakable EÜR expense-incompleteness warning.**

## Accomplishments

- Added shared German report labels backed by an explicit `CultureInfo("de-DE")`.
- Added `UstVaDocument : IDocument` with the requested title/meta band, Soll-/Ist-Besteuerungsart, preliminary badge, four-column Kennziffer table, highlighted Kz 83, footnote, page numbering, and `Render(UstVaReport)` helper.
- Kept the USt-VA print no-fabrication-safe by iterating only `report.Lines`; missing Kennziffern are never synthesized and nullable cells remain blank.
- Added `EuerDocument : IDocument` with Anlage-EÜR income/expense grouping, line labels including Zeile 17 when supplied, group totals, highlighted Gewinn/Verlust, prominent incompleteness warning, page numbering, and `Render(EuerReport)` helper.
- Added pure `ReportPdfTests` fixtures with no database dependency. The tests check `%PDF` magic, extract rendered text with the already-transitive PdfPig dependency, prove Kz 81 and Zeile 17 are present, and prove the supplied EÜR warning is present.
- Performed a temporary standalone QuestPDF render/image smoke check (not the integration test suite), visually confirming white A4 pages, visible metadata/headings, aligned numeric columns, unclipped content, highlights, warning placement, and page numbering. All temporary harness/PDF/PNG artifacts were removed afterward.

## Verification

- Exact final build command: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build --configuration Release --no-restore`.
- Result: successful full-solution Release build, including `Numera.Api` and `Numera.IntegrationTests`; **0 warnings, 0 errors**.
- Integration tests were compiled but not run, per explicit instruction.
- No package was added; the implementation reuses the existing QuestPDF and transitive PdfPig references.
- No files were staged or committed.

## Deviations and uncertainties

- No behavioral deviation from plan 11-05 or the locked no-fabrication decisions.
- A white page color and explicit EÜR table row/column coordinates were added after visual smoke rendering exposed transparent PNG previews and one automatic-placement alignment issue; these are layout-stability refinements within plan scope.
- Runtime execution of `ReportPdfTests` remains for reviewer-run verification because integration tests were explicitly not to be run in this task.
- No Reporting or Reporting/Elster source file was changed.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-04*
