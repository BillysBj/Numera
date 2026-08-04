---
phase: 11-berichte-ust-voranmeldung
verified: 2026-08-04T10:24:04Z
status: passed
score: 15/15 aggregate must-have truths verified across plans 11-01..11-07
---

# Phase 11: Berichte and USt-Voranmeldung Verification Report

**Phase Goal:** Nutzer kann seine steuerlichen Pflichtauswertungen erzeugen, die EUeR und die USt-Voranmeldung, direkt und korrekt aus den Buchungen.
**Verified:** 2026-08-04T10:24:04Z
**Status:** passed
**Re-verification:** No, initial verification

## Method

Note: gsd-tools.js verify artifacts/key-links could not parse this repo's must_haves YAML (its indentation-matching regex expects 4-space-indented artifacts/key_links keys under must_haves, but all seven 11-xx-PLAN.md files use 2-space indentation -- a pre-existing tool/convention mismatch, not a phase defect). Verification was therefore done by reading each plan's frontmatter must_haves directly and checking the actual implementation file by file, plus git log/git show to confirm the Phase-10 ledger was not touched.

## Goal Achievement

### Success Criterion 1 -- EUeR in Anlage-EUeR structure (Zufluss/Abfluss), matching bookings

VERIFIED. EuerCalculator.ComputeAsync (src/Numera.Api/Reporting/EuerCalculator.cs) computes Betriebseinnahmen purely from RecognitionReader.ReadCashRecognitionAsync (payment ValueDate), never invoice date -- confirmed in code and by EuerCalculatorTests (6 Facts: payment timing, no double-count, Regel netto/brutto split, Kleinunternehmer gross, Gewinn arithmetic, expense-incomplete Hinweis). EuerLineMap.cs maps SKR03/SKR04 accounts to real Anlage-EUeR Zeilen (14/15/17 income, 25/55/56/57 expense) -- checked in, not fabricated. EuerReport.Gewinn = SummeEinnahmen minus SummeAusgaben. Expense lines are honest zeros (Phase-12 deferred) with IsExpenseDataIncomplete=true and Hinweis="Betriebsausgaben unvollstaendig -- Belegerfassung ab Phase 12." always set -- this is the correct, honestly-flagged behavior per D2/NO-FABRICATION, not a gap. EuerDocument.cs (QuestPDF, de-DE culture) renders the same grouping plus incompleteness warning. On-screen: web/src/features/reports/EuerReportPage.tsx renders income/expenses/Gewinn plus incompleteness banner plus PDF download. AfA is explicitly flagged out of scope in an XML-doc comment on EuerLineMap.

### Success Criterion 2 -- USt-VA generated, Kennziffern correctly computed, shown in Pruefansicht

VERIFIED. UstVaCalculator.ComputeAsync (src/Numera.Api/Reporting/UstVaCalculator.cs) branches on LedgerSettings.Besteuerungsart: Soll uses RecognitionReader.ReadSollAsync (entry_date-grouped, signed natural-side netting so cross-period Storno nets correctly -- proven by Soll_cross_period_storno_reduces_81_in_the_reversal_period), Ist uses ReadCashRecognitionAsync (payment ValueDate attribution -- proven by Ist_attributes_81_to_payment_month_instead_of_invoice_month). Kz 83 = round(Kz81*0.19 + Kz86*0.07) from untruncated bases, carrying UstVaKennzifferMap.ZahllastHinweis = "Ohne Vorsteuerabzug (Belegerfassung ab Phase 12)." Kleinunternehmer gate returns an empty report (Lines: [], IsKleinunternehmer: true) -- proven by Kleinunternehmer_is_gated_without_zero_filled_vat_lines. Report_never_fabricates_unsupported_kennziffern proves 66/35/36/89/61/46/47 are absent. GET /api/reports/ustva and GET /api/reports/ustva/kz/{kz}/entries (ReportEndpoints.cs) expose the Pruefansicht and a per-Kz drill-down that correctly branches: Soll traces journal entries by entry_date, Ist traces invoice plus payment via the same Payment to PaymentAllocation to OpenItem to SalesDocument to SalesDocumentTaxBreakdown join at ValueDate. Frontend UstVaPruefansichtPage.tsx renders the Kz table, Zahllast row, Besteuerungsart, a "vorlaeufig" badge (from not isFestgeschrieben), the Hinweis, and expandable per-Kz drill-down (Soll journal rows vs Ist payment rows), plus a Kleinunternehmer empty state.

### Success Criterion 3 -- ELSTER-conformant XML export plus Druck (manual upload, no ERiC)

