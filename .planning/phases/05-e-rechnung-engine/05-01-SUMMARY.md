---
phase: 05-e-rechnung-engine
plan: 01
subsystem: api
tags: [xrechnung, zugferd-csharp, en16931, ubl, cii, e-invoice, vat, cius]

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern
    provides: frozen SalesDocument snapshot + BG-23 VAT breakdown + Pflichttext/VATEX codes
  - phase: 04-pdf-versand
    provides: InvoicePdfModel + SnapshotReader (stateless frozen-model projection)
provides:
  - "EInvoiceMapper.ToDescriptor: the SINGLE EN 16931 mapping layer (frozen InvoicePdfModel → one ZUGFeRD-csharp InvoiceDescriptor), closing CIUS gaps G1-G4"
  - "XRechnungGenerator: one descriptor → XRechnung UBL bytes + XRechnung CII bytes (structural EINV-02 guarantee), plus a GenerateCiiForZugferd seam for 05-04"
  - "EInvoiceFormat enum (XRechnungUbl/XRechnungCii/ZugferdPdfA3) — the stable persistence key for 05-03"
  - "ZUGFeRD-csharp 18.0.0 pinned on the Sales module"
  - "14 golden-file mapper tests per VAT scenario proving UBL≡CII + category/VATEX/totals + G1-G4"
affects: [05-03, 05-04, 05-05]

# Tech tracking
tech-stack:
  added: [ZUGFeRD-csharp 18.0.0 (Apache-2.0, last OSS major)]
  patterns:
    - "One descriptor, N serializations — UBL and CII from the SAME InvoiceDescriptor (single source of truth)"
    - "Mapper reads ONLY the frozen snapshot, never live master data (same GoBD rule as SnapshotReader); totals transcribed, never recomputed"
    - "CIUS gaps G1-G4 filled from existing frozen fields + constants — no sales_documents migration"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/EInvoice/EInvoiceFormat.cs
    - src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs
    - src/modules/Numera.Modules.Sales/EInvoice/XRechnungGenerator.cs
    - tests/Numera.Platform.Tests/Sales/EInvoiceMapperTests.cs
  modified:
    - src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj

key-decisions:
  - "Pin ZUGFeRD-csharp 18.0.0 (last OSS major); FactoorSharp + XRechnung 4.0 are a documented upgrade seam in EInvoiceMapper, not v1 work"
  - "BT-10 Käuferreferenz defaults to the documented constant 'NA' when the frozen model has none (BR-DE-15 always satisfied; a genuinely-absent value is caught by the 05-03 dry-run)"
  - "BT-34/BT-49 electronic addresses use EAS scheme EM (ElectronicMailSmtp); BT-81 payment means = 58 (SEPA) with IBAN else 30"
  - "Category codes decided in ONE private switch (UNCL5305); VATEX exemption code transcribed from the frozen string via dash→underscore enum parse; §19 code/text left to the 05-03 KoSIT golden file"

patterns-established:
  - "Pattern: EInvoiceMapper is the single change-locus for the EN 16931 mapping + the ZUGFeRD-csharp upgrade seam"
  - "Pattern: golden-file mapper tests assert on emitted XML by EN 16931 element (local-name matched) so a library minor bump does not thrash the suite"

# Metrics
duration: 30min
completed: 2026-07-26
---

# Phase 5 Plan 01: EInvoice Mapper + XRechnung Generator Summary

**One EN 16931 `EInvoiceMapper` turns a finalized invoice's frozen `InvoicePdfModel` into a single ZUGFeRD-csharp `InvoiceDescriptor` that `XRechnungGenerator` saves to both XRechnung UBL and CII — closing the four XRechnung CIUS gaps (BT-10, BT-34/49 scheme EM, BG-6, BT-81) from frozen data with zero migration and zero recompute.**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-07-26T20:59Z
- **Completed:** 2026-07-26T19:29Z (21:29 local +0200)
- **Tasks:** 3
- **Files modified:** 5 (4 created, 1 csproj)

## Accomplishments
- **The single mapping layer (EINV-01 core):** `EInvoiceMapper.ToDescriptor` — static, stateless, frozen-model-only — builds ONE `InvoiceDescriptor` with correct seller/buyer/lines/BG-23/totals + `BusinessProcess`; every monetary value transcribed from the frozen snapshot, nothing recomputed (RESEARCH Pitfall 3).
- **CIUS gaps G1-G4 closed from existing frozen fields + constants (no schema change):** BT-10 Käuferreferenz defaulted, BT-34/BT-49 electronic addresses with EAS scheme EM, BG-6 seller contact (BT-41/42/43), BT-81 payment means 58/30.
- **Structural EINV-02 guarantee:** `XRechnungGenerator.GenerateUbl`/`GenerateCii` serialize the SAME descriptor to the two XRechnung syntaxes; a `GenerateCiiForZugferd` seam lets 05-04 reuse this exact CII path for the PDF/A-3 embed.
- **Proven per VAT scenario:** 14 golden-file tests cover S / AE §13b / E §19 / K / G / Z with correct UNCL5305 category + VATEX exemption + verbatim frozen Pflichttext + transcribed totals, and prove UBL and CII carry identical totals + per-(category,rate) tax.

