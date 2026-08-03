# Feature Research

**Domain:** German accounting/invoicing SaaS — v2.0 new domains (Buchhaltung, Banking, Belege & Ausgaben, Monetarisierung)
**Researched:** 2026-08-02
**Confidence:** HIGH for domain workflows (established German accounting practice, verified against Lexware Office / sevDesk / DATEV); MEDIUM for provider-specific API detail (finAPI, Stripe, ERiC).

> Scope note: This milestone ADDS four feature domains to a shipped v1.0. v1 already provides: multi-tenant + RLS, Owner/Employee/TaxAdvisor roles, CRM (customers/suppliers), product catalog, quote→order→delivery→invoice chain with GoBD immutability + Storno/Gutschrift, §14 invoices with EN-16931 VAT categories (19/7/0, §19, §13b, i.g.), foreign-currency/recurring/down-payment invoices, PDF + email, XRechnung/ZUGFeRD create + KoSIT validation, **inbound e-invoice receive/parse/match**, **open items + manual payments + multi-stage dunning**, product catalog, **S/M/L/XL feature-gates (server-authoritative, no billing)**, PWA DE/EN, exact decimal money, immutable audit-log, DSGVO export, GoBD Verfahrensdokumentation draft. Do NOT re-specify these — the tables below reference them as dependencies.

---

## Domain 1 — Buchhaltung (Bookkeeping Engine, Reports, USt-VA)

The foundational domain. The chart of accounts + booking engine (Buchungslogik) is the substrate that EÜR, GuV, BWA and USt-Voranmeldung all read from. Nothing else in the accounting side works without it.

### How it actually works (domain workflow)

- **Kontenrahmen (chart of accounts):** German SMBs use standardized DATEV charts. **SKR03** orders accounts by business *process* (Prozessgliederungsprinzip); **SKR04** orders by *financial-statement* structure (Abschlussgliederungsprinzip). Both encode the same economics; the tenant picks one at setup and it's effectively immutable afterward. A tenant works with a small *active subset* of accounts, not all ~1,500.
- **Buchung (double-entry):** Every business event becomes a Soll/Haben (debit/credit) pair on accounts. The engine must map v1 document/payment events to accounts automatically:
  - *Outgoing invoice issued* → **Soll** Forderungen aus L&L / Debitor (SKR03 1400 / SKR04 1200) · **Haben** Erlöse 19% (SKR03 8400 / SKR04 4400) + Umsatzsteuer 19% (SKR03 1776 / SKR04 3806). Split lines per VAT rate; §13b/i.g. use dedicated tax keys with no output VAT on the invoice.
  - *Customer payment received* → **Soll** Bank (SKR03 1200 / SKR04 1800) · **Haben** Forderungen — clears the open item.
  - *Supplier invoice / expense* → **Soll** Aufwandskonto + Vorsteuer (SKR03 1576 / SKR04 1406) · **Haben** Verbindlichkeiten aus L&L / Kreditor (SKR03 1600 / SKR04 3300).
  - *Supplier payment made* → **Soll** Verbindlichkeiten · **Haben** Bank.
  - The mapping is driven by a **Steuerschlüssel/BU-key** table (tax key → rate + accounts) so users never hand-pick accounts for standard flows.
- **EÜR vs GuV vs BWA** — three different outputs, three different audiences:
  - **EÜR (Einnahmen-Überschuss-Rechnung, §4(3) EStG):** cash-basis (Zufluss/Abfluss) profit calc for Freiberufler and non-Bilanzierer. Income − expenses = profit. Maps onto the official **Anlage EÜR** ELSTER form. This is a tax filing.
  - **GuV (Gewinn- und Verlustrechnung, §275 HGB):** accrual-basis P&L for GmbH/UG and other Bilanzierende, part of the Jahresabschluss (with Bilanz). Legal statement structure (Gesamt-/Umsatzkostenverfahren).
  - **BWA (Betriebswirtschaftliche Auswertung):** a *management* report (DATEV standard BWA form 01), typically monthly, showing revenue, cost blocks and a provisional result vs. prior period. NOT a legal filing — a steering tool for the owner and tax advisor.
