# Project Research Summary

**Milestone:** Numera v2.0 — Buchhaltung, Banking, Belege & Ausgaben, Monetarisierung
**Synthesized:** 2026-08-02 (consolidates STACK / FEATURES / ARCHITECTURE / PITFALLS in this directory)
**Confidence:** HIGH on decisive scope + stack + integration; MEDIUM on external-provider operational specifics (finAPI, Stripe, ELSTER/DATEV format versions, OCR accuracy) — verify in the owning phase.

---

## The one-paragraph picture

v2.0 turns Numera from an invoicing product into a full finance system by adding four domains onto the existing GoBD-compliant .NET 10 + Postgres-RLS monolith. The **bookkeeping engine (Ledger)** is the spine — invoices and payments already exist as immutable facts, so bookings become an automatic *projection* of them (no double entry). **Banking (finAPI)** feeds real bank transactions that reconcile against the existing open items and reuse `PaymentService`. **Belege/OCR** captures expenses that post into the Ledger and feed EÜR/USt-VA. **Stripe billing** finally makes the S/M/L/XL plans self-service by driving the existing `tenants.plan` gate. Nothing here needs a new architecture — it extends proven seams (domain events, Hangfire, RLS, append-only immutability, entitlements).

## Stack additions (from STACK.md — HIGH confidence)

- **Buchhaltung:** *no new library.* Double-entry is a domain/SQL problem → extend the existing `Ledger` module with an append-only, balanced journal (DB trigger + REVOKE + period-lock). Seed **SKR03/SKR04 yourself** (DATEV ships only PDF/Excel, no API). EÜR/GuV/BWA render via the existing **QuestPDF**.
- **Banking:** **finAPI** (BaFin-licensed, Berlin-Group XS2A). **RegShield/Web Form 2.0 → Numera needs no own PSD2 licence.** No official .NET SDK → generate a typed client from their OpenAPI. SEPA via finAPI **PIS**. ⚠ **GoCardless/Nordigen is closed to new signups — do not build on it.**
- **Belege:** two tiers — incoming ZUGFeRD/XRechnung Belege parse with the **existing ZUGFeRD-csharp (zero OCR)**; photographed receipts → **Azure AI Document Intelligence** (prebuilt-receipt, **EU region** + DPA) behind an `IReceiptExtractor` port, self-hosted PaddleOCR/docTR as escape hatch. Email intake = own catch-all mailbox polled by **MailKit** (already in stack).
- **Monetarisierung:** **Stripe.net 52.2.0** (.NET 10 OK). Hosted **Checkout + Customer Portal** (no card forms), **idempotent signed webhooks are the single source of truth** for `tenants.plan`.

## Feature table stakes (from FEATURES.md)

- **Buchhaltung:** per-tenant SKR03/04 chart, double-entry journal, automatic booking from invoice/payment, elektronisches **Kassenbuch (cash journal, NO TSE)**, **EÜR + GuV + BWA**, **USt-Voranmeldung** (Kennziffern calc + export).
- **Banking:** connect bank (finAPI Web Form), sync transactions, **automatic reconciliation** with open items, SEPA credit transfer.
- **Belege:** camera/upload capture, OCR extraction → **review before posting**, per-tenant **email intake**, GoBD-immutable 10-year archive, expense/supplier-invoice posting.
- **Monetarisierung:** self-service **upgrade/downgrade**, trial, Stripe customer portal, webhook-driven plan gate.

## Build order (from ARCHITECTURE.md)

1. **Ledger/Buchhaltung core** (chart + balanced append-only journal + auto-posting) — foundation for everything that touches the books.
2. **Reports + USt-VA** (EÜR/GuV/BWA + VAT return).
3. **Banking** (finAPI sync + reconciliation reusing open items/PaymentService + SEPA). *Start finAPI vendor onboarding in parallel with step 1 — it is a licence-gated contractual dependency.*
4. **Belege & Ausgaben** (capture + OCR port + email + archive + expense posting) — largely parallel.
5. **Monetarisierung (Stripe)** — fully independent/parallel.

## Watch out for (from PITFALLS.md — the costly mistakes)

- **GoBD booking immutability + Festschreibung:** never edit posted bookings — counter-book (Storno). Lock fiscal periods. Balanced-per-entry enforced DB-side.
- **SKR mapping + VAT keys (Steuerschlüssel):** wrong account/VAT-key → wrong USt-VA. Pin per-fiscal-year Kennziffer mapping; distinguish **Soll- vs Ist-Versteuerung** and EÜR vs GuV vs Bilanz.
- **Reconciliation:** idempotent transaction import (dedupe), guard duplicate/partial matches, **PSD2 90-day re-consent**, finAPI sandbox≠prod, store bank data DSGVO-safe, SEPA PIS needs strong customer auth.
- **OCR:** **never auto-post** — always human review (accuracy + liability); DSGVO with cloud OCR (EU region/DPA); store originals immutably for 10 years.
- **Stripe:** verify webhook **signatures**, make handlers **idempotent**, Stripe subscription state is the source of truth (mirror → `tenants.plan`), handle proration/trial/dunning, never mix test/live keys.
- **Scope traps:** NO TSE/POS Registrierkasse; NO direct ERiC submission this milestone (calc + export only); NO Nordigen/own-FinTS/own-pain.001; NO card forms.
- **Cross-cutting:** every new table gets RLS ENABLE+FORCE + tenant policy; money stays `decimal`; extend the append-only/audit pattern to bookings + receipts.

## Explicit "do NOT add"

TSE/KassenSichV till, GoCardless/Nordigen, FinTS/HBCI, hand-rolled pain.001, own PSD2 licence, card-collection forms, US inbound-mail-parse SaaS as default, direct ERiC submission, a new charting library.

## Open questions for phase-level research

finAPI production tier + PIS/white-label eligibility & pricing; exact DATEV **EXTF** format-version + column order; per-fiscal-year **UStVA** Kennziffer/XSD; GoBD receipt **archive** storage (WORM vs. hash-chain); S/M/L/XL → Stripe **Price** mapping + proration/trial semantics; Azure DI accuracy on real German receipts.

## Sources

See STACK.md, FEATURES.md, ARCHITECTURE.md, PITFALLS.md in this directory (each with its own source list — finAPI docs, Stripe/NuGet, ELSTER dev portal, DATEV format docs, Azure DI docs, GoBD/TSE 2026 guidance).