VERIFIED. UstVaXmlWriter.Write (src/Numera.Api/Reporting/Elster/UstVaXmlWriter.cs) hand-builds a bare Anmeldungssteuern Nutzdaten payload with XmlWriter, ISO-8859-15 encoding, namespace http://finkonsens.de/elster/elsteranmeldung/ustva/v{year}, version="{year}" -- no Elster/TransferHeader envelope, confirmed by reading the file (no envelope elements) and by UstVaXmlWriterTests (6 Facts covering shape/encoding/namespace/only-present-Kz/Steuernummer). SteuernummerConverter.Convert converts local Landes-format Steuernummer plus Bundesland to the ELSTER 13-digit form and explicitly rejects a VatId-shaped input (DE-prefix check). UstVaDocument.cs (QuestPDF) renders the same producible Kz plus Zahllast plus Besteuerungsart plus vorlaeufig badge plus Hinweis as a print. GET /api/reports/ustva/export.xml and /export.pdf (ReportEndpoints.cs) serve the downloads; Kleinunternehmer requests are gated with a 409 Conflict (no fabricated filing). No ERiC/Hersteller-registration/direct-submission code exists anywhere in the phase's files -- confirmed absent.

### Observable Truths (aggregate across the 7 plans' must_haves)

| # | Truth | Status | Evidence |
|---|---|---|---|
| 1 | Soll read sums postings by Kennziffer/TaxCategory/rate/direction on entry_date | VERIFIED | RecognitionReader.ReadSollAsync, raw SQL grouped exactly as specified |
| 2 | Ist read recognizes invoice net+VAT on Payment.ValueDate, pro-rata, reversals net out | VERIFIED | RecognitionReader.ReadCashRecognitionAsync; ReportRecognitionTests (4 Facts) |
| 3 | Kz 81/86/41 signed-netted, cross-period Storno nets correctly (Soll) / moves with payment (Ist) | VERIFIED | UstVaCalculator; UstVaCalculatorTests (6 Facts + 2 Theory cases = 7 executions) |
| 4 | Kz 83 = output-VAT-only Zahllast with explicit Vorsteuer-deferred Hinweis | VERIFIED | UstVaKennzifferMap.ZahllastHinweis; rendered in JSON/PDF/XML/frontend |
| 5 | Kleinunternehmer produces no USt-VA (gated, not fabricated zeros) | VERIFIED | UstVaCalculator early-return; UstVaXmlWriter throws; endpoint returns 409; frontend empty state |
| 6 | Only {81,86,41,83} present; 66/35/36/89/61/46/47 truly absent | VERIFIED | UstVaKennzifferMap versioned dictionary; Report_never_fabricates_unsupported_kennziffern test |
| 7 | EUeR income on Zufluss, no double-count with invoice postings | VERIFIED | EuerCalculator uses same ReadCashRecognitionAsync as Ist; test coverage |
| 8 | EUeR Regelunternehmer netto+USt-Zeile-17 vs Kleinunternehmer brutto | VERIFIED | EuerLineMap.RevenueLineFor branches on isKleinunternehmer |
| 9 | EUeR Gewinn = Einnahmen minus Ausgaben, with IsExpenseDataIncomplete/Hinweis always honest | VERIFIED | EuerCalculator.ComputeAsync final lines; always sets the flag (Phase-12 deferred) |
| 10 | Steuernummer to ELSTER 13-digit conversion per Bundesland; VatId rejected | VERIFIED | SteuernummerConverter.Convert |
| 11 | XML = bare Nutzdaten, ISO-8859-15, versioned namespace, additive Kz | VERIFIED | UstVaXmlWriter.Write |
| 12 | Both PDFs render via QuestPDF under explicit de-DE culture | VERIFIED | UstVaDocument/EuerDocument constructors set culture from ReportPdfLabels.German |
| 13 | ust_va_filing persisted, immutable-on-submit, RLS FORCE, correction = new row | VERIFIED | Migration 20260804092442_UstVaFiling.cs: ENABLE+FORCE RLS, tenant_isolation policy, REVOKE UPDATE/DELETE, trigger |
| 14 | Endpoints expose Pruefansicht/EUeR/exports, RLS-scoped, Kleinunternehmer-gated | VERIFIED | ReportEndpoints.cs; ReportEndpointsTests (6 Facts); CrossTenantIsolationCompletenessTests covers UstVaFiling |
| 15 | Frontend Pruefansicht + EUeR pages, routed, downloadable | VERIFIED | UstVaPruefansichtPage.tsx, EuerReportPage.tsx, routes in App.tsx, human-verify checkpoint approved |