- **USt-Voranmeldung (VAT advance return):** periodic (monthly/quarterly) return. The engine sums booked turnover/VAT into **Kennziffern**: net revenue at 19% → **Kz 81**, at 7% → **Kz 86**, other rates → **Kz 35/36**; tax-free i.g. deliveries → **Kz 41**; i.g. acquisitions → **Kz 89** (tax) / **Kz 61** (deductible input tax on them); §13b reverse-charge → **Kz 46/47**; input tax (Vorsteuer) from incoming invoices → **Kz 66**; resulting Zahllast/Vorauszahlung → **Kz 83**. Filing is transmitted to the tax office as an **ELSTER/ERiC XML**. Per the v1 decision, v2 does **calculation + export** (XML/print for manual ELSTER portal upload); direct ERiC transmission is deferred.
- **Elektronisches Kassenbuch:** a digital cash book — chronological record of cash receipts/disbursements with running balance (Kassenbestand), used by businesses that handle physical cash. It is the digital equivalent of the paper Kassenbuch. **Critical distinction:** a Kassenbuch is NOT a Kassensystem/POS. Only an electronic *point-of-sale till* triggers the KassenSichV/**TSE** (certified technical security element) obligation. A pure cash book does not require a TSE — but it must be GoBD-conform (no gap-free deletion, immutable, chronological, no negative balance).

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| SKR03 + SKR04 chart of accounts, tenant picks one at setup | Every German bookkeeping product ships both; tax advisor expects it | MEDIUM | Seed data per chart; store active-account subset per tenant; immutable choice |
| Double-entry booking engine (Soll/Haben, immutable journal) | Core of any Buchhaltung; GoBD requires unchangeable postings | HIGH | Reuse v1 GoBD immutability + audit-log patterns; corrections via Storno-buchung, never edit |
| Automatic account mapping from v1 invoice events | Users don't want to book their own sales manually | HIGH | Tax-key → account table per chart; depends on v1 EN-16931 VAT categories |
| Automatic booking from payments (clears open items) | Payment→ledger must be one flow | MEDIUM | Depends on v1 open items + payments |
| Expense/supplier-invoice posting into ledger | Feeds EÜR/GuV; needed for input-tax deduction | HIGH | Fed by Belege domain + v1 inbound e-invoice |
| EÜR report (Anlage EÜR structure) | Default for Numera's core audience (Freiberufler/KU) | MEDIUM | Cash-basis; map accounts → EÜR line codes |
| USt-Voranmeldung: Kennziffer calc + review + XML/print export | VAT return is monthly/quarterly legal duty | HIGH | Kz 81/86/35/66/83/41/89/46/47/61; ERiC XML format; manual ELSTER upload per v1 decision |
| Elektronisches Kassenbuch (GoBD-conform, no negative balance) | Cash-handling businesses need it; part of "complete" bookkeeping | MEDIUM | Cash book only — NOT a POS; no TSE needed if scoped correctly |
| Journal / account ledger views (Buchungsjournal, Kontoauszug per account) | Auditability; tax advisor & Betriebsprüfung expect it | MEDIUM | Read models over the journal |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| BWA (DATEV-style monthly management report + prior-period compare) | Owner-facing insight competitors gate to higher tiers | MEDIUM | Pure read model over ledger; high perceived value, low marginal cost once engine exists |
| GuV + simple Bilanz for GmbH/UG (accrual) | Opens the Kapitalgesellschaft market (PROJECT target group) | HIGH | Accrual logic, period accruals; big scope — strong candidate to phase later |
| Guided VAT-return checklist ("what changed vs last month", plausibility warnings) | Reduces the #1 anxiety (filing a wrong USt-VA) | MEDIUM | Diff + heuristics over Kennziffern |
| Dauerfristverlängerung / Sondervorauszahlung handling | Real recurring pain for filers | LOW | Mostly config + reminder logic |
| One-click "prepare for tax advisor" period pack (journal + reports) | Bridges to existing TaxAdvisor role, pre-DATEV-export | LOW–MEDIUM | Leverages v1 TaxAdvisor role |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Full TSE-certified POS / Kassensystem | "We handle cash, isn't that the same?" | KassenSichV = certified TSE hardware/cloud, DSFinV-K export, Belegausgabepflicht — a separate regulated product | Ship a GoBD cash *book* only; integrate third-party TSE POS later if demanded |
| Direct ERiC transmission of USt-VA in v2 | "Just send it to the Finanzamt" | Requires Herstellerregistrierung + ERiC C-library integration + liability; explicitly deferred in PROJECT | Calc + XML/print export, user uploads via ELSTER portal (matches v1 decision) |
| Full HGB Jahresabschluss + eBilanz + Anhang/Lagebericht | "GmbHs need a Bilanz" | eBilanz taxonomy is huge and heavily regulated; scope explosion | Start with GuV + basic Bilanz read model; eBilanz a separate future milestone |
| Free-form manual journal entry as the primary UX | Accountants want raw booking power | Encourages mis-postings, breaks the automation story, GoBD risk | Automate standard flows; expose manual booking only as a gated "advanced" escape hatch |
| Anlagenverwaltung / AfA (depreciation) in this milestone | GmbHs depreciate assets | Own sub-domain (asset register, depreciation methods, GWG); dilutes focus | Defer to a dedicated milestone (already backlog in PROJECT) |

---

## Domain 2 — Banking (Multibanking via finAPI, Reconciliation, SEPA)

Banking is where the accounting engine meets reality. Its headline value is **automatischer Zahlungsabgleich** — matching real bank movements to v1 open items and auto-marking invoices paid.

### How it actually works (domain workflow)

- **Account connection (AIS):** tenant connects bank accounts through finAPI (BaFin-licensed, Berlin-Group XS2A/PSD2). finAPI handles the bank-specific PSD2 consent + SCA (strong customer authentication) flows. PSD2 consent expires (typically ~90 days) and must be periodically re-authenticated — a recurring UX obligation, not a one-time setup.
- **Transaction sync:** periodic pull of Kontoumsätze (booking date, amount, counterparty name + IBAN, Verwendungszweck/remittance text, end-to-end ID). Stored per-tenant under RLS.
- **Automatic reconciliation (the core):** for each incoming bank transaction the engine proposes a match to open items:
  1. **Amount match** against open receivables/payables (exact first, then tolerant for partial/rounding).
  2. **Reference match** — invoice number / customer number found in the Verwendungszweck (strongest signal); end-to-end ID if present.
  3. **Counterparty match** — IBAN or name against v1 customer/supplier master data.
  4. Confidence score → auto-clear high-confidence, propose for review otherwise. Confirmation posts payment (clears open item) AND books Bank↔Forderung/Verbindlichkeit in the ledger.
  - **Learned rules:** remember the user's categorization (e.g. recurring rent/subscription → fixed account) and auto-propose next time — the behavior Lexware/sevDesk lean on heavily.
  - Handle 1:N and N:1 (one transfer paying several invoices, or a batch) and residual/partial payments (feeds v1 dunning).
- **SEPA transfers (PIS):** initiate SEPA Credit Transfer to pay supplier invoices from open payables; requires SCA per payment. Optional SEPA Direct Debit collection is a heavier, separate capability.

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Connect bank account(s) via finAPI (AIS) + manage PSD2 consent/SCA | Multibanking is the headline; PROJECT pre-selected finAPI | HIGH | Consent expiry re-auth UX; secure token storage; per-tenant isolation |
| Transaction sync (Kontoumsätze) with dedup | Nothing works without the feed | MEDIUM | Idempotent import keyed on bank tx id |
| Automatic reconciliation to open items (amount + reference + counterparty) | The #1 reason users buy multibanking | HIGH | Depends on v1 open items + CRM master data; scoring + review queue |
| Auto-mark invoice paid + post payment on confirmed match | Closes the loop to ledger + dunning | MEDIUM | Reuses v1 payment recording |
| Manual assignment / correction of a transaction | Matcher is never 100%; users need override | MEDIUM | Split one tx across multiple invoices/accounts |
| CSV/MT940/CAMT import fallback | Some banks/tenants can't/won't use PSD2 | MEDIUM | Cheap insurance against finAPI coverage gaps |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Learned categorization rules (remembered booking proposals) | Cuts manual booking ~70% (Lexware's own claim) | MEDIUM | Rule store keyed on counterparty/reference patterns |
| SEPA outgoing transfer to pay supplier invoices from open payables | Pay-from-inbox loop competitors charge premium for | HIGH | PIS + SCA per payment; strong when combined with Belege/inbound e-invoice |
| Auto-reconcile across foreign-currency accounts | Numera already does FX invoices (v1) | MEDIUM | Leverage v1 FX arithmetic |
| Cash-flow / liquidity view over real balances | Owner-facing insight | MEDIUM | Read model over synced balances + open items |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Storing bank credentials / screen-scraping ourselves | "Cheaper than a licensed provider" | Illegal under PSD2, liability, breakage; finAPI exists precisely for this | Use finAPI's licensed XS2A exclusively |
| SEPA Direct Debit collection (Lastschrift einziehen) in this milestone | "Also collect from customers" | Mandate management (SEPA mandates), pre-notification, R-transactions — a sub-domain | Start with outgoing Credit Transfer; add SDD later if demanded |
| Fully unattended auto-booking with no review queue | "Just book everything" | One wrong high-confidence match pollutes an immutable ledger | Auto-clear only above a high confidence threshold; everything else reviewed |
| Building our own PSD2 aggregation for all EU banks | "Broader coverage" | finAPI already aggregates; reinventing is years of work | Rely on finAPI coverage; CSV/CAMT import as gap-filler |

---

## Domain 3 — Belege & Ausgaben (Receipt Capture, OCR, GoBD Archive, Expense Posting)

Feeds Domain 1: every captured receipt becomes an expense booking (input-tax deduction) and a GoBD-archived document. This is the "snap a photo, it books itself" story.

### How it actually works (OCR → booking-proposal flow)

1. **Capture:** camera (PWA — already a v1 capability) or file upload; PDF or image. Plus **per-tenant email intake** — a unique inbox address (e.g. belege-&lt;tenant&gt;@…) so suppliers/users forward invoices; attachments auto-ingested.
2. **OCR / extraction:** pull structured fields — supplier name, date, gross/net amounts, VAT rate(s) + VAT amount, invoice number, currency. For **e-invoices (XRechnung/ZUGFeRD)** already received in v1, skip OCR and read the structured XML directly (far higher accuracy — reuse v1 parser).
3. **Match & enrich:** link to an existing supplier (v1 CRM) or propose creating one; detect duplicates; optionally link to an open payable.
4. **Booking proposal:** derive expense account + tax key (Vorsteuer) → propose Soll Aufwand + Vorsteuer / Haben Kreditor. Apply learned rules (same supplier → same account as last time).
5. **Confirm → post + archive:** user confirms; posting hits the ledger (Domain 1); the original document is written to the **GoBD long-term archive** (revisionssicher: immutable/WORM, original format preserved, indexed/searchable, 10-year retention, linked to the booking, covered by Verfahrensdokumentation).

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Receipt capture: camera + upload (PDF/image) | Mobile receipt scan is a core buying reason (PROJECT core value) | MEDIUM | PWA camera exists in v1; add ingestion pipeline |
| OCR field extraction (supplier, date, amounts, VAT, invoice no.) | "Type it yourself" is a dealbreaker vs competitors | HIGH | OCR/IDP service or model; German receipts; validation UX |
| Structured read of v1-received e-invoices (skip OCR) | Higher accuracy path already available | LOW–MEDIUM | Reuse v1 XRechnung/ZUGFeRD parser |
| GoBD-conform revisionssicheres Langzeitarchiv (WORM, 10y, indexed) | Legal requirement for receipts; a headline claim | HIGH | Immutable storage, retention, search, links to booking + Verfahrensdokumentation |
| Booking proposal (expense account + Vorsteuer tax key) | The "it books itself" promise | HIGH | Depends on Domain 1 engine + tax keys |
| Supplier match + duplicate detection | Avoids double-booking; ties to v1 CRM | MEDIUM | Reuse v1 supplier master |
| Per-tenant email intake (unique inbox) | Standard in sevDesk/Lexware; frictionless capture | MEDIUM | Inbound mail routing + attachment extraction + per-tenant isolation |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Learned per-supplier booking rules | Near-zero-touch repeat expenses | MEDIUM | Shares rule store with Banking |
| Auto-link receipt ↔ bank transaction ↔ open payable (three-way match) | Full audit chain, one confirmation | HIGH | Requires Banking + Belege + ledger together — strong moat |
| Line-item extraction (not just header totals) | Cost-center / detailed categorization | HIGH | Diminishing returns for small businesses; keep optional |
| Mobile "snap on the go, queue offline" (uses v1 offline shell) | Field/receipt-heavy users | MEDIUM | Leverages v1 PWA offline capability |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Editing/deleting archived originals | "Fix the scan" | Breaks GoBD Unveränderbarkeit — the whole point of the archive | Archive is WORM; corrections are new versions/annotations, original retained |
| Guaranteeing 100% OCR accuracy / auto-posting without review | "Zero manual work" | OCR errors post wrong VAT → wrong USt-VA → real tax exposure | Always require confirmation; show confidence + source snippet |
| Training a bespoke OCR model in-house | "Better than off-the-shelf" | Huge cost; mature IDP services exist | Use a proven OCR/IDP provider; layer domain rules on top |
| Accepting any file type into the archive as a "document" | "Store everything" | Archive scope creep, retention/DSGVO ambiguity | Constrain to accounting-relevant documents with defined retention |

---

## Domain 4 — Monetarisierung (Stripe Subscription Billing)

Independent of the accounting domains. It turns v1's already-server-authoritative feature-gates into a real paid subscription. Numera **is a Stripe merchant here billing its own tenants** — distinct from tenants invoicing their customers.

### How it actually works (Stripe → tenant plan gate)

- **Model:** one Stripe Product per plan tier (S/M/L/XL) with recurring Prices (monthly/annual). A tenant has **one subscription**; plan changes swap the price/line-item (Stripe's recommended pattern for clean upgrade/downgrade UI).
- **Source of truth for the gate:** Stripe holds billing state, but the **tenant's entitlement lives in Numera's DB**, updated from **Stripe webhooks** (checkout completed, subscription created/updated/deleted, invoice paid/payment_failed). The existing v1 tarif-gate reads this entitlement — so Stripe *drives* the existing gate rather than replacing it. Webhook handling must verify signatures and be **idempotent** (webhooks retry).
- **Self-service via Stripe Customer Portal:** upgrade/downgrade (with proration), change payment method, view invoices, cancel — configured in the portal (allowed plan IDs) rather than custom-built UI.
- **Trial:** `trial_period_days`; billing_cycle_anchor resets at trial end; the gate grants full access during trial.
- **Dunning for Numera's own subscription:** failed payment → Stripe Smart Retries + dunning emails; on final failure, downgrade/limit the tenant's plan (in-app degradation UX already patterned in v1).
- **Downgrade edge:** downgrading below current usage (e.g. more users than the new tier allows) needs a policy — block, or degrade gracefully at period end.

### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Stripe Checkout to start a paid plan | Table stakes to charge money at all | MEDIUM | Stripe-hosted checkout; avoid handling card data |
| Webhook-driven tenant entitlement (source of truth in Numera DB) | Gate must reflect real billing state reliably | HIGH | Signature verify + idempotent handlers; wires to existing v1 gate |
| Self-service upgrade/downgrade via Customer Portal (proration) | Users expect to change plans without support | MEDIUM | Configure portal; single-subscription line-item swap |
| Trial period with full access then convert | Standard SaaS acquisition | LOW–MEDIUM | `trial_period_days`; gate honors trial state |
| Own-subscription dunning + graceful degradation on failed payment | Revenue recovery + defined failure behavior | MEDIUM | Smart Retries + v1 degradation UX |
| Manage payment method + view own billing invoices | Basic account hygiene | LOW | Customer Portal covers it |
| VAT-correct billing of Numera's own subscription (incl. reverse charge for EU B2B) | Numera invoices businesses; must be VAT-correct | MEDIUM | Stripe Tax or own logic; Numera's own §14/e-invoice could even reuse v1 |

### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Annual vs monthly pricing with in-app upgrade nudges | Higher LTV, lower churn | LOW | Tie nudges to existing gate hit-points |
| Usage-aware downgrade guardrails (warn before losing data/features) | Prevents angry churn + support load | MEDIUM | Policy at period-end; leverages v1 gate limits |
| In-app plan comparison at the exact gate hit-point | Converts at moment of intent | LOW | Enhances existing v1 "upgrade hint instead of error" UX |
| Proration transparency ("you'll be charged X now") | Trust at upgrade moment | LOW | Stripe preview invoice |

### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Building custom billing/subscription UI instead of Customer Portal | "Full control / on-brand" | Reinvents proration, tax, SCA, dunning — high risk, low value | Use Stripe Customer Portal; deep-link from app |
| Storing card data / prices locally as truth | "Show plan in our UI fast" | PCI scope; local price drift vs Stripe | Stripe-hosted; entitlement (not price) is local truth |
| Metered/usage-based billing now | "Charge per invoice/tx" | Complex metering + reconciliation; S/M/L/XL is seat/feature-tiered | Stay tier-based; revisit metering only with clear demand |
| Trusting client-side gate state | "Simpler frontend" | Bypassable → revenue leak | Server-authoritative gate (already v1) fed by webhooks |
| Coupling accounting rollout to billing rollout | "Ship together" | Monetization is independent; coupling blocks either | Build Stripe in parallel; it only touches the existing gate |

---

## Feature Dependencies

```
v1 open items + payments ──required by──> Banking auto-reconciliation
v1 CRM (customer/supplier master) ──required by──> reconciliation match + expense supplier match
v1 EN-16931 VAT categories ──required by──> booking account/tax-key mapping + USt-VA Kennziffern
v1 inbound e-invoice parser ──enhances──> Belege OCR (structured path, skips OCR)
v1 PWA camera + offline shell ──enables──> receipt capture
v1 GoBD immutability + audit-log ──pattern for──> ledger journal + GoBD archive
v1 server-authoritative tarif-gate ──driven by──> Stripe webhooks/entitlement

Buchhaltung: chart of accounts + booking engine (FOUNDATION)
    ├──required by──> EÜR report
    ├──required by──> GuV / Bilanz
    ├──required by──> BWA
    ├──required by──> USt-Voranmeldung (Kennziffern)
    └──required by──> expense posting + payment posting

Belege & Ausgaben (OCR → proposal)
    └──feeds──> expense booking ──feeds──> EÜR / GuV / USt-VA (Vorsteuer Kz 66)

Banking (sync + reconciliation)
    ├──requires──> v1 open items
    ├──posts through──> Buchhaltung booking engine (Bank ↔ Forderung/Verbindlichkeit)
    └──SEPA transfer ──pays──> open payables (from Belege/inbound e-invoice)

Belege + Banking + Ledger ──together enable──> three-way match (receipt↔tx↔payable) [differentiator]

Monetarisierung (Stripe) ── INDEPENDENT of accounting domains; touches only the existing gate
```

### Dependency Notes

- **Buchhaltung is the keystone.** EÜR, GuV, BWA and USt-VA are all read models/derivations over the booking engine. Build the chart of accounts + booking engine + tax-key mapping first; reports and USt-VA follow cheaply once postings exist.
- **USt-VA depends on both the booking engine (v2) AND v1's EN-16931 VAT categories.** The Kennziffern (81/86/66/83/…) are aggregations of correctly-classified turnover and input tax. Wrong VAT category upstream → wrong return.
- **Reconciliation depends on v1 open items + CRM.** Matching is meaningless without open receivables/payables to match against, and weak without supplier/customer master data for counterparty matching.
- **Expense posting bridges Belege → Buchhaltung.** Belege produces the booking proposal; the ledger consumes it; EÜR/GuV/USt-VA (Vorsteuer) then reflect it. So Belege needs at least a minimal booking engine to be useful end-to-end.
- **v1 inbound e-invoices are the accuracy shortcut for Belege.** Structured XML → no OCR error → cleaner Vorsteuer. Prioritize that path.
- **Banking posts through the ledger.** A confirmed reconciliation isn't just "mark paid" (v1) — in v2 it books Bank↔Forderung/Verbindlichkeit. So Banking's booking side depends on Domain 1.
- **Monetarisierung is decoupled.** It only writes tenant entitlement that the existing gate already reads. It can ship before, after, or in parallel with the accounting work — do not couple its rollout to them.

---

## MVP Definition

### Launch With (v2.0 core)

- [ ] **SKR03/04 chart of accounts + double-entry booking engine + tax-key mapping** — foundation; nothing else works without it
- [ ] **Auto-booking from v1 invoice + payment events** — makes the engine real without manual entry
- [ ] **EÜR report + USt-Voranmeldung (calc + XML/print export)** — the tax duties Numera's core audience must fulfill
- [ ] **Belege capture (camera/upload) + OCR + booking proposal + GoBD archive** — the "snap it, book it" headline + legal archive
- [ ] **Per-tenant email intake for receipts** — frictionless capture, competitor parity
- [ ] **Banking: finAPI account connect + transaction sync + automatic reconciliation to open items** — the multibanking headline
- [ ] **Stripe Checkout + webhook entitlement + Customer Portal self-service + trial** — turns on real revenue; independent track

### Add After Validation (v2.1)

- [ ] **BWA** — high-value read model, cheap once ledger exists; add right after core reports
- [ ] **SEPA outgoing transfers (pay supplier invoices)** — trigger: users ask to pay from the app
- [ ] **Learned booking rules (shared Banking + Belege)** — trigger: enough repeat data to auto-propose
- [ ] **Three-way match (receipt ↔ tx ↔ payable)** — trigger: Banking + Belege both stable
- [ ] **Elektronisches Kassenbuch** — trigger: cash-handling tenant demand
- [ ] **Usage-aware downgrade guardrails + annual pricing** — trigger: churn/plan-change data

### Future Consideration (v3+)

- [ ] **GuV + Bilanz + eventual eBilanz for GmbH/UG** — defer: large regulated scope; own milestone
- [ ] **Direct ERiC transmission of USt-VA** — defer: Herstellerregistrierung + C-library + liability (per PROJECT decision)
- [ ] **Anlagenverwaltung / AfA** — defer: separate sub-domain
- [ ] **SEPA Direct Debit collection** — defer: mandate management sub-domain
- [ ] **TSE-certified POS** — defer/never: separate regulated product, out of scope

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Chart of accounts + booking engine | HIGH | HIGH | P1 |
| Auto-booking from invoices/payments | HIGH | HIGH | P1 |
| USt-Voranmeldung (calc + export) | HIGH | HIGH | P1 |
| EÜR report | HIGH | MEDIUM | P1 |
| Belege capture + OCR + booking proposal | HIGH | HIGH | P1 |
| GoBD long-term archive | HIGH | HIGH | P1 |
| Banking connect + sync + reconciliation | HIGH | HIGH | P1 |
| Stripe checkout + entitlement + portal + trial | HIGH | MEDIUM | P1 |
| Per-tenant email intake | MEDIUM | MEDIUM | P2 |
| BWA | MEDIUM | MEDIUM | P2 |
| SEPA outgoing transfers | MEDIUM | HIGH | P2 |
| Learned booking rules | MEDIUM | MEDIUM | P2 |
| Elektronisches Kassenbuch | MEDIUM | MEDIUM | P2 |
| Three-way match | MEDIUM | HIGH | P2 |
| GuV + Bilanz (GmbH/UG) | MEDIUM | HIGH | P3 |
| Direct ERiC transmission | MEDIUM | HIGH | P3 |
| Anlagenverwaltung / AfA | LOW–MEDIUM | HIGH | P3 |

**Priority key:** P1 = must have for v2.0 · P2 = add when possible (v2.1) · P3 = future consideration.

**Build-order hint for roadmapper:** Buchhaltung engine → EÜR/USt-VA → Belege (feeds engine) → Banking (reconciles into engine) → BWA/three-way match. Monetarisierung runs as an **independent parallel track** (only touches the existing gate).

---

## Competitor Feature Analysis

| Feature | Lexware Office | sevDesk | DATEV / Buchhaltungsbutler | Our Approach (Numera v2.0) |
|---------|----------------|---------|----------------------------|----------------------------|
| Chart of accounts | SKR03/04 behind-the-scenes | SKR03/04 | SKR full (DATEV native) | SKR03/04, tenant-selected, automation-first |
| Auto payment reconciliation | Integrated multibanking, real-time, ~12M accepted suggestions/mo | Integrated online banking match | Strong (Buchhaltungsbutler = reconciliation-first) | finAPI sync + scored match to v1 open items + learned rules |
| OCR receipt capture | Automated categorization + suggestions | Belegerfassung + OCR + email intake | Buchhaltungsbutler OCR-first | Capture + OCR + proposal; structured e-invoice path reuses v1 parser |
| GoBD archive | Yes | Yes | Yes (DATEV DMS) | Revisionssicher WORM + Verfahrensdokumentation |
| EÜR / USt-VA | Yes, ELSTER submission | Yes, ELSTER submission | Yes (advisor workflow) | Calc + XML/print export (direct ERiC deferred) |
| BWA | Higher tiers | Yes | DATEV standard BWA | Included as differentiator (cheap over ledger) |
| GuV / Bilanz | Limited | Limited | Full (advisor domain) | Deferred to v3 (GmbH/UG market) |
| SEPA transfers | Yes | Yes | Via banking | Outgoing transfer as P2 |
| Subscription billing | Vendor's own (tiers) | Vendor's own (tiers) | n/a | Stripe: portal self-service, trial, webhook entitlement → existing gate |

---

## Sources

- finAPI — Banking/XS2A/Payment API, Berlin-Group PSD2, SEPA transfer (BaFin licensed): https://www.finapi.io/en/products/open-banking/banking-api/ , https://www.finapi.io/en/products/payments/transfer-api/ , https://documentation.finapi.io/xs2a/01-getting-started (MEDIUM — provider docs)
- Lexware Office — automatic payment reconciliation, booking suggestions, multibanking, multi-Beleg assignment: https://www.lexoffice.de/funktionen/zahlungen-abgleichen/ , https://www.lexware.de/funktionen/automatisierte-buchhaltung/ , https://help.lexware.de/de-form/collections/1335660-verarbeiten-ihrer-bankumsatze (HIGH — competitor behavior)
- sevDesk — reconciliation, OCR receipt capture, DATEV/advisor, USt-VA Kennzahlen: https://sevdesk.de/ratgeber/buchhaltung-finanzen/rechnung-buchhaltung-programme/rechnungsprogramm-vergleich/ , https://hilfe.sevdesk.de/de/articles/9418927-kennzahlen-der-umsatzsteuervoranmeldung-v1 (HIGH — competitor + Kennzahlen)
- USt-Voranmeldung Kennziffern (81/86/35/66/83) + 2026 Vordruckmuster: ELSTER Hilfe https://www.elster.de/eportal/helpGlobal?themaGlobal=help_ustva_2019 , BMF Vordruckmuster 2026 https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Umsatzsteuer/2025-12-29-vordruckmuster-USt-voranmeldung-2026.pdf (HIGH — official)
- KassenSichV / TSE vs GoBD, Kassenbuch vs Kassensystem distinction: https://www.payone.com/DE-de/ueber-uns/insights/kassensicherungsverordnung , https://www.ihk-muenchen.de/ratgeber/steuern/kassenbuchfuehrung/ (HIGH — regulatory)
- Stripe — subscriptions, trials, change price/proration, Customer Portal, webhook idempotency/entitlements: https://docs.stripe.com/billing/subscriptions/trials , https://docs.stripe.com/billing/subscriptions/change-price , https://operatoriq.io/blog/stripe-customer-portal-plan-changes/ (HIGH official / MEDIUM guides)
- SKR03 vs SKR04 (Prozess- vs Abschlussgliederungsprinzip) + standard booking accounts — DATEV chart conventions (HIGH — established German accounting practice, training data)

---
*Feature research for: German accounting/invoicing SaaS — v2.0 (Buchhaltung, Banking, Belege & Ausgaben, Monetarisierung)*
*Researched: 2026-08-02*
