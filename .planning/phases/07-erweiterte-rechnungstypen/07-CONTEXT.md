# Phase 7 Context — Erweiterte Rechnungstypen

Decisions locked before planning (4 by Claude as architect, 2 by the user). No `/gsd:discuss-phase` was run; these resolve the 6 open questions from `07-RESEARCH.md`.

## Decisions (LOCKED — plans must honor exactly)

1. **Recurring scheduling architecture** — one Hangfire **recurring job per template**, with `tenantId` baked into the job args, re-entering the proven per-scope `ICurrentTenant.SetTenant(tenantId)` pattern (as in `SendDunningNoticeJob`). NO cross-tenant enumeration, NO `BYPASSRLS` role, NO central multi-tenant dispatcher. This keeps the "never bypass RLS" invariant (verified: no DB role can enumerate all tenants under FORCE RLS).

2. **Foreign-currency exchange rate** — user-entered and **frozen at finalize** (deterministic + GoBD). NO external/live rate feed in v1 (an ECB "suggest" prefill may come later). VAT shown in EUR per EN 16931 BT-6 (VAT accounting currency = EUR) + BT-111 (VAT total in EUR), mandatory under BR-53; the document total stays in the foreign currency (BT-5).

3. **Currency scope** — restrict v1 to 2-minor-unit currencies (EUR/USD/GBP/CHF/…). JPY/BHD-style (0/3 minor units) are OUT of scope (keeps `numeric(19,4)` + 2-dp `RoundingPolicy` clean).

4. **Abschlagsrechnung / Schlussrechnung** — new `DocumentType` enum values (append-only ordinals), NOT flags on `Rechnung`. Consistent with the existing polymorphic single-table discriminator. Down-payment offset in the final invoice uses the itemized-gross-deduction method → EN 16931 BT-113 (prepaid amount) so BT-115 payable = BT-112 − BT-113 + BT-114 (BR-CO-16); the resulting **OpenItem uses the residual `AmountDue`, not the gross** (research-flagged trap). No double taxation.

5. **Recurring finalization mode (USER)** — generated recurring invoices are **auto-finalized by default** (numbered, immutable, and auto-sent if the template configures it), with a **per-template opt-out toggle** to instead produce a Draft for manual review. Satisfies INV-06 "automatisch erzeugt" while allowing per-template caution. Auto-finalize must stay race-safe (reuse `FinalizeCoreAsync`/`NumberingService`) and idempotent per period (no double-generation; catch-up after downtime).

6. **Tarif-Gating (USER: gate to higher tiers)** — add three new `Capability` values and gate the features server-side via `IEntitlementService` (authoritative, not client flags), with upgrade-hint UX (not errors). **Proposed tier (Claude): all three at L+ (granted by L and XL)**, consistent with the existing "professional finance" grouping where `Dunning` + `EInvoicing` already live at L:
   - `ForeignCurrencyInvoicing` → L+ (INV-05)
   - `RecurringInvoices` → L+ (INV-06)
   - `DownPaymentInvoices` → L+ (INV-07)
   Capabilities are strictly increasing per tier (S ⊆ M ⊆ L ⊆ XL) — add to the L and XL sets in `PlanCapabilityMap`. (If the user later wants recurring at M for broader upsell, that's a one-line map change.)

## Claude's Discretion (freedom areas)
- Exact schema/column names, plan/wave split, contract shapes, PDF/label details, and which existing tests to extend.
- Recommended build order from research: INV-07 → INV-05 → INV-06 (recurring reuses the settled finalize path last).

## Deferred Ideas (OUT of scope for Phase 7)
- Live/ECB exchange-rate feed or auto-suggest prefill.
- 0/3-minor-unit currencies (JPY/BHD).
- Multi-currency payment reconciliation beyond what Phase 6 already does.
