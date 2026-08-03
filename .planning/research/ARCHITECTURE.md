# Architecture Research

**Domain:** German accounting/invoicing SaaS — v2.0 new domains (Buchhaltung, Banking, Belege & Ausgaben, Monetarisierung) integrated into the existing Numera modular monolith
**Researched:** 2026-08-02
**Confidence:** HIGH on integration into the existing architecture (codebase mapped this session); MEDIUM on external-provider event/webhook specifics (finAPI, Stripe — verify in the owning phase)

---

## Existing architecture (integrate WITH, do not redesign)

- **.NET 10 modular monolith.** `Numera.Platform.*` kernel (Db/Money/Tenancy/Audit/Entitlements) + feature modules `Numera.Modules.{Crm,Catalog,Sales,Ledger}` + `Numera.Api` (minimal-API BFF) + `Numera.Worker` (Hangfire host).
- **Postgres 18, one database.** Every tenant table `ENABLE + FORCE ROW LEVEL SECURITY` with a `tenant_isolation` policy keyed on the `app.current_tenant` GUC. Migrations run as `numera_migrator` (owns schema); the app runs as `numera_app` (no BYPASSRLS). EF Core 10 with global named query filters (Tenant + NotArchived).
- **Money** = `decimal`, EN-16931 rounding (`RoundingPolicy`, NodaMoney available). Never float.
- **Immutability = DB-side.** Finalized documents / audit / payments use `REVOKE UPDATE/DELETE` + append-only triggers. Domain events fire **after** the finalize transaction commits (`InProcessDomainEventPublisher`).
- **Async = Hangfire** (Postgres storage) on the Api's default queue (PDF render, email, e-invoice generation, recurring invoices). `SetTenant` re-establishes RLS inside each job.
- **Auth** = Keycloak BFF (OIDC cookie); `TenantResolutionMiddleware` maps the `organization` claim → `tenant_id`; `ReadOnlyWriteGuardMiddleware` enforces the TaxAdvisor read-only role.
- **Entitlements** = `PlanCapabilityMap` (S/M/L/XL → Capability set); server-authoritative gates read `tenants.plan` per request.
- **`Numera.Modules.Ledger` already exists** with `accounts`, `journal_entries`, `postings` — the seam for the Buchhaltung engine (currently minimal/inert).

## Module ownership (new work)

| Domain | Module | New? | Rationale |
|--------|--------|------|-----------|
| Buchhaltung | `Numera.Modules.Ledger` | **Extend** | Journal/accounts/postings already live here; grow it into the double-entry engine + reports + USt-VA. |
| Banking | `Numera.Modules.Banking` | **New** | finAPI integration, bank accounts, transactions, reconciliation, SEPA. Isolated external-provider surface. |
| Belege & Ausgaben | `Numera.Modules.Belege` | **New** | Receipt capture, OCR port, email intake, immutable archive, expense/supplier-invoice posting. |
| Monetarisierung | `Numera.Api` (Billing area) or `Numera.Modules.Billing` | **New (thin)** | Stripe is a platform concern (drives `tenants.plan`); keep it near Api/Platform, not a business module. |

## New tables + RLS (all tenant-scoped, RLS ENABLE+FORCE)

- **Ledger:** extend `accounts` (SKR03/04 seed, per-tenant chart), `journal_entries` + `postings` become **append-only** (balanced-per-entry DB trigger: Σ debit = Σ credit; REVOKE UPDATE/DELETE for `numera_app`); `fiscal_periods` (with a `locked`/Festschreibung flag); `vat_return` (USt-VA header + Kennziffern snapshot). Booking is immutable → corrections are counter-bookings (Storno), mirroring the invoice model.
- **Banking:** `bank_connection` (finAPI connection id + consent expiry), `bank_account` (IBAN, balance), `bank_transaction` (append-only, imported; dedupe key), `reconciliation_match` (transaction ↔ open_item/payment, with confidence + user-confirmed flag), `sepa_transfer` (PIS order + status).
- **Belege:** `receipt` (immutable original bytes/blob ref, source: upload/camera/email, GoBD 10-year retention), `receipt_extraction` (OCR read-model: vendor, date, gross, VAT, confidence), `expense`/supplier-invoice link into Ledger postings. Reuse the append-only + immutable-original pattern from `inbound_document` (Phase 5) and `customer_file` (Phase 8).
- **Billing:** `subscription` (Stripe customer/subscription id, plan, status, current_period_end, trial_end) — the mirror of Stripe state that drives `tenants.plan`. Stripe is the source of truth; this table is the local projection.

## Data-flow changes (the important integrations)

