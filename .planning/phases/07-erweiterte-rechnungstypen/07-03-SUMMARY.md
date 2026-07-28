# Plan 07-03 Summary — INV-07 E-Invoice/PDF Deduction

Implemented the BT-113 representation and presentation model for frozen
Schlussrechnung prepayments.

## Delivered

- Added immutable PDF-model prepayment rows with number, date, net, VAT, gross,
  stable line number, and a gross `TotalPrepaid` convenience value.
- Extended `SnapshotReader` to map explicitly supplied
  `SalesDocumentPrepayment` rows in date/number order, without reading live
  Abschlagsrechnungen.
- Switched `EInvoiceMapper` to ZUGFeRD-csharp 18's nine-argument `SetTotals`
  overload and emits frozen gross prepayments as BT-113 alongside residual
  BT-115. The full project VAT breakdown remains unchanged.
- Added a bilingual, culture-formatted PDF deduction table and the prepaid total
  plus residual amount due.
- Widened finalize enqueue handling to Rechnung, Abschlagsrechnung, and
  Schlussrechnung; all remain BT-3 type 380.
- Added pure UBL/CII XML assertions for BT-113 = 357.00, BT-115 = 833.00, and
  unchanged full VAT.
- Added a live KoSIT `prepay` scenario for UBL and CII.

## Scope Limitation

Plan 07-02 created `SalesDocumentPrepayment` but did not add a `Prepayments`
collection to `SalesDocument`. The production PDF and e-invoice query loaders
also do not query those child rows. Those three files are outside the 07-03
frontmatter and the task explicitly forbids touching other files.

Accordingly, `SnapshotReader.FromDocument` now has an explicit `prepayments`
parameter and documents the frozen-row loading contract, but the existing
production callers still pass no rows. Completing production end-to-end loading
requires permission to modify:

- `src/modules/Numera.Modules.Sales/SalesDocument.cs`
- `src/Numera.Api/Services/DocumentPdfService.cs`
- `src/Numera.Api/Services/EInvoiceService.cs`

No live Abschlag data is recomputed or read as a fallback.

## Verification

- `dotnet build Numera.sln -c Debug`
  - succeeded: 0 warnings, 0 errors
- `dotnet test tests/Numera.Platform.Tests --no-build`
  - 98 passed, 0 failed, 0 skipped
- `docker compose up -d kosit-validator`
  - sidecar running and reachable on host port 8081
- `dotnet test tests/Numera.IntegrationTests --no-build --filter Category=KositConformance`
  - 14 passed, 0 failed, 0 skipped
  - live KoSIT accepted `prepay/ubl` and `prepay/cii` with zero error findings
- Generated XML inspection:
  - UBL: `PrepaidAmount=357.00`, `PayableAmount=833.00`
  - CII: `TotalPrepaidAmount=357.00`, `DuePayableAmount=833.00`
- `dotnet test tests/Numera.IntegrationTests --no-build`
  - 109 passed, 0 failed, 0 skipped