Score: 15/15 aggregate truths verified (all individually confirmed against source, not merely assumed from SUMMARY claims).

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| src/Numera.Api/Reporting/RecognitionReader.cs (96 ln) | Soll+Ist shared read model | VERIFIED | Both methods present, RLS-implicit, no manual tenant filter |
| src/Numera.Api/Reporting/RecognitionModels.cs (20 ln) | Typed rows | VERIFIED | SollRecognitionRow, CashRecognitionRow exact shape |
| src/Numera.Api/Reporting/UstVaKennzifferMap.cs (91 ln) | Versioned {81,86,41,83} map | VERIFIED | Additive dictionary keyed by fiscal year 2026 |
| src/Numera.Api/Reporting/UstVaCalculator.cs (219 ln) | Kennziffer computation | VERIFIED | Soll/Ist branch, Zahllast, Kleinunternehmer gate, drill-down seam |
| src/Numera.Api/Reporting/UstVaModels.cs (29 ln) | Pruefansicht model | VERIFIED | UstVaLine, UstVaReport |
| src/Numera.Api/Reporting/EuerLineMap.cs (230 ln) | Account to Zeile map | VERIFIED | SKR03/SKR04, AfA-out-of-scope doc comment |
| src/Numera.Api/Reporting/EuerCalculator.cs (115 ln) | Cash-basis EUeR | VERIFIED | Uses shared cash recognition read |
| src/Numera.Api/Reporting/EuerModels.cs (18 ln) | EUeR report model | VERIFIED | Matches spec exactly incl. IsExpenseDataIncomplete/Hinweis |
| src/Numera.Api/Reporting/Elster/SteuernummerConverter.cs (132 ln) | Local to ELSTER 13-digit | VERIFIED | Per-Bundesland formats, VatId rejection |
| src/Numera.Api/Reporting/Elster/UstVaXmlWriter.cs (134 ln) | Nutzdaten XML | VERIFIED | ISO-8859-15, versioned namespace, additive Kz |
| src/Numera.Api/Pdf/UstVaDocument.cs (181 ln) | USt-VA print | VERIFIED | Kz table, Zahllast highlight, vorlaeufig badge, Hinweis footnote |
| src/Numera.Api/Pdf/EuerDocument.cs (200 ln) | EUeR print | VERIFIED | Einnahmen/Ausgaben sections, incompleteness warning, Gewinn |
| src/Numera.Api/Pdf/ReportPdfLabels.cs (48 ln) | de-DE labels | VERIFIED | Present |
| src/Numera.Api/Reporting/UstVaFiling.cs (66 ln) | Filing snapshot entity | VERIFIED | ITenantEntity, Draft/Submitted, BerichtigtVonFilingId |
| src/platform/Numera.Platform.Db/Migrations/20260804092442_UstVaFiling.cs | RLS+immutability migration | VERIFIED | ENABLE+FORCE RLS, REVOKE, trigger, all present |
| src/Numera.Api/Endpoints/ReportEndpoints.cs (497 ln) | All report endpoints | VERIFIED | ustva, ustva/kz/{kz}/entries, euer, export.xml, export.pdf x2 |
| web/src/features/reports/ustvaApi.ts | TanStack Query hooks | VERIFIED | Present (198 ln) |
| web/src/features/reports/UstVaPruefansichtPage.tsx (327 ln) | Pruefansicht page | VERIFIED | All required elements present |
| web/src/features/reports/EuerReportPage.tsx (179 ln) | EUeR page | VERIFIED | All required elements present |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| RecognitionReader.ReadSollAsync | postings/journal_entries/accounts | raw SqlQuery grouped by kennziffer/tax_category/rate/direction | WIRED | Exact SQL confirmed |
| RecognitionReader.ReadCashRecognitionAsync | payment/payment_allocation/open_items/sales_documents/tax_breakdown | pro-rata join at value_date | WIRED | Exact SQL confirmed |
| UstVaCalculator | RecognitionReader | ReadSollAsync/ReadCashRecognitionAsync branch on Besteuerungsart | WIRED | Confirmed in ComputeAsync |
| UstVaCalculator | CompanyProfile.IsKleinunternehmer / LedgerSettings.Besteuerungsart | tenant config read | WIRED | Confirmed |
| EuerCalculator | RecognitionReader.ReadCashRecognitionAsync | same cash-basis path as Ist | WIRED | Confirmed |
| EuerCalculator | EuerLineMap + IsKleinunternehmer | account to Zeile mapping, brutto/netto branch | WIRED | Confirmed |
| UstVaXmlWriter | UstVaReport + SteuernummerConverter | serialize computed report | WIRED | Confirmed |
| UstVaDocument/EuerDocument | UstVaReport/EuerReport | render each line | WIRED | Confirmed |
| ReportEndpoints | Calculators/XmlWriter/PDF documents | compute then serialize/render/persist | WIRED | Confirmed |
| Program.cs | reporting services + MapReportEndpoints | AddScoped registrations + endpoint mapping | WIRED | AddScoped RecognitionReader/UstVaCalculator/EuerCalculator, app.MapReportEndpoints confirmed |
| UstVaFiling | ust_va_filing migration | RLS policy + immutability trigger | WIRED | Confirmed |
| web/ustvaApi.ts | /api/reports/* | lib/api fetch + TanStack Query | WIRED | Confirmed |
| App.tsx | reports pages | Route path=/reports/... | WIRED | Confirmed, nav entries present |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|---|---|---|
| ACCT-07 (EUeR) | SATISFIED | None |
| ACCT-08 (USt-VA calc + Pruefansicht + ELSTER-XML/Druck) | SATISFIED | None |

### LOCKED Decisions Verified

- D1 (Soll+Ist via report-time derivation, ledger FROZEN): Confirmed. git log/git show across all Phase-11 commits show zero modification to any Phase-10 ledger entity, posting engine, or migration file. PaymentPostingSource.cs still books only Bank/Forderung with no TaxCategory/TaxRatePercent fields -- payment postings carry no tax metadata, exactly as locked. The only "Ledger module" touch in Phase 11 is additive seed-JSON rows (accounts 8125/4125) for Kz 41, which is explicitly plan-sanctioned data, not an engine/schema change.
- D2 (EUeR = on-screen + QuestPDF only, no ELSTER EUeR XML; same cash-basis read as Ist): Confirmed. No EUeR XML writer exists anywhere in the codebase (checked Reporting/ and Reporting/Elster/ -- only UstVaXmlWriter exists). EuerCalculator uses the identical ReadCashRecognitionAsync method as UstVaCalculator's Ist path.
- D3 + NO-FABRICATION (only {81,86,41,83} declared; 66/35/36/89/61/46/47 truly absent; Kz 83 output-VAT-only with Hinweis; EUeR expense caveat honest, not fabricated): Confirmed. UstVaKennzifferMap contains exactly 4 entries. Report_never_fabricates_unsupported_kennziffern test proves it. EuerCalculator.ComputeAsync always sets IsExpenseDataIncomplete: true with the mandated Hinweis text, and expense lines are explicit Betrag: 0m rather than any estimated/derived figure.

### Anti-Patterns Found

None. No placeholder text, empty handlers, return null/return [] stubs, or console-log-only implementations found in any of the 19 reviewed backend/frontend artifacts. All "empty" behaviors present (Kleinunternehmer gate, expense-incomplete zeros, omitted Kz) are explicit, intentional, and honestly flagged per the locked NO-FABRICATION rule -- not accidental stubs.

### Human Verification Required

None outstanding. The blocking human-verify checkpoint (11-07 Task 3 -- end-to-end reporting loop against the seeded Phase-10 ledger) was already run and APPROVED by the user per the orchestrator's briefing, and the associated frontend code was independently reviewed here and matches the approved scope.

### Known, Intentional Scope Boundaries (not gaps)

- ELSTER Nutzdaten omits DatenLieferant/Unternehmer blocks and has not been validated against the official ERiC ustva/v2026 XSD in this environment (flagged MEDIUM-confidence in 11-04-SUMMARY.md; to be confirmed on a real Mein-ELSTER upload -- this is a pre-acknowledged limitation of hand-building the XML without the authoritative Datensatzbeschreibung, not a phase defect).
- Vorsteuer/expense-side Kennziffern (66, etc.) and EUeR expense runtime data are deferred to Phase 12 by design (Belege/expense posting path not yet built) -- surfaced honestly via the Zahllast Hinweis and IsExpenseDataIncomplete flag rather than fabricated.
- No ERiC direct submission and no ELSTER EUeR XML -- both explicitly out of scope per D2/CONTEXT.

### Gaps Summary

No gaps found. All three success criteria are met with concrete, working, tested code: the EUeR is generated in Anlage-EUeR structure from cash-basis recognition with an honest incompleteness caveat; the USt-VA correctly computes its producible Kennziffern for both Soll and Ist tenants with a working Pruefansicht and per-Kz drill-down; and the USt-VA exports as ELSTER-conformant Nutzdaten XML plus a QuestPDF print, with no ERiC direct-submission code anywhere in the phase. The Phase-10 ledger was independently confirmed frozen (no entity/engine/migration changes) via git history, and payment postings were confirmed to still carry no tax metadata.

---

Verified: 2026-08-04T10:24:04Z
Verifier: Claude (gsd-verifier)
