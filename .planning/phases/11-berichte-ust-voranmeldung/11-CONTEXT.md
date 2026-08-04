# Phase 11 Context — Berichte & USt-Voranmeldung

**Captured:** 2026-08-03 (via orchestrator decision questions; no full /gsd:discuss-phase run)
**Source:** user decisions locking the open questions from 11-RESEARCH.md

## Decisions (LOCKED — honor exactly, do not revisit)

### D1 — Support BOTH Ist- and Soll-Versteuerung via report-time derivation
The USt-VA must be correct for both `Besteuerungsart` values (already captured on `LedgerSettings` in Phase 10).
- **Soll-Versteuerung:** aggregate postings by invoice `EntryDate` within the Voranmeldungszeitraum (pure account+Steuerschlüssel sum).
- **Ist-Versteuerung:** derive recognition at REPORT TIME by attributing each payment to its `Payment.ValueDate` period via `PaymentAllocation → SalesDocument → SalesDocumentTaxBreakdown` (the tax base/amount lives on the invoice's breakdown; payment postings carry no tax metadata).
- **The Phase-10 ledger stays FROZEN** — do NOT add tax metadata to payment postings and do NOT change the posting engine/migrations. All Ist/cash-basis recognition is a report-time read model over existing frozen data.
- Kleinunternehmer (§19, `IsKleinunternehmer`) → no output USt; USt-VA is zero/■ for VAT — respect it.

### D2 — EÜR output = on-screen + QuestPDF print only (NO ELSTER XML for EÜR)
Render the Anlage-EÜR structure (Zufluss/Abfluss, cash-basis) on screen and as a QuestPDF print/export mirroring `DocumentPdfService`/`InvoiceDocument`. Do NOT build an ELSTER Anlage-EÜR XML this phase (annual ESt attachment; disproportionate here — success criterion 1 only requires "in der Struktur der Anlage EÜR"). EÜR cash-basis income/expense recognition follows the SAME report-time payment-date derivation as Ist (D1) so it never double-counts the invoice+payment postings.

### D3 — Omit Kennziffern that have NO real data source in v2.0 (do NOT render fabricated zeros)
The USt-VA form + ELSTER XML include ONLY Kennziffern that can be computed from ACTUAL Phase-10/11 postings. Kennziffern with no data source in this milestone are OMITTED entirely (not shown as 0-with-note).
- **Producible now (outgoing invoices + payments):** 81 (Umsätze 19% base), 86 (Umsätze 7% base), 83 (verbleibende USt-Vorauszahlung / Zahllast). Plus 41 (innergemeinschaftliche Lieferungen) IF the planner extends the SKR seed with the i.g.-Lieferung revenue accounts (8125/4125, TaxCategory.K) — decide during planning.
- **NOT producible in Phase 11 → OMIT:** 66 (abziehbare Vorsteuer) and any input-VAT / §13b-recipient / innergemeinschaftlicher-Erwerb Kennziffern (35/36/89/61/46/47) — these need the expense/Beleg posting path whose runtime entry point is deferred to Phase 12 (ACCT-05 shipped only the rule + tests in Phase 10). Because there are no Vorsteuer postings yet, the Zahllast (Kz 83) in Phase 11 equals the output VAT with no input-VAT deduction — this is correct given the available data; note it in the Prüfansicht.
- Design the XML/form generation so adding the omitted Kennziffern in a later phase is additive (the ELSTER Nutzdaten only carries the elements that have values).

## Claude's Discretion (freedom areas — make implementation choices, honor research recommendations)
- ELSTER USt-VA XML: hand-build the `Anmeldungssteuern` Nutzdaten (ISO-8859-15, namespace `.../ustva/v2026`) with `XmlWriter`, mirroring the Phase-5 ZUGFeRD post-processing approach; pull the exact `ustva/v2026` element list from the ERiC/ELSTER doc during planning. No ERiC envelope/TransferHeader (manual upload).
- Steuernummer → ELSTER 13-digit Bundesland-format conversion (from `CompanyProfile.TaxNumber`, local format) — locate/implement as needed.
- Whether to persist a `ust_va_filing` record (RLS-scoped; consider immutable-on-generate) vs compute-on-demand.
- Report read shape: mirror `LedgerEndpoints` raw-SQL RLS-scoped aggregation + keyset patterns.
- Prüfansicht drill-down depth (each Kz → contributing postings/invoices).
- Whether generating a USt-VA should require/encourage Festschreibung of the period (tie to `FestschreibungService`).

## Deferred Ideas (OUT of scope — do NOT include)
- Direct ERiC submission of the USt-VA (v3 — needs Hersteller-registration + C library). Calculation + Prüfansicht + XML/print export ONLY.
- ELSTER Anlage-EÜR XML (D2).
- Vorsteuer / expense-side Kennziffern and their data source (Phase 12 Belege).
- BWA / GuV / Bilanz (later milestone).
- Dauerfristverlängerung / Sondervorauszahlung.

## No-Fabrication rule
Every rendered Kennziffer/EÜR line must trace to real postings. Never emit a plausible-looking number that isn't backed by ledger data (D3). An honest omission beats a fabricated zero-with-caveat here (per user decision).