1. **Invoice/payment → automatic booking.** On `InvoiceFinalized` and on payment-recorded domain events, the Ledger module posts the double-entry booking (e.g. Forderungen aLuL / Umsatzerlöse / USt) using the SKR mapping + VAT key. Post-commit, via the existing domain-event seam. This makes the books a *projection* of the already-immutable sales facts — no double data entry.
2. **Bank transaction → reconciliation → payment → open item.** A Hangfire job pulls finAPI transactions (idempotent import, dedupe), a matcher proposes `reconciliation_match` against open items (amount + reference + date + partner IBAN), the user confirms, and confirmation **reuses the existing `PaymentService.RecordAsync`** → open-item status + Ledger booking flow for free.
3. **Receipt → OCR → expense booking → EÜR/USt-VA.** Upload/email/camera → immutable `receipt` → Hangfire OCR job via an `IReceiptExtractor` port (Azure AI Document Intelligence, EU region) → a **booking proposal** the user reviews (never auto-post) → expense posting into Ledger → flows into EÜR/GuV and the USt-VA Vorsteuer.
4. **Stripe webhook → plan change → entitlements.** Stripe Checkout/Customer-Portal events hit a signed, **idempotent** webhook endpoint → update `subscription` → set `tenants.plan` → the existing server-authoritative `PlanCapabilityMap` gate reacts immediately (no re-login; plan read per request).

## Sync vs. async (Hangfire)

- **Hangfire jobs:** finAPI transaction sync (recurring per connection, tenant-safe, idempotent — mirror the recurring-invoice job), OCR extraction, SEPA status polling, Stripe webhook heavy-processing (enqueue-after-verify).
- **Synchronous:** booking on finalize/payment (in/after the same commit for atomicity), reconciliation confirmation (user action), USt-VA calculation (on demand), Stripe webhook signature verify + fast ACK.

## External integration points

- **finAPI:** OAuth client-credentials + user tokens; Web Form 2.0 (RegShield) so **Numera needs no own PSD2 licence**; polling + optional notifications; PSD2 90-day re-consent. No official .NET SDK → generate a typed client from their OpenAPI. Sandbox before prod; onboarding/pricing is a **contractual, licence-gated dependency — start immediately**.
- **Stripe:** `Stripe.net` (hosted Checkout + Customer Portal, no card data touches Numera), signed idempotent webhooks as source of truth, test vs. live keys.
- **OCR:** `IReceiptExtractor` port; Azure AI Document Intelligence pinned to an **EU region** + DPA (DSGVO); self-hosted PaddleOCR/docTR escape hatch.
- **Email intake:** own per-tenant catch-all mailbox polled by MailKit (already in stack) — not a US inbound-parse SaaS; verify SPF/DKIM, guard spoofing.

## Suggested build order (dependency-aware)

1. **Ledger/Buchhaltung core** — chart of accounts (SKR03/04 seed), append-only balanced journal, auto-posting from finalize/payment. *Everything else that touches the books depends on this.*
2. **Reports + USt-VA** — EÜR/GuV/BWA (QuestPDF) + USt-Voranmeldung calculation/export. Consumes the journal.
3. **Banking** — finAPI connect + transaction sync + reconciliation (reuses open items + PaymentService) + SEPA. *Start finAPI vendor onboarding in parallel with step 1 (licence-gated).*
4. **Belege & Ausgaben** — capture + OCR port + email intake + immutable archive + expense posting (feeds EÜR/USt-VA). Largely parallel; depends on the Ledger for posting.
5. **Monetarisierung (Stripe)** — fully parallel/independent; drives the existing `tenants.plan` gate.

## What NOT to build (architectural anti-recommendations, from STACK/FEATURES)

- No TSE/KassenSichV Registrierkasse/POS — the *elektronisches Kassenbuch* is a cash journal, not a certified till.
- No GoCardless/Nordigen (closed to new signups); no own FinTS/HBCI or hand-rolled `pain.001` (finAPI PIS instead).
- No own PSD2 licence (finAPI RegShield/Web Form covers it).
- No card forms in Numera (Stripe hosted Checkout/Portal).
- No US inbound-mail SaaS as default; no direct ERiC submission in this milestone (USt-VA = calc + export only).

## Open questions (phase-level)

finAPI production tier + PIS/white-label eligibility; exact DATEV EXTF format-version + column order; per-fiscal-year UStVA Kennziffer/XSD mapping; GoBD receipt archive storage (WORM vs. hash-chain over the existing append-only pattern); S/M/L/XL → Stripe Price mapping + proration/trial semantics.

## Sources

Grounded in the mapped Numera codebase (src/platform, src/modules/Numera.Modules.Ledger, Sales finalize/payment flow, Hangfire jobs, RLS + entitlements) and the v2.0 STACK.md / FEATURES.md / PITFALLS.md research in this directory.
