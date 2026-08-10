# 14-07 SUMMARY — End-to-end human-verify (Stripe TEST mode)

**Plan:** 14-07 (Wave 5, blocking checkpoint:human-verify)
**Outcome:** ✅ **APPROVED** by the user (2026-08-10)
**Type:** manual verification — no source changes.

## Pre-flight (automated, green)
- `Numera.sln` builds Release `--no-restore` — 0 errors.
- Integration suite (Testcontainers postgres:18 as `numera_app`): **311 passed / 1 skipped** (live-KoSIT, no sidecar) — incl. the Billing filters:
  - `BillingProviderRegistration` (2) — empty config → StubBillingProvider; keyed → StripeBillingProvider + IStripeClient.
  - `BillingWebhook` — signed-fixture paid→Free→paid transitions, idempotency (single `processed_stripe_event` row, no flap), bad-signature → 400.
  - `BillingDegradationGuard` — lapsed-trial/canceled → writes 403 `billing_read_only`, reads 200, re-subscribe endpoints reachable; `past_due` NOT degraded.
- Platform tests: **131 passed**.
- Web: lint 0 errors, build ok, vitest **56 passed**; zero card-input elements.

## Human verification (approved)
The user confirmed the full loop against a live Stripe **TEST** account:
1. **BILL-01/02** — hosted Checkout starts a paid plan; the live `checkout.session.completed` webhook flips `tenants.plan` server-authoritatively with **no re-login**.
2. **BILL-03/06** — Customer Portal upgrade/downgrade with **proration**; the gate reacts; payment method + subscription invoices are visible.
3. **BILL-07** — subscription invoice is **USt-correct**: 19% domestic; **EU-B2B reverse-charge (0%, §13b)** applied via **Stripe Tax** (no custom VAT math).
4. **BILL-04 + D4** — no-card 14-day trial grants full access ("Testphase: noch N Tage"); on lapse the tenant degrades to **read-only** (banner shown, reads/data intact, writes refused, no login wall, no data loss); starting a paid plan restores writes.
5. **BILL-05** — failed payment → Smart Retries/dunning → canceled/unpaid → orderly read-only degradation; a later successful Portal payment **re-activates** the paid plan and restores writes.
6. **Idempotency** — resending a delivered webhook event does not double-apply.

## Decisions honored (D1–D4)
- D1: Stripe TEST-mode behind `IBillingProvider` + config-gate; stub in CI + signed fixtures → CI never contacts Stripe.
- D2: Stripe Tax automatic incl. EU-B2B reverse-charge; Stripe-issued invoices referenced via Portal; not booked into the tenant SKR ledger.
- D3: no-card app-managed 14-day trial; server-authoritative `trial_ends_at`; conversion is an explicit Checkout action.
- D4: read-only/Free degradation via the dedicated write-guard (authoritative); data intact; re-activation on successful-payment webhook.

## Result
All four ROADMAP success criteria + BILL-01..07 verified live in TEST mode. **No defects reported.** Phase 14 ready to mark complete.
