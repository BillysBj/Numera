# Phase 14 Context — Monetarisierung (Stripe)

**Captured:** 2026-08-06 (orchestrator decision-questions at plan-phase time)
**Requirements:** BILL-01, BILL-02, BILL-03, BILL-04, BILL-05, BILL-06, BILL-07
**Goal (roadmap):** Die Tarife S/M/L/XL werden echt verkaufbar — Self-Service-Abo über Stripe, das serverautoritative `tenants.plan`-Gate wird von Stripe-Webhooks getrieben. (Unabhängiger Track.)

## Decisions (LOCKED — honor exactly, do not revisit)

### D1 — Stripe wiring: TEST-mode live behind a port + config-gate + fake for CI
Real Stripe.NET calls against Stripe **TEST mode**, hidden behind an `IBillingProvider` (or equivalent) port with config-gated API keys/price IDs. A **fake/stub provider** + **signed webhook fixtures** (Stripe-CLI-style, real signature verification against a test secret) so **CI never contacts Stripe**. Same architecture shape as Phase 13's finAPI-sandbox provider + `FakeBankConnectionProvider`. Flipping to live keys is a later config-only change. Webhooks are the source of truth for entitlement.

### D2 — VAT: Stripe Tax (automatic), incl. EU-B2B Reverse-Charge
Enable **Stripe Tax**. Stripe computes German USt (19%) for domestic B2C/B2B, validates EU VAT-IDs and applies **Reverse-Charge (0%, §13b note)** for EU-B2B, and generates the subscription invoices. We **store/reference** the resulting Stripe invoices + tax lines (for the customer's records via the Portal); we do NOT re-implement VAT math. Numera's own-subscription billing is Stripe-issued — this is the company's *outbound* self-billing, separate from the tenant's accounting ledger (do NOT book Numera's own Stripe invoices into the tenant's SKR ledger).

### D3 — Trial: no-card, app-managed, 14 days
Trial starts at signup with **full access, NO card required**. A server-authoritative `trial_ends_at` (on the tenant) drives the gate; when it lapses, access **degrades** (see D4 target state) until the user starts a paid plan via Stripe Checkout. Conversion is an explicit user action (start paid plan), NOT an automatic card charge. Trial length = **14 days**.

### D4 — Failed-payment / trial-lapse degradation: downgrade to read-only/Free gate (NO data loss)
After Stripe **Smart Retries + dunning** are exhausted (subscription `past_due` → `canceled`/`unpaid` webhook), OR the no-card trial lapses, the tenant's `tenants.plan` gate drops to a **read-only / Free capability set**: data stays intact and fully visible, but write/premium features lock until payment resumes/starts. **Never** a hard login wall; **never** delete or hide data. Re-activation on a subsequent successful-payment webhook restores the paid capability set.

## Claude's Discretion (freedom areas — choose the implementation)
- Exact port/interface shape + where it lives (mirror `IBankConnectionProvider`/finAPI module placement conventions).
- Stripe object modeling: number of Products/Prices, monthly vs monthly+annual intervals, whether price IDs are config or Stripe-synced. (Recommend: 4 products S/M/L/XL, config-mapped price IDs; monthly at minimum, annual optional if cheap.)
- Webhook idempotency mechanism (processed-event table vs. Stripe event-id dedupe) — mirror the Phase-13 dedupe/`Processed*` patterns.
- How the read-only/Free capability set is expressed against the existing v1 `Capability`/entitlement system + `tenants.plan`.
- Customer Portal configuration (which actions enabled: payment method, invoices, upgrade/downgrade with proration).
- Frontend: pricing/upgrade page, current-plan/trial-status surface, "Manage billing" → Portal redirect, degradation banner.
- Whether a recurring job is needed for trial-lapse detection (app-managed trial has no Stripe webhook) vs. compute-on-read gate.

## Deferred Ideas (OUT OF SCOPE — do NOT include)
- Annual/multi-year commitment discounts beyond a simple monthly (+optional annual) price — no coupon/promo-code engine this phase.
- Usage-based / metered billing, seat-based add-ons beyond the existing MultiUser capability.
- Affiliate/referral, dunning-email *content* design beyond Stripe's built-in dunning + one degradation banner.
- Booking Numera's own Stripe subscription invoices into the tenant's SKR accounting ledger (explicitly excluded per D2).
- Live-mode go-live / real card processing (test-mode only this phase; live is a later config flip).

## Notes for research/planning
- Independent track — depends only on v1's server-authoritative `tenants.plan` gate + Upgrade-Hinweis UX, NOT on Phases 10–13.
- Stack: Stripe.NET (official), hosted Stripe Checkout + Customer Portal (no card forms in Numera — PROHIBITED per platform rules anyway), webhooks = source of truth.
- Reuse the Phase-13 provider+config-gate+fake+signed-fixture testing pattern; reuse v1 entitlement/`Capability` gate rather than inventing a new one.
