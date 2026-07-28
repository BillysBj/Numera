---
phase: 07-erweiterte-rechnungstypen
status: passed
verified_by: claude (reviewer of the Codex-delegated implementation)
verified_on: 2026-07-28
method: independent build + full integration/vitest/live-KoSIT re-runs with the .NET 10 SDK; goal-backward mapping of success criteria to committed code + tests
---

# Phase 7 Verification — Erweiterte Rechnungstypen

**Goal:** Nutzer kann über die Standardrechnung hinausgehende, praxisrelevante Rechnungstypen erstellen — Fremdwährung, wiederkehrende Rechnungen und Abschlagslogik.

**Result:** PASSED. All 3 success criteria delivered and backed by tests on real Postgres 18 (+ the live KoSIT validator for e-invoice conformance). Both human-verify checkpoints (07-08 documents UI, 07-09 recurring UI) approved by the user on 2026-07-28.

## Success criteria → evidence

1. **Fremdwährungsrechnungen; USt-Ausweis korrekt in EUR-Umrechnung.**
   - Frozen user-entered ExchangeRate/ExchangeRateDate + TotalTaxEur on sales_documents; at finalize TotalTaxEur = RoundingPolicy.RoundAmount(TotalTax / rate) — document total stays foreign (BT-5), VAT additionally in EUR (07-04). BT-6 (TaxCurrency=EUR) + BT-111 emitted in XRechnung/ZUGFeRD via post-serialization injection (07-05). 2-minor-unit guard (CurrencyScope); ForeignCurrencyInvoicing L+ gate. Documents UI: currency/rate fields + USt-in-EUR display (07-08).
   - Tests: ForeignCurrencyInvoiceTests (USD freezes EUR VAT + stays foreign; EUR leaves FX null; JPY/BHD rejected); EInvoiceMapperTests BT-6/BT-111 goldens; **live-KoSIT conformance ACCEPTED (0 errors) for a foreign-currency invoice in UBL + CII** under BR-53.

2. **Serienrechnungen, die automatisch nach Zeitplan erzeugt werden.**
   - recurring_invoice_templates(+lines) + per-template Hangfire recurring job (tenantId in args, no cross-tenant enumeration/BYPASSRLS — Decision 1); sales_documents partial unique (tenant,template,period_key) index for idempotency (07-06). GenerateRecurringInvoiceJob: FOR-UPDATE-locked catch-up loop driven by next_run_on/cadence, auto-finalize via the shared FinalizeCoreAsync (race-safe numbering; FX flows through) or Draft opt-out, optional auto-send (07-07). Templates UI: list + editor + pause/resume (07-09). RecurringInvoices L+ gate.
   - Tests: RecurringTemplateTests (persistence/RLS/status/gate/scheduling-id/duplicate-period index); RecurringGenerationTests (catch-up generates one invoice per missed period, same-period re-run is idempotent, auto-finalize→RE-number+open item, Draft opt-out, RLS, end conditions, foreign currency).

3. **Abschlags-/Schlussrechnungen; geleistete Anzahlungen korrekt verrechnet.**
   - New DocumentType Abschlagsrechnung/Schlussrechnung with AR-/SR- numbering; frozen sales_document_prepayment table; FinalizeCoreAsync keeps the FULL VAT breakdown but AmountDue = TotalGross − Σ prepaid gross (BT-113 / BR-CO-16, residual receivable — no double taxation); over-deduction rolls back finalize; all four DocumentType.Rechnung gates widened (07-02). BT-113 in XRechnung/ZUGFeRD + PDF deduction block, wired through the production render/e-invoice loaders (07-03). Creation: /api/documents/abschlag + /api/documents/final-invoice (freezes prepayment rows from selected finalized Abschläge); documents UI Abschlag picker + deduction/residual display (07-08). DownPaymentInvoices L+ gate.
   - Tests: DownPaymentInvoiceTests (residual + full VAT, over-deduction rollback, Storno of new types, RLS); EInvoiceMapper BT-113 goldens; **live-KoSIT ACCEPTED a real Schlussrechnung in UBL + CII**; production-path test asserts BT-113 in the generated UBL.

## Verification runs (final committed state)
- Full solution build: **0 warnings / 0 errors** (.NET 10 SDK).
- Integration suite: **121/121** on Testcontainers postgres:18 (+ live KoSIT conformance).
- Platform suite: **99/99**. Frontend: `tsc -b` clean, **vitest 56/56**.

## Notes / accepted boundaries (non-blocking)
- ZUGFeRD-csharp 18 lacks a BT-111 property → EUR VAT total is injected by post-processing the serialized UBL/CII XML in XRechnungGenerator (KoSIT-validated). Documented workaround.
- The /api/documents/final-invoice creation endpoint and the documentSchema FX client-gates lack dedicated tests; the money core they feed (residual/BT-113, EUR VAT) IS integration- and KoSIT-tested, and both flows were human-verified. Optional test backfill.
- open_items dunning columns (Phase 6) and the recurring period-key remain DB-accessed patterns already covered by their own suites.
- Accepted v1 limitation (07-02): no BT-113 cascade recompute on out-of-order Abschlag Storno.
