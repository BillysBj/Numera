---
phase: 05-e-rechnung-engine
verified: 2026-07-27T13:58:17Z
status: passed
score: 5/5 must-haves verified
---

# Phase 5: E-Rechnung-Engine Verification Report

**Phase Goal:** Nutzer kann gesetzlich verpflichtende E-Rechnungen EN-16931-konform erzeugen, validieren und empfangen — der strategische Compliance-Kern von Numera.
**Verified:** 2026-07-27T13:58:17Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer kann eine Rechnung als XRechnung (UBL UND CII, EN 16931) erzeugen und per Download oder E-Mail uebermitteln | VERIFIED | `EInvoiceMapper.ToDescriptor` (one descriptor) to `XRechnungGenerator.GenerateUbl`/`GenerateCii` (two serializations). `EInvoiceService.GenerateAndValidate` + `GetOrGenerate` (render-if-absent) persist to `document_einvoice`. `GET /api/documents/{id}/xrechnung?syntax=ubl or cii` and `POST /{id}/send-einvoice` (gated on stored Accepted verdict) both wired in `EInvoiceEndpoints.cs`. 12/12 live-KoSIT conformance tests pass (S/AE/E/K/G/Z x ubl/cii) against the real government-validator sidecar. |
| 2 | Nutzer kann eine Rechnung als ZUGFeRD (PDF/A-3 mit eingebettetem XML) erzeugen; PDF- und XML-Werte stimmen EXAKT ueberein | VERIFIED | `ZugferdGenerator.Generate`: QuestPDF core `RenderPdfA` (PDF/A-3b) + `XRechnungGenerator.GenerateCiiForZugferd` (same frozen `InvoicePdfModel`) embedded via `DocumentOperation` (AF Source) + ZUGFeRD XMP. `EInvoiceService.FinalizeFormats` = [Ubl, Cii, ZugferdPdfA3]; `GET /{id}/zugferd` serves render-if-absent. `ZugferdGeneratorTests.Embedded_cii_is_byte_identical_to_the_standalone_cii` asserts standalone equals embedded byte-for-byte; `Embedded_xml_values_equal_the_printed_model_values` cross-checks CII summation totals against the model. All 8 tests (2 fixtures x 4 assertions) pass. |
| 3 | Jede ausgehende E-Rechnung wird VOR Finalisierung gegen den KoSIT-Validator geprueft; Fehler blockieren den Versand und werden verstaendlich erklaert | VERIFIED | `SalesDocumentEndpoints.cs` finalize handler: for `DocumentType.Rechnung`, `einvoice.DryRunAsync(doc, profile, partner, ct)` runs BEFORE `FinalizeCoreAsync`/number assignment; Rejected leads to 422 with `EInvoiceGate.ToProblemDictionary` (DE authoritative + EN); Unavailable logs a warning and proceeds (outage never strands a number). `POST /{id}/send-einvoice` is the stage-2 gate: refuses unless the stored UBL artifact ValidationStatus == Accepted (Rejected leads to 422 explained; Unavailable leads to 409). `KoSitReport.Parse` is a namespace-tolerant VARL parser with a DE/EN rule-id explanation map. Live-KoSIT harness (12/12) proves this against the real KoSIT JAR 1.5.0 / config 2025-07-09 sidecar. |
| 4 | Nutzer kann empfangene E-Rechnungen (XRechnung/ZUGFeRD) hochladen, validieren und menschenlesbar anzeigen | VERIFIED | `InboundParser.Parse`: PDF-vs-XML detect, PdfPig /EmbeddedFiles extraction (name-stem + content-sniff fallback), `InvoiceDescriptor.Load` (auto UBL/CII/ZUGFeRD), `InboundReadModel` projection. `InboundEInvoiceService.IngestAsync` validates via the same `IEInvoiceValidator`, stores immutably. POST/GET `/api/inbound-documents`, `GET /{id}`, `GET /{id}/original` all mapped. Frontend `InboundListPage.tsx`/`InboundDetailPage.tsx` wired into `App.tsx` routing (`/inbound`, `/inbound/:id`) with DE/EN i18n (`inbound.json`). `InboundEInvoiceTests.cs` (4/4) passes on real Postgres. |
| 5 | Empfangene E-Rechnungen werden als Eingangsbelege dem Lieferanten zugeordnet und abgelegt | VERIFIED | `SupplierMatcher.MatchSellerAsync`: exact VAT-id match (preferring IsSupplier=true, then earliest id), then exact name fallback, then null. `InboundDocument` (RLS, `inbound_document` table, migration `20260727085052_InboundDocument.cs` with ENABLE/FORCE ROW LEVEL SECURITY + tenant_isolation policy) stores OriginalBytes byte-for-byte immutable + MatchedPartnerId + read-model + verdict. Confirmed via code inspection + passing integration test. |

