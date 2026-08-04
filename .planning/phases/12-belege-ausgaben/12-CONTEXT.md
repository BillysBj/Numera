# Phase 12 Context — Belege & Ausgaben

**Captured:** 2026-08-04 (orchestrator decision questions; no full /gsd:discuss-phase run)
**Source:** user decisions locking the OPEN QUESTIONS from 12-RESEARCH.md

## Decisions (LOCKED — honor exactly, do not revisit)

### D1 — OCR: `IReceiptExtractor` port + STUB default now; Azure adapter behind it, wired live later
Build the full capture → extract → human-review → confirm → book flow AND the `IReceiptExtractor` port this phase. The DEFAULT registered extractor is a stub/manual-entry implementation (returns empty/low-confidence fields so the user fills them in) so the phase ships with ZERO cloud dependency and no per-page cost. ALSO implement the Azure AI Document Intelligence adapter (`prebuilt-invoice`, EU region, DPA-ready) behind the same port, but do NOT make it the default — it is wired live (config/DI swap) only when the user green-lights the Azure account/DPA/budget. Every extractor path returns structured fields WITH per-field confidence for review. **NEVER auto-book** — OCR and e-invoice both only *propose*; a human confirms before any JournalEntry is written (hard rule, all tiers).

### D2 — Email intake (BELEG-07) IS IN SCOPE this phase
Build the per-tenant unique catch-all email address + MailKit **IMAP** polling as a Hangfire recurring job, mandantengetrennt (address → tenant routing, job calls `SetTenant` for RLS). Extract attachments (PDF/image) → Beleg (same review/confirm/book flow). Idempotent/dedup on Message-Id + content hash (never double-import a re-forwarded mail). The mailbox is CONFIG-DRIVEN (host/creds/domain from configuration) — the catch-all domain + IMAP credentials are an external provisioning dependency the user will supply; make the intake testable in integration tests with a containerized IMAP server (e.g. GreenMail or equivalent) rather than a live mailbox. Address scheme (`belege-{token}@{domain}` vs sub-addressing) is planner's discretion.

### D3 — Expense VAT scope: 19% / 7% + 0%/steuerfrei (NO reverse-charge this phase)
The confirm→book path covers standard-rated expenses (19% and 7% → Aufwand + abziehbare Vorsteuer, unlocking USt-VA **Vorsteuer Kz 66** and real **EÜR Betriebsausgaben**) PLUS a 0%/steuerfrei path (expense booked with NO Vorsteuer leg). Extend the Phase-10 `ExpensePostingSource`/`ExpenseMapping` minimally for the 0%/steuerfrei case (it currently throws for rates ≠ 7/19). **OUT OF SCOPE:** §13b and innergemeinschaftlicher Erwerb reverse-charge expenses and their USt-VA Kz (89/61/46/47) — deferred to a later phase. Multi-rate e-invoices book N expense legs (one per breakdown row).

### D4 — Attachment malware scanning IS IN SCOPE (ClamAV) this phase
Add a ClamAV sidecar (new stack container) and scan EVERY uploaded and emailed attachment BEFORE it is archived or booked. An infected/failed-scan attachment is rejected/quarantined (not archived as a valid Beleg, never booked) with a clear status. Type + size validation applies as well. Make the scan seam abstracted/testable (a port with a ClamAV adapter + a test double) so integration tests don't require a live ClamAV for every case.

## Claude's Discretion (freedom areas — adopt the 12-RESEARCH.md recommendations)
- **GoBD WORM archive (BELEG-04)** = Postgres `bytea` immutable store, reusing the proven append-only pattern (REVOKE UPDATE/DELETE + immutability trigger, RLS ENABLE+FORCE+tenant_isolation) from `customer_file`/`inbound_document`/Payments — copy that migration template. Store the ORIGINAL bytes + format, indexed metadata (supplier, date, amounts, hash), 10-year retention, linked to the Beleg + resulting JournalEntry. NOT object storage (bytea is proven + keeps unified RLS/immutability); note the volume caveat.
- **Beleg domain model** = one converged `receipt` aggregate over both tiers with a state machine (Draft/Extracted → Confirmed → Booked, + Rejected/Quarantined for AV). Source = camera | upload | email | e-invoice. For Tier A (e-invoice) it LINKS the existing `inbound_document` rather than duplicating its parse — reuse Phase-5 `InboundParser`/`InboundEInvoiceService`/`SupplierMatcher`.
- **Booking (BELEG-05)** mirrors the invoice/payment hooks: build an `ExpensePostingInput` from the confirmed fields and call `PostingEngine.PostAsync(new ExpensePostingSource(...), header, ct)` inside a transaction (SourceType=Expense, SourceRef=receipt id, idempotent on source). Reuse `AccountResolver.ResolveExpense`/`ResolveInputTax`.
- **Supplier match + dedup (BELEG-06)** reuse v1 CRM `BusinessPartner` (IsSupplier + CreditorAccount); match by VAT-id (like inbound e-invoice) + name/IBAN; dedup by (supplier + invoice number + amount + date) or content hash.
- All new tables get RLS ENABLE+FORCE+tenant_isolation; all Hangfire jobs (OCR, IMAP) call `SetTenant`; money stays `decimal` (never float from OCR).

## Deferred Ideas (OUT of scope — do NOT include)
- Live Azure Document Intelligence as the DEFAULT extractor (implement the adapter, keep it behind the port, wire later per D1).
- §13b / innergemeinschaftlicher Erwerb reverse-charge expenses + USt-VA Kz 89/61/46/47 (D3).
- Object storage for archived originals (D-discretion: bytea).
- Positionsgenaue Line-Item-Extraktion (BELEG-D3, v2.1); gelernte Lieferanten-Buchungsregeln (BELEG-D1, v2.1); Drei-Wege-Abgleich (BELEG-D2).
- Own OCR model training; US inbound-mail-parse SaaS.

## No-Fabrication / GoBD rule
Never auto-book an unconfirmed extraction. Never edit/delete an archived original (WORM; corrections = new version/Storno). Every booked expense traces to a confirmed Beleg + its archived original. OCR-extracted amounts are proposals with confidence, never authoritative until a human confirms.