## Task Commits

Each task was committed atomically:

1. **Task 1: ZUGFeRD-csharp + EInvoiceMapper (frozen model → InvoiceDescriptor, G1-G4)** - `738fd25` (feat)
2. **Task 2: XRechnungGenerator — one descriptor → UBL + CII bytes** - `f5c4275` (feat)
3. **Task 3: Golden-file mapper tests per VAT scenario** - `c75e2c9` (test)

_(Interleaved with the parallel 05-02 executor's commit `7da96bc`.)_

## Files Created/Modified
- `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceFormat.cs` - Output-target enum (XRechnungUbl/XRechnungCii/ZugferdPdfA3), the stable 05-03 persistence key.
- `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs` - THE frozen-model → InvoiceDescriptor mapper; one category switch, VATEX transcription, G1-G4 fill, upgrade seam.
- `src/modules/Numera.Modules.Sales/EInvoice/XRechnungGenerator.cs` - Descriptor → UBL + CII bytes; ZUGFeRD-CII seam; XmlFileName helper; pure.
- `tests/Numera.Platform.Tests/Sales/EInvoiceMapperTests.cs` - 14 golden-file tests (VAT scenarios + G1-G4 + UBL≡CII proof).
- `src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj` - Added ZUGFeRD-csharp 18.0.0 (pinned).

## Decisions Made
- **Pin ZUGFeRD-csharp 18.0.0** (last OSS major, Apache-2.0). FactoorSharp + XRechnung 4.0 documented as an upgrade seam in `EInvoiceMapper` — not v1.
- **Real 18.0.0 API honored** over the plan's indicative names: `desc.ReferenceOrderNo` is BT-10 (verified in the shipped XML doc), `SetSellerContact`/`SetSellerElectronicAddress`/`SetBuyerElectronicAddress` for BG-6/BT-34/BT-49, `SetPaymentMeans(PaymentMeansTypeCodes.SEPACreditTransfer|CreditTransferNonSEPA)` for BT-81 58/30, `AddCreditorFinancialAccount` for IBAN/BIC, `AddTradePaymentTerms(null, dueDate)` for BT-9, `ActualDeliveryDate`/`SetBillingPeriod` for BT-72/BG-14.
- **BT-121 exemption code is a ZUGFeRD-csharp enum** (`TaxExemptionReasonCodes`), not a string: the frozen `VATEX-EU-*` string is mapped by dash→underscore `Enum.TryParse` (unknown/blank → null so only the BT-120 reason text is emitted). The BT-120 reason text stays a verbatim string.
- **Unit codes** (`QuantityCodes` enum, whose numeric members are `_`-prefixed) are parsed from the frozen UN/ECE string with a `C62` fallback.
- **BT-10 default = "NA"** documented constant (`EInvoiceMapper.DefaultBuyerReference`), asserted by tests.

## Deviations from Plan

None - plan executed exactly as written. (The plan explicitly flagged its ZUGFeRD-csharp method names as indicative and instructed to honor the real 18.0.0 surface, which was done — this is conformance, not deviation.)

## Issues Encountered
- The CII serialization emits `ram:ApplicableTradeTax` at BOTH document (BG-23, with `CalculatedAmount`) and line level (category+rate only). The UBL≡CII per-category-tax comparison initially threw on the line-level rows; fixed by restricting the CII breakdown query to rows carrying a `CalculatedAmount` child. Caught and fixed within Task 3 before commit.

## User Setup Required
None - pure model→bytes with no external service configuration. (The KoSIT sidecar + validation gate are 05-02/05-03.)

## Next Phase Readiness
- **05-03** (XRechnung generate + two-stage validation gate + `document_einvoice` table) can call `XRechnungGenerator.GenerateUbl/GenerateCii` and key persistence on `EInvoiceFormat`. It also owns the §19 category-E VATEX code/text pinning via a KoSIT golden file, and the Storno sign decision (Pitfall 7) — this mapper is invoice-type-380 only by design.
- **05-04** reuses `GenerateCiiForZugferd` for the PDF/A-3 embed (one path, not two).
- **05-05** (inbound) reuses `InvoiceDescriptor.Load` from the same ZUGFeRD-csharp package.
- **Not verified here (by design):** KoSIT/BR-DE conformance — these are fast structural golden tests; the live-conformance gate is 05-02/05-03.

---
*Phase: 05-e-rechnung-engine*
*Completed: 2026-07-26*

## Self-Check: PASSED

- All 4 created files + SUMMARY.md exist on disk.
- All 3 task commits (738fd25, f5c4275, c75e2c9) exist in git history.
- ZUGFeRD-csharp 18.0.0 pinned in the Sales csproj.
- Build 0 warnings (TreatWarningsAsErrors); full platform suite 75/75 green (61 prior + 14 new).