**Score:** 5/5 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs` | Single EN 16931 mapping layer, frozen model to one InvoiceDescriptor | VERIFIED | Reads only frozen InvoicePdfModel; totals transcribed not recomputed; fills CIUS gaps G1-G4 (BT-10, electronic addresses, BG-6 contact, BT-81 payment means) |
| `src/modules/Numera.Modules.Sales/EInvoice/XRechnungGenerator.cs` | One descriptor to UBL + CII bytes | VERIFIED | GenerateUbl/GenerateCii/GenerateCiiForZugferd all route through EInvoiceMapper.ToDescriptor + descriptor.Save |
| `src/modules/Numera.Modules.Sales/EInvoice/ZugferdGenerator.cs` | PDF/A-3b + embedded CII from same model | VERIFIED | InvoiceDocument.RenderPdfA + XRechnungGenerator.GenerateCiiForZugferd over one model; DocumentOperation embeds w/ AF Source relationship + ZUGFeRD XMP |
| `src/Numera.Api/Services/EInvoiceService.cs` | Generate/validate/persist orchestration + dry-run + render-if-absent | VERIFIED | FinalizeFormats=[Ubl,Cii,ZugferdPdfA3]; GenerateAndValidate idempotent replace; DryRunAsync for pre-finalize gate; GetOrGenerate render-if-absent; ZUGFeRD reuses CII stored KoSIT verdict (no double-validation) |
| `src/Numera.Api/Endpoints/EInvoiceEndpoints.cs` | Download + send-gate HTTP surface | VERIFIED | /xrechnung?syntax=, /zugferd, /send-einvoice all mapped; EInvoiceGate.ToProblemDictionary for DE/EN explanations |
| `src/Numera.Api/Services/KoSitValidatorClient.cs` + `KoSitReport.cs` | Typed HTTP client + VARL report parser | VERIFIED | Outage-vs-rejection distinction (200/406 = report, else Unavailable); namespace-tolerant parsing |
| `src/modules/.../EInvoice/Inbound/InboundParser.cs` + `SupplierMatcher.cs` | Detect/extract/parse + VAT-id match | VERIFIED | PdfPig embedded-file extraction with stem+content-sniff fallback; VAT-id-first supplier match |
| `src/Numera.Api/Services/InboundEInvoiceService.cs` + `InboundDocumentEndpoints.cs` | Upload/list/detail/original endpoints | VERIFIED | Upload to parse to validate to match to store (non-idempotent, each upload distinct); RLS-scoped reads |
| `src/platform/Numera.Platform.Db/Migrations/20260727085052_InboundDocument.cs` | inbound_document table + RLS | VERIFIED | Hand-written ENABLE ROW LEVEL SECURITY + FORCE ROW LEVEL SECURITY + tenant_isolation policy |
| `web/src/features/inbound/{InboundListPage,InboundDetailPage}.tsx` + `web/src/lib/api/inbound.ts` | Bilingual inbound frontend | VERIFIED | Routed in App.tsx (/inbound, /inbound/:id), nav link present, enum wire-format mirrored from C# ordinals |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| SalesDocumentEndpoints finalize handler | EInvoiceService.DryRunAsync | direct call, pre-transaction | WIRED | Rejected leads to 422 before FinalizeCoreAsync/numbering; Unavailable logged, proceeds |
| InvoiceFinalized domain event | GenerateEInvoiceJob | EnqueueEInvoiceOnFinalize (Hangfire enqueue after commit) | WIRED | Confirmed via Program.cs DI registration (AddScoped IDomainEventHandler InvoiceFinalized, EnqueueEInvoiceOnFinalize) |
| GenerateEInvoiceJob | EInvoiceService.FinalizeFormats | foreach loop | WIRED | Iterates the 3-entry list with no per-format special-casing (ZUGFeRD reuses CII verdict internally) |
| EInvoiceEndpoints /send-einvoice | stored EInvoiceArtifact.ValidationStatus | GetArtifactAsync / GenerateAndValidate fallback | WIRED | Refuses dispatch unless Accepted; Rejected leads to 422 explained; else leads to 409 |
| InboundDocumentEndpoints POST / | InboundEInvoiceService.IngestAsync | direct call | WIRED | 422 on non-e-invoice upload (no row written), 201 with summary on success |
| InboundParser | SupplierMatcher | InboundEInvoiceService.IngestAsync composition | WIRED | Seller VAT id/name from read-model feeds the matcher; result stored as MatchedPartnerId |
| App.tsx routes | InboundListPage/InboundDetailPage | React Router Route | WIRED | /inbound, /inbound/:id mapped; nav link present |
| web/src/lib/api/inbound.ts | /api/inbound-documents | fetch w/ credentials:include | WIRED | List/detail/upload/original all implemented, enum ordinals mirrored |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|-----------------|
| EINV-01 | SATISFIED | none |
| EINV-02 | SATISFIED | none |
| EINV-03 | SATISFIED | none |
| EINV-04 | SATISFIED | none |
| EINV-05 | SATISFIED | none |

(Note: .planning/REQUIREMENTS.md traceability table still shows these as Pending — a documentation-sync item, not a functional gap; recommend updating on next docs pass.)

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| tests/Numera.IntegrationTests/EInvoiceOutboundTests.cs | 70, 90 | Stale assertion Assert.Equal(2, artifacts.Count) / CountAsync(...) == 2 | Warning | This 05-03 test was never updated when 05-04 appended ZugferdPdfA3 to EInvoiceService.FinalizeFormats. The PRODUCTION behavior is correct (the job now generates 3 artifacts: UBL, CII, ZUGFeRD, exactly what EINV-02 requires), but the test hardcoded expectation of 2 is now wrong, so it fails on the current codebase (confirmed: dotnet test on Numera.IntegrationTests gives 87/88 pass, this is the sole failure). Does not block any of the 5 phase success criteria (independently verified via code inspection + the live-KoSIT harness + ZugferdGeneratorTests), but it is a real regression in the checked-in regression suite that should be fixed (update the two assertions to 3 and add a ZugferdPdfA3 artifact assertion) before this branch is considered CI-green. |
| src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs, EInvoiceService.cs | multiple | Comment/const use of the word placeholder (DryRunNumberPlaceholder, BT-10 default) | Info | Legitimate, documented business logic (a structural pre-finalize placeholder number / BR-DE-15 default), not an incomplete stub, confirmed by reading surrounding code and the pre-finalize dry-run flow. |
| tests/Numera.Platform.Tests/Sales/ZugferdGeneratorTests.cs | 30-35 | Documented TODO(05-verify): veraPDF PDF/A conformance not wired into CI; only the pdfaid:part=3 XMP marker is asserted | Info | Pre-accepted per task instructions, documented, non-blocking follow-up. |
| KoSIT sidecar | n/a | Image is easybill/kosit-validator-xrechnung_3.0.2:v0.2.7 (JAR 1.5.0, config 2025-07-09), not the originally-named image | Info | Pre-accepted per task instructions, still the production XRechnung 3.0.2 ruleset; confirmed the sidecar IS running and all 12 live-conformance tests pass against it. |

### Human Verification Required

None. The 05-05 human-verify checkpoint (visual inbound UI round-trip) was already approved by the user per the phase own checkpoint record (commit 8ede8f5: docs(05-05) complete inbound e-invoices + human-verify checkpoint approved, Phase 5 done), and is treated as accepted per task instructions. No new items require human testing beyond what has already been approved.

### Automated Test Evidence (this verification run)

Ran against the user-local .NET 10 SDK (10.0.301) with numera-postgres (started fresh, healthy) and numera-kosit-validator (already up 18h) docker containers:

- dotnet build Numera.sln -c Debug: 0 errors, 0 warnings.
- Numera.Platform.Tests filtered to EInvoice or Zugferd or KoSit: 34/34 passed (includes ZugferdGeneratorTests, EInvoiceMapperTests, KoSitReportTests).
- Numera.IntegrationTests filtered to InboundEInvoiceTests: 4/4 passed (real Postgres, RLS).
- Numera.IntegrationTests filtered to Category=KositConformance: 12/12 passed (real KoSIT validator sidecar, all 6 VAT scenarios x UBL/CII, Accepted with 0 errors).
- Numera.IntegrationTests full suite (no filter): 87/88 passed, 1 failure (EInvoiceOutboundTests stale count assertion, see Anti-Patterns above; this is a test-only regression, not a production defect).

### Gaps Summary

No gaps block phase-goal achievement. All 5 observable truths are verified against real production code (not SUMMARY claims): the mapper/generator produce structurally-identical UBL/CII/ZUGFeRD from one frozen model; the two-stage KoSIT gate is wired exactly at the documented points (pre-finalize dry-run blocking a hard rejection before the gapless number is burned, outage never blocking finalize; post-finalize send gate requiring a stored Accepted verdict); the inbound pipeline (parse, validate, supplier-match, immutable store) and its bilingual frontend are wired end-to-end. This is corroborated by 34 unit tests, 4 inbound-integration tests on real Postgres, and, most significantly, 12/12 live conformance tests proving Numera actual generated XRechnung XML is Accepted by the real government KoSIT validator across all 6 VAT scenarios in both syntaxes.

One test-suite regression was found and is documented above (EInvoiceOutboundTests.cs stale artifact-count assertion, broken by 05-04 addition of ZugferdPdfA3 to FinalizeFormats without updating the 05-03 test). This should be fixed as a quick follow-up (change 2 to 3 in two assertions) but does not affect any of the phase 5 success criteria, which were independently verified through other passing tests and direct code inspection.

---

*Verified: 2026-07-27T13:58:17Z*
*Verifier: Claude (gsd-verifier)*
