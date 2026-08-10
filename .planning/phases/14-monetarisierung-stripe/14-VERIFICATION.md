---
phase: 14-monetarisierung-stripe
verified: 2026-08-10T00:00:00Z
status: passed
score: 26/26 must-have truths verified (6 plans); 4/4 ROADMAP criteria; BILL-01..07 satisfied
re_verification: false
---

# Phase 14: Monetarisierung (Stripe) Verification Report

**Phase Goal:** Die Tarife S/M/L/XL werden echt verkaufbar — Self-Service-Abo ueber Stripe; ein signierter, idempotenter Webhook setzt die Berechtigung serverautoritativ und treibt das bestehende tenants.plan-Gate.
**Verified:** 2026-08-10
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### ROADMAP Success Criteria

| # | Criterion | Status | Evidence |
| - | --------- | ------ | -------- |
| 1 | Paid plan via hosted Checkout; signed idempotent webhook sets entitlement server-authoritatively, no re-login | VERIFIED | StripeBillingProvider.SessionCreateOptions (mode=subscription); BillingWebhookHandler EventUtility.ConstructEvent -> tenant-agnostic dedupe -> fresh scope + SetTenant before DbContext -> tenant.Plan = plan + SaveChangesAsync. Idempotency: unique-violation on processed_stripe_event -> 200 skip. Human-verified live (14-07). |
| 2 | Portal upgrade/downgrade with proration; gate reacts; payment method + invoices visible | VERIFIED (auto) / HUMAN | CreatePortalSessionAsync; customer.subscription.updated re-maps plan via SubscriptionPlanMapper; frontend openPortal() -> window.location.assign. Proration/invoice visibility Stripe-hosted + human-verified (14-07). |
| 3 | Trial grants full access then auto-converts; failed payment -> Smart Retries + orderly degradation | VERIFIED | RegistrationService.TrialEndsAt = UtcNow.AddDays(14); BillingStateService compute-on-read (trialing/active/past_due OR trial-active -> full access); past_due NOT degraded (mapper returns currentPlan); canceled/unpaid/deleted -> Free; invoice.paid reactivates; BillingDegradationWriteGuardMiddleware blocks writes (403) allows GET. |
| 4 | Numera bills own subscription VAT-correct incl. EU-B2B reverse-charge | VERIFIED (auto) / HUMAN | SessionAutomaticTaxOptions + SessionTaxIdCollectionOptions + CustomerUpdate in checkout -> Stripe Tax handles 19% domestic + para 13b reverse-charge. Human-verified live (14-07). No custom VAT math (per D2). |

### Observable Truths (per plan must_haves)

| Plan | Truths | Status |
| ---- | ------ | ------ |
| 01 — Data model | trial deadline persisted; Stripe/subscription columns; Free=deny-by-default; tenant-agnostic dedupe table | 4/4 |
| 02 — Port + stub | provider-neutral IBillingProvider; no-network stub default; scriptable fake; locally-signed HMAC fixture | 4/4 |
| 03 — Stripe + config-gate | real-vs-stub gate on both keys; hosted Checkout w/ AutomaticTax + TaxId; Portal URL; status endpoint | 4/4 |
| 04 — Webhook | 400 on bad/missing signature (anonymous); checkout flips plan server-authoritative; idempotent dedupe; canceled/unpaid/deleted->Free + invoice.paid reactivate; past_due not degraded | 5/5 |
| 05 — Degradation | compute-on-read (no job); trialing/active/past_due/in-trial = full write; lapsed = write-blocked but GET-allowed; no data loss/login wall + re-enable on payment; write-guard authoritative, EntitlementService NOT rewired | 5/5 |
| 06 — Frontend | pricing S/M/L/XL -> hosted Checkout redirect; plan + trial days shown; manage-billing -> Portal; app-wide degradation banner | 4/4 |

**Score:** 26/26 truths verified.

### Required Artifacts

| Artifact | Status | Details |
| -------- | ------ | ------- |
| Tenant.cs | VERIFIED | StripeCustomerId/StripeSubscriptionId/SubscriptionStatus/CurrentPeriodEnd/TrialEndsAt present; TenantPlan.Free = 5 |
| ProcessedStripeEvent.cs | VERIFIED | [Index(EventId, IsUnique)]; required EventId |
| NumeraDbContext.cs | VERIFIED | DbSet ProcessedStripeEvents; ToTable(processed_stripe_event) unique EventId; no tenant/RLS query filter (tenant-agnostic) |
| Migration 20260810141528_Billing | VERIFIED | AddColumn x5 on tenants; processed_stripe_event table + unique ix_...event_id; GRANT SELECT,INSERT ... TO numera_app (no RLS policy) |
| PlanCapabilityMap.cs | VERIFIED | Free intentionally absent from matrix -> empty capability set |
| RegistrationService.cs | VERIFIED | trialEndsAt = UtcNow.AddDays(14) -> TrialEndsAt = trialEndsAt |
| Numera.Modules.Billing.csproj | VERIFIED | No Stripe.net reference (grep for using Stripe = NONE) |
| IBillingProvider.cs / StubBillingProvider.cs | VERIFIED | 3-method port; stub throws NotSupported (no network) |
| FakeBillingProvider.cs / StripeWebhookFixture.cs | VERIFIED | Fake implements IBillingProvider; fixture builds t=,v1=HMACSHA256 header |
| StripeBillingProvider.cs | VERIFIED | over IStripeClient; AutomaticTax + TaxIdCollection + CustomerUpdate; item-level firstItem.CurrentPeriodEnd |
| BillingModuleServiceCollectionExtensions.cs | VERIFIED | gate requires BOTH SecretKey + WebhookSecret -> Stripe; else Stub |
| BillingEndpoints.cs | VERIFIED | checkout/portal/status RequireAuthorization; webhook anonymous raw-body |
| BillingWebhookHandler.cs | VERIFIED | ConstructEvent -> dedupe scope -> resolve -> fresh scope SetTenant-first -> flip plan |
| SubscriptionPlanMapper.cs | VERIFIED | past_due->currentPlan; canceled/unpaid->Free; deleted->Free; invoice.paid->PlanForPriceId |
| BillingStateService.cs | VERIFIED | IsDegradedAsync/EffectivePlanAsync compute-on-read from status+trial |
| BillingDegradationWriteGuardMiddleware.cs | VERIFIED | skip if not degraded; IsAllowed check; 403 otherwise |
| BillingReadOnlyAccessPolicy.cs | VERIFIED | GET/HEAD/OPTIONS allowed; billing recovery paths allowed; other writes blocked |
| web billing feature (billingApi/BillingPage/PricingCards/DegradationBanner/App.tsx) | VERIFIED | checkout/portal -> window.location.assign; useBillingStatus; PricingCards S/M/L/XL -> startCheckout; banner gated on degraded; /billing route + nav + banner mount |
| billing.json (de/en) | VERIFIED | Path corrected to repo convention web/src/i18n/locales/{de,en}/billing.json (registered in i18n/index.ts); plan-frontmatter path was aspirational — documented deviation, not a gap |
| Test files (3) | VERIFIED | ProviderRegistration asserts StubBillingProvider; Webhook asserts 400 bad-sig + paid->Free->paid + dedupe + past_due; DegradationGuard asserts 403/read-allowed |

### Key Link Verification

| From | To | Status | Details |
| ---- | -- | ------ | ------- |
| RegistrationService -> Tenant.TrialEndsAt | set at creation | WIRED | TrialEndsAt = trialEndsAt (now+14d) |
| NumeraDbContext -> processed_stripe_event | tenant-agnostic DbSet | WIRED | no RLS/tenant filter; unique event_id |
| Api.csproj -> Billing module | ProjectReference | WIRED | line 40 |
| StripeWebhookFixture -> Stripe-Signature | real HMAC | WIRED | t=unix,v1=hex HMACSHA256 |
| Program.cs -> AddBillingProvider | DI after AddBankConnectionProvider | WIRED | lines 153-154 |
| BillingEndpoints -> CreateCheckoutSessionAsync | checkout returns session.Url | WIRED | line 78 |
| BillingEndpoints -> POST /api/billing/webhook | anonymous raw-body, before middleware pipeline | WIRED | mapped line 226 (after /health, before UseMiddleware at 233+) — bypasses auth + degradation guard |
| WebhookHandler -> ICurrentTenant.SetTenant | fresh scope, SetTenant before DbContext | WIRED | lines 107-110 |
| WebhookHandler -> processed_stripe_event | insert before tenant resolution; skip on dup | WIRED | lines 69-88 |
| Program.cs -> BillingDegradationWriteGuardMiddleware | after TenantResolutionMiddleware, before ReadOnlyWriteGuardMiddleware | WIRED | lines 233-235 |
| WriteGuardMiddleware -> IBillingState | consult effective state | WIRED | InvokeAsync(ctx, IBillingState) |
| billingApi -> checkout/portal | fetch -> window.location.assign | WIRED | lines 27-38 |
| DegradationBanner -> GET /api/billing/status | useBillingStatus().degraded gates banner | WIRED | returns null unless degraded is true |

### Requirements Coverage

| Requirement | Status | Evidence |
| ----------- | ------ | -------- |
| BILL-01 hosted Checkout, no card data in Numera | SATISFIED | hosted SessionCreateOptions; zero card-input elements in web/src (grep NONE) |
| BILL-02 signed idempotent webhook -> tenants.plan | SATISFIED | ConstructEvent + dedupe + plan flip; tests + live |
| BILL-03 Portal upgrade/downgrade w/ proration | SATISFIED | Portal session + subscription.updated remap; human-verified proration |
| BILL-04 trial full access then auto-convert | SATISFIED | 14d trial + compute-on-read + Checkout conversion |
| BILL-05 dunning + orderly degradation | SATISFIED | past_due preserves; canceled/unpaid->Free; write-guard 403 |
| BILL-06 manage payment method + view invoices | SATISFIED | Customer Portal; human-verified |
| BILL-07 VAT-correct incl. EU-B2B reverse-charge | SATISFIED | Stripe Tax AutomaticTax + TaxIdCollection; human-verified para 13b |

### Anti-Patterns Found

None. Grep for TODO/FIXME/PLACEHOLDER/NotImplementedException across billing source (src Stripe/Auth, module, web feature) returned NONE. Stub NotSupportedException is intentional (config-gated no-network default), not an incomplete implementation.

### Human Verification Required

None outstanding. The inherently-human items (live Checkout redirect, real proration, actual Stripe Tax computation, failed-payment->dunning->reactivation loop, idempotency on live resend) were executed against a live Stripe TEST account and APPROVED by the user on 2026-08-10 (14-07-SUMMARY.md).

### Gaps Summary

No gaps. All 6 execution plans must-haves are present, substantive, and wired in the actual codebase. Config-gate defaults to the no-network stub (CI-safe); the real StripeBillingProvider activates only when both SecretKey and WebhookSecret are set. The webhook is anonymous, raw-body, signature-verified, tenant-agnostically deduped, and flips tenants.plan server-authoritatively in a fresh SetTenant-first scope. Degradation is compute-on-read via a dedicated write-guard ordered after tenant resolution and before the read-only guard, leaving EntitlementService intentionally un-rewired (advisory). Deferred scope (metered billing, coupons, affiliate, custom dunning content, SKR self-invoice booking, live-mode) was correctly excluded per 14-CONTEXT.md and is not treated as a gap. Test evidence (Platform 131, Integration 311/1-skip incl. Billing filters, web vitest 56) and the approved live TEST-mode checkpoint corroborate the static analysis.

---

_Verified: 2026-08-10_
_Verifier: Claude (gsd-verifier)_
