# Pitfalls Research

**Domain:** Multi-tenant German accounting & e-invoicing SaaS (Lexware Office / sevDesk competitor)
**Researched:** 2026-07-09
**Confidence:** HIGH for legal/e-invoicing facts (EU Commission, BMF, EBA sources); MEDIUM for product/market wisdom (community + competitor observation)

> Scope note: This project is legally regulated software. Unlike a generic SaaS, several pitfalls here are not "bugs" — they are **liability**. A wrong VAT calculation, a mutable finalized invoice, or a tenant data leak in accounting software has tax-law and DSGVO consequences for *your customers*, which flows back to you. Treat the "Critical Pitfalls" below as gating requirements, not backlog items.

---

## Critical Pitfalls

### Pitfall 1: Treating a PDF (even a nice one) as an "e-invoice"

**What goes wrong:**
Teams build a beautiful PDF renderer, call it "e-invoicing," and ship. But since 1 Jan 2025 a German B2B e-invoice (*E-Rechnung*) is legally a **structured, machine-readable format** (XRechnung XML, or ZUGFeRD/Factur-X where the XML is the binding part). A plain PDF — even a PDF/A — is a *sonstige Rechnung*, not an e-invoice. The structured XML is the legally binding artifact; the PDF is only a human-readable convenience.

**Why it happens:**
Invoicing intuition says "invoice = the document the human reads." EN 16931 inverts this: the XML is primary, the visual is secondary. Teams also conflate "we email a PDF" with "we do e-invoicing."

**How to avoid:**
- Architect the invoice as **data first** (an EN 16931 semantic model), render both XML and PDF from that single source of truth. Never hand-build the XML from a rendered PDF.
- For ZUGFeRD/Factur-X: the embedded XML and the PDF's visible figures **must agree exactly** — a mismatch (e.g., PDF shows 100.00, XML says 99.99 due to rounding) is a documented audit red flag and a common validator finding.
- Validate every generated invoice against the **KoSIT validator** (the official German EN 16931 / XRechnung validator) in CI and at runtime before the invoice can be finalized.

**Warning signs:**
"We generate the PDF and then extract the XML from it." Two code paths that compute totals independently. No KoSIT validation step.

**Phase to address:** E-invoicing core phase (v1). This is foundational — the data model must be EN 16931-shaped from day one.

---

### Pitfall 2: Money as floating-point / inconsistent rounding (per-line vs per-document)

**What goes wrong:**
Using `float`/`double` for money, or rounding inconsistently, causes cent-level drift that EN 16931 validation rejects outright. The killer rules:
- **BR-CO-10:** sum of line net amounts must equal the document line-total.
- **BR-CO-15:** grand total = total net + total tax.
- **BR-S-08 / per-category:** each VAT-category tax amount (BT-117) must equal taxable base (BT-116) × rate (BT-119)/100, **rounded to 2 decimals** — and the document total VAT must equal the *sum of the rounded per-category amounts*, not a re-rounded grand total.

If you round per line but total per document (or vice versa), or truncate instead of round-half-up, these rules fire and the invoice is rejected by the recipient / KoSIT.

**Why it happens:**
Float is the language default. Rounding order feels like an implementation detail. Developers "round at the end" intuitively, but EN 16931 mandates rounding each subtotal *first*, then summing.

**How to avoid:**
- Use **integer minor units (cents)** or a `Decimal`/`BigDecimal` type end-to-end. Never `float` for money. (Postgres `numeric`, not `float8`.)
- Codify one rounding policy: round each VAT category's tax to 2 decimals, then sum categories for the document VAT total. Round-half-up (kaufmännisch), matching the EN 16931 expectation.
- Write **property-based / golden-file tests** that feed known invoices through generation and assert against KoSIT-validated reference XML. Include nasty cases: 7% + 19% mixed, discounts (BT-107 allowances), reverse-charge €0 tax lines.

**Warning signs:**
`float` in any money column or DTO. Totals computed in the UI/JS layer. BR-CO-10 / BR-CO-15 / BR-S-* rejections. "It's off by one cent sometimes."

**Phase to address:** Foundational data-model phase, before invoicing. Retrofitting money types is a full-schema migration.

---

### Pitfall 3: Editing finalized invoices (GoBD immutability violation)

**What goes wrong:**
A finalized/sent invoice is edited in place — corrected typo, changed amount, re-issued under the same record. This violates GoBD *Unveränderbarkeit* (immutability): booked/issued documents must not be silently alterable. In accounting software this is not a UX preference; it makes the customer's books non-GoBD-conform and can invalidate them in an audit (*Betriebsprüfung*).

**Why it happens:**
CRUD instinct — an invoice is "just a row you can UPDATE." Draft and finalized states aren't modeled distinctly. No concept of a correction document (*Korrekturrechnung* / *Stornorechnung*).

**How to avoid:**
- Model an explicit lifecycle: **Draft (fully mutable) → Finalized (immutable)**. Finalization is a one-way gate that assigns the invoice number and freezes content.
- Corrections after finalization are **new documents** (cancellation invoice + new invoice, or a credit note), never in-place edits. Keep an immutable audit trail (append-only history) of state changes.
- Enforce immutability at the **database layer** (append-only, or DB triggers / row-versioning), not just in app code — GoBD cares about the actual system behavior, and app-layer guards get bypassed.

**Warning signs:**
An `UPDATE invoices SET amount=…` path reachable after finalization. No `status` distinction between draft and issued. Delete buttons on issued invoices. No cancellation-invoice concept.

**Phase to address:** Invoicing core (v1). The state machine must exist before the first invoice is issued.

---

### Pitfall 4: Multi-tenant data leakage (the existential SaaS bug)

**What goes wrong:**
Tenant A sees / modifies Tenant B's invoices, customers, or bank data. In accounting software this is simultaneously a DSGVO breach (Art. 33 reportable), a trade-secret leak, and a trust-ending event. A single missing `WHERE tenant_id = ?` does it.

**Why it happens:**
Tenant scoping is enforced ad hoc in application code, so one forgotten filter — in a new endpoint, a report query, a background job, an admin tool, a cache key — leaks. ORMs and raw SQL bypass app-layer guards.

**How to avoid:**
- Enforce isolation at the **database layer with Postgres Row-Level Security (RLS)**, keyed off a session variable (`app.current_tenant`) set per request. App-code filters are defense-in-depth, not the primary control.
- Ensure **every** table with tenant data has a `tenant_id` and an RLS policy; add a test/lint that fails CI if a tenant table lacks a policy.
- Watch the leaks RLS *doesn't* automatically cover: cache keys, file/blob storage paths, background jobs, full-text search indexes, exported files, and connection pooling that reuses a session with a stale `SET`. Reset the tenant context explicitly per checkout.
- Add automated **cross-tenant tests**: authenticate as Tenant A, attempt to read Tenant B's IDs, assert 404/403 for every resource type.

**Warning signs:**
Tenant filtering only in service/ORM code. IDs that are guessable sequential integers exposed in URLs. "We'll add RLS later." Shared caches keyed without tenant. No cross-tenant test suite.

**Phase to address:** Foundational tenancy/auth phase, phase 1. RLS retrofitted onto an existing schema is high-risk and easy to get subtly wrong.

---

### Pitfall 5: Per-tenant invoice numbering race conditions

**What goes wrong:**
Two invoices get the same number, or a global sequence leaks tenant B's volume to tenant A, or gaps appear that the customer can't explain. Concurrent finalizations under a naive "SELECT MAX(number)+1" produce duplicates under load.

**Why it happens:**
Numbering feels trivial. A global DB sequence is per-database, not per-tenant. `MAX()+1` has a classic read-modify-write race. Teams also over-correct into a **numbering myth** (see below).

**How to avoid:**
- Numbering must be **per-tenant** (each tenant has independent series). A single global sequence both collides logically and leaks business volume.
- Generate the number **atomically at finalization** inside the same transaction, using a per-tenant counter row locked with `SELECT … FOR UPDATE` or an `INSERT … ON CONFLICT` upsert — not `MAX()+1`.
- **Kill the "gapless" myth:** German law (and case law) requires invoice numbers to be **unique and traceable (*einmalig, nachvollziehbar*)** — it does **not** require a gapless (*lückenlose*) sequence. `fortlaufend` ≠ no gaps. Gaps from cancelled drafts are fine *if the numbering system is documented* (in the *Verfahrensdokumentation*). Do not build brittle gapless-guarantee logic that forces you to reuse numbers (which *is* forbidden) or block on failures.

**Warning signs:**
`ORDER BY id DESC LIMIT 1` to pick the next number. One sequence for all tenants. Duplicate-number bug reports under concurrency. Effort spent guaranteeing zero gaps.

**Phase to address:** Invoicing core (v1), same phase as the finalization state machine.

---

### Pitfall 6: VAT edge cases that produce legally wrong invoices

**What goes wrong:**
The happy path (19% domestic) works; the tax-special-cases are wrong. Each of these has a *mandatory legal text/marking* on the invoice and specific EN 16931 encoding:
- **§13b reverse charge** (*Steuerschuldnerschaft des Leistungsempfängers*): 0% shown, VAT category code `AE`, mandatory note. Getting this wrong shifts tax liability incorrectly.
- **Innergemeinschaftliche Lieferung** (intra-EU supply): exempt, category `K`, requires valid VAT-ID of recipient + note.
- **Kleinunternehmer §19**: no VAT shown, category `E`, mandatory note ("*Kein Ausweis von Umsatzsteuer wegen Anwendung der Kleinunternehmerregelung nach §19 UStG*"). Showing VAT here is illegal.
- **7% vs 19%** mixed carts; reduced-rate eligibility.

**Why it happens:**
Teams model VAT as a single rate field, not as EN 16931 **VAT category codes** (S/AE/K/E/Z/G/O) each with their own rules and required notes. The special cases only surface with real customers.

**How to avoid:**
- Model VAT as **(category code + rate + exemption reason)**, not a bare percentage. Map each business scenario to its EN 16931 category and required BT-fields (BT-120 exemption reason, etc.).
- Kleinunternehmer is a first-class tenant configuration, not an afterthought — it changes issuing obligations, invoice text, and whether VAT appears at all.
- Have a tax advisor / *Steuerberater* review the mapping table. This is cheap insurance against a class of legal defects.

**Warning signs:**
A single `vat_rate` column with no category. No reverse-charge or intra-EU handling. Kleinunternehmer invoices that show a VAT line.

**Phase to address:** Invoicing core (v1) for the common cases; §13b / intra-EU can be a fast-follow but must be scoped explicitly, not "assumed handled."

---

### Pitfall 7: Claiming "GoBD-zertifiziert" (there is no such certification)

**What goes wrong:**
Marketing writes "GoBD-zertifiziert" / "GoBD-certified." **No such certification exists.** GoBD is a set of principles (*Grundsätze*) enforced through proper process and documentation, not by any issuing body. The claim is misleading advertising (*wettbewerbswidrig*, abmahnfähig under UWG) and erodes credibility with informed buyers and accountants.

**Why it happens:**
Competitors use fuzzy language; teams copy it. "Certified" sounds reassuring.

**How to avoid:**
- Use accurate claims: "**GoBD-konform**" / "supports GoBD-compliant bookkeeping" / "we provide a *Verfahrensdokumentation* template." Never "zertifiziert."
- What actually helps customers: ship a **Verfahrensdokumentation** describing how your system stores/immutabilizes/archives data, since GoBD compliance is the *customer's* obligation and your software is the tool. This is a genuine differentiator done honestly.

**Warning signs:**
The word "zertifiziert"/"certified" next to GoBD anywhere in copy. No Verfahrensdokumentation artifact for customers.

**Phase to address:** Any phase touching marketing/legal copy; the Verfahrensdokumentation artifact belongs with the GoBD-archive phase.

---

### Pitfall 8: DSGVO / hosting / AVV foundations bolted on late

**What goes wrong:**
Processing German businesses' financial + personal data without: an **AVV (Auftragsverarbeitungsvertrag)** offered to every customer (you are their processor — Art. 28 GDPR), an EU/Germany hosting posture, a data-export/portability path, and deletion/retention logic that respects the **10-year GoBD retention** (which *overrides* GDPR erasure for tax-relevant records — you cannot simply delete an ex-customer's invoices).

**Why it happens:**
Compliance is treated as a launch-blocker checklist item, discovered late, then it forces schema/hosting changes.

**How to avoid:**
- Decide **hosting in the EU (ideally Germany)** up front; make it a stated selling point.
- Have an **AVV ready** as a self-serve document; you are contractually a processor for every tenant.
- Design retention as **10-year immutable archive** for tax-relevant data (invoices, bookings), with GDPR-erasure applying only to non-tax personal data. Model "deletion" as "anonymize what you may, retain what the tax law requires."
- Build **data export** (customer's right to their data + reduces lock-in fear) early.

**Warning signs:**
US-region default hosting. No AVV. A hard-delete that would wipe tax records. Retention conflated with "delete after account closes."

**Phase to address:** Foundational phase (hosting, tenancy) + a dedicated compliance pass before public launch; retention design lands with the GoBD-archive phase.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| `float` for money | Fast to write | EN 16931 rejections, cent drift, full-schema migration to fix | **Never** |
| App-code tenant filtering only (no RLS) | Ship faster | One forgotten `WHERE` = DSGVO breach; painful RLS retrofit | Never for a real multi-tenant launch; a solo-prototype spike only |
| Editable finalized invoices | Simpler CRUD | GoBD non-conformity for every customer; rework of the whole write path | Never once real invoices are issued |
| Build XML from rendered PDF | Reuse existing PDF | PDF/XML mismatch, audit red flags, brittle | Never |
| Skip KoSIT validation in pipeline | Fewer moving parts | Recipients reject invoices in production; you find out from angry customers | Never for issued invoices; OK to defer only for internal drafts |
| Gapless-numbering guarantee logic | "Feels compliant" | Complexity chasing a non-requirement; risks number reuse (which *is* illegal) | Never — uniqueness+traceability is the real rule |
| Single global invoice sequence | One counter | Cross-tenant volume leak + collisions | Never for multi-tenant |
| Defer AVV / EU hosting | Faster launch | Blocks B2B sales; forces migration; DSGVO exposure | Never for German B2B |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| KoSIT validator | Only validating in dev, or not at all | Validate every issued invoice in the pipeline; treat failure as a hard block on finalization |
| ZUGFeRD/Factur-X (PDF/A-3) | Non-conformant PDF/A-3 (embedded fonts missing, JavaScript present, wrong XMP metadata), or XML attached to a normal PDF | Generate true PDF/A-3 (ISO 19005-3): embedded fonts, ICC profile, no JS, correct XMP declaring the embedded XML; target ZUGFeRD 2.3 / Factur-X 1.0.07, EN 16931 (COMFORT) profile |
| ZUGFeRD profiles | Picking MINIMUM/BASIC-WL (no line items) and assuming it's a full B2B e-invoice | Use the **EN 16931 (COMFORT)** profile as the baseline for German B2B; MINIMUM/BASIC-WL are not fully EN 16931-compliant for all cases |
| Leitweg-ID (B2G) | Omitting it, or requiring it for B2B | Leitweg-ID (BT-10 buyer reference) is **mandatory for public-sector (B2G) XRechnung**; for B2B it's typically not required. Don't hard-require it for all invoices |
| Bank APIs (PSD2 / AIS) | Assuming a **90-day** re-auth window | The EU AIS re-authentication window is **180 days** since 25 Jul 2023 (EBA RTS amendment) — 90 days is UK/stale. Still design for periodic re-consent and graceful feed-break recovery |
| Bank multibanking aggregators (finAPI, FinTS, GoCardless/Yapily/Klarna Kosma) | Provider lock-in + surprise per-connection pricing | Abstract the aggregator behind your own interface; model per-connection cost; expect coverage gaps and per-bank quirks |
| DATEV export | Assuming "CSV export" = DATEV | DATEV expects specific formats (DATEV-Format / EXTF, SKR03/SKR04 account mapping); validate against DATEV's spec, wrong account mapping corrupts the *Steuerberater*'s import |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `MAX(number)+1` for invoice numbering | Duplicate numbers, deadlocks under load | Atomic per-tenant counter with row lock / upsert | Concurrent finalizations — even at low tenant scale |
| Synchronous KoSIT validation + PDF/A-3 render on the request thread | Slow finalize, timeouts | Do validation/render async or in a job with a status; keep the atomic number+immutability commit fast | Bulk invoicing / month-end spikes |
| Connection-pool session reuse with stale `SET app.current_tenant` | Intermittent cross-tenant reads (worst-case) | Reset tenant GUC on every pool checkout; test it | Under pooling + concurrency, low user count |
| Storing 10-year archive in hot primary DB | Ballooning DB, slow backups | Tiered/object storage for the immutable archive; keep working set lean | As invoice history accumulates over years |
| Per-request full re-computation of reports (USt-VA, sums) across all history | Slow dashboards | Incremental aggregates / materialized views scoped per tenant | Once a tenant has thousands of documents |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Tenant scoping in app code only | Cross-tenant data leak = DSGVO Art. 33 breach | Postgres RLS as primary control + cross-tenant tests |
| Sequential/guessable resource IDs in URLs | IDOR enumeration across tenants | RLS makes IDs safe even if guessed; still prefer opaque IDs |
| Bank credentials / PSD2 tokens stored plaintext | Catastrophic financial-data breach | Encrypt at rest (KMS); never store bank login creds — use the aggregator's token model |
| Storing full IBANs / financial PII without field-level care | Elevated breach impact | Encrypt sensitive columns; minimize; access-log |
| Immutability enforced only in app layer | Auditor/attacker bypass, GoBD failure | DB-level append-only / trigger enforcement |
| No audit trail on financial state changes | Can't prove GoBD *Nachvollziehbarkeit* | Append-only audit log of every finalize/cancel/void with actor + timestamp |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Exposing raw XRechnung/EN 16931 jargon (BT-codes, profiles) to end users | Overwhelm; feels enterprise-hostile | Hide the standard behind plain-language UI; "send as e-invoice" just works |
| No human-readable rendering of received structured invoices | Users can't *read* incoming XRechnung XML | Always render a visual view of structured invoices you receive |
| Blocking on validation errors with cryptic KoSIT messages | User stuck, can't self-serve | Translate BR-* errors into actionable German-language guidance |
| Forcing users to understand Kleinunternehmer/reverse-charge to invoice | Wrong invoices or abandonment | Ask about their tax status once (onboarding), then apply rules automatically |
| PWA that silently fails offline / loses a draft | Data loss, distrust with financial data | Be explicit about offline limits; don't over-promise offline for financial writes (see below) |

## PWA / Platform Traps

| Trap | Reality | Mitigation |
|------|---------|------------|
| Assuming iOS Safari PWA push/camera/storage parity | iOS PWA support historically lags: push only via installed (Add-to-Home-Screen) PWA and only on recent iOS; storage can be evicted; camera/file access constrained | Don't make core flows depend on iOS push or persistent offline storage; test on real iOS; treat mobile as responsive-web-first |
| Promising rich **offline** invoicing | Offline financial writes create sync/conflict + numbering-race problems; users expect correctness, not eventual consistency, for invoices | Keep invoice **finalization online-only** (numbering + immutability need a source of truth). Allow offline *drafting* at most |
| iOS storage eviction wiping "saved" data | Silent data loss | Never treat client storage as durable for financial data; server is source of truth |

## "Looks Done But Isn't" Checklist

- [ ] **E-invoice generation:** Often missing — passes *your* renderer but **fails the official KoSIT validator**. Verify: every issued invoice validated against KoSIT, all BR-CO / BR-S rules green.
- [ ] **ZUGFeRD hybrid:** Often missing — the PDF's visible total ≠ the embedded XML total. Verify: PDF and XML figures byte-for-byte consistent; PDF/A-3 conformance (fonts/ICC/no-JS/XMP) checked.
- [ ] **Invoice immutability:** Often missing — enforced in app code but an `UPDATE` path still exists. Verify: DB-level immutability after finalization; corrections create new documents.
- [ ] **Multi-tenancy:** Often missing — one report query or background job lacks tenant scope. Verify: RLS on *every* tenant table + green cross-tenant test suite.
- [ ] **VAT cases:** Often missing — reverse-charge (§13b), intra-EU, Kleinunternehmer notes and category codes. Verify: each scenario produces the mandated invoice text + correct EN 16931 category.
- [ ] **Numbering:** Often missing — race-safe + per-tenant. Verify: concurrent finalization test yields no duplicates; sequence is per-tenant.
- [ ] **Retention/DSGVO:** Often missing — "delete account" would wipe tax-relevant records. Verify: 10-year archive survives account deletion; only non-tax PII is erased.
- [ ] **Marketing copy:** Often missing — "GoBD-zertifiziert" slips in. Verify: only "GoBD-konform"; Verfahrensdokumentation exists.
- [ ] **Bank re-consent:** Often missing — assumes 90 days. Verify: 180-day AIS window handled + feed-break re-consent UX.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| `float` money in schema | HIGH | Migrate columns to `numeric`/integer-cents; backfill; re-verify all historical totals against EN 16931 rules; add regression tests |
| No RLS (app-only scoping) | HIGH | Add `tenant_id` where missing, write RLS policies for every table, set session GUC per request, add cross-tenant tests; audit for any leak that already occurred (breach-notification obligation) |
| Mutable finalized invoices already shipped | HIGH | Introduce state machine + DB immutability; reconcile/lock existing issued records; implement cancellation-invoice flow; document remediation in Verfahrensdokumentation |
| Duplicate invoice numbers issued | MEDIUM–HIGH | Cannot silently renumber issued invoices; issue corrections/cancellations per tax rules with advisor; fix generator to atomic per-tenant counter |
| "GoBD-zertifiziert" published | LOW | Remove claim, replace with "GoBD-konform," publish Verfahrensdokumentation |
| Assumed 90-day bank re-auth | LOW | Update to 180-day window + re-consent handling; no data migration needed |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Float / rounding money bugs | Phase 1 (foundational data model) | Golden-file tests vs KoSIT-valid reference XML; no `float` in schema |
| Multi-tenant leakage | Phase 1 (tenancy/auth) | RLS on every tenant table; cross-tenant test suite green |
| Numbering races / per-tenant sequences | Invoicing core (v1) | Concurrent-finalize test → zero duplicates; per-tenant series |
| Editing finalized invoices (immutability) | Invoicing core (v1) | DB-level immutability; corrections = new documents |
| PDF-as-e-invoice / XML-from-PDF | E-invoicing phase (v1) | KoSIT-validated structured XML from a data-first model |
| PDF/A-3 + hybrid mismatch | E-invoicing phase (v1) | PDF/A-3 conformance check; PDF↔XML figure equality |
| VAT category edge cases | Invoicing core (v1) + tax fast-follow | Scenario tests: §13b, intra-EU, Kleinunternehmer texts + codes |
| DSGVO/AVV/EU hosting | Phase 1 + pre-launch compliance pass | EU hosting confirmed; AVV available; export path works |
| 10-year retention vs erasure | GoBD-archive phase | Account deletion retains tax records; erases only non-tax PII |
| "GoBD-zertifiziert" claim | Any marketing/legal-copy phase | Copy audit; Verfahrensdokumentation shipped |
| Bank API 180-day re-auth / lock-in | Multibanking phase | 180-day handling; aggregator abstracted; cost modeled |
| DATEV export format | Bookkeeping/DATEV phase | Validated against DATEV spec; SKR03/04 mapping correct |
| Scope creep (see below) | Every planning cycle | v1 ships invoicing+e-invoicing only; bookkeeping deferred |

## Cross-cutting product pitfall: scope creep kills these projects

**What goes wrong:** The domain is enormous (invoicing → e-invoicing → bookkeeping → USt-VA → GoBD archive → multibanking → DATEV). Teams try to match Lexware/sevDesk's *full* feature surface before shipping, run out of runway, and never launch — or launch a shallow version of everything that's compliant at nothing.

**Why it happens:** Every feature looks mandatory because competitors have it; German accounting genuinely *is* interconnected (an invoice feeds bookkeeping feeds USt-VA).

**How to avoid:** Ship **v1 = invoicing + e-invoicing done correctly and compliantly** (the one thing that's newly legally mandated and where correctness is non-negotiable). Get the data model right so bookkeeping/DATEV/USt-VA can layer on later without rework. Defer bookkeeping, multibanking, and DATEV explicitly — they are separate phases, not v1. A compliant, trustworthy invoicing product beats a broad-but-shaky suite in this market where trust is the product.

**Warning signs:** v1 backlog includes bookkeeping or DATEV. "We need multibanking to launch." No clear line between v1 and later.

---

## Sources

**E-invoicing mandate & timeline (HIGH):**
- [eInvoicing in Germany — European Commission](https://ec.europa.eu/digital-building-blocks/sites/spaces/DIGITAL/pages/467108886/eInvoicing+in+Germany)
- [BMF FAQ zur obligatorischen E-Rechnung ab 1.1.2025 — Bundesfinanzministerium](https://www.bundesfinanzministerium.de/Content/DE/FAQ/e-rechnung.html)
- [Germany E-Invoicing Timeline 2025–2028 — Flick](https://www.flick.network/en-de/germany-e-invoicing-timeline)
- [IHK Hannover — Elektronische Rechnungen Pflicht ab 2025](https://www.ihk.de/hannover/hauptnavigation/recht/steuerrecht/umsatzsteuer/elektronische-rechnungen-pflicht-ab-2025-6168870)

**EN 16931 validation & rounding (HIGH/MEDIUM):**
- [EN16931 Validation Rule Map — Invoice Navigator](https://www.invoicenavigator.eu/blog/en16931-validation-rules-complete-guide)
- [Peppol / EN16931 error code lookup (BR-CO-10, BR-CO-15, BR-S-*)](https://peppolvalidator.com/peppol-validation-errors)
- [ConnectingEurope/eInvoicing-EN16931 Schematron model (official rules)](https://github.com/ConnectingEurope/eInvoicing-EN16931/blob/master/ubl/schematron/abstract/EN16931-model.sch)
- [Rounding on invoice lines / BR-CO-17 discussion — GitHub issue #143](https://github.com/ConnectingEurope/eInvoicing-EN16931/issues/143)

**ZUGFeRD / PDF/A-3 / profiles (MEDIUM):**
- [ZUGFeRD 2.3 Profiles, Validation & 2026 Mandate — Invoice Navigator](https://www.invoicenavigator.eu/learn/zugferd)
- [ZUGFeRD e-Invoices: PDF/A-3, CII XML, XMP metadata — Docentric](https://ax.docentric.com/zugferd-e-invoices-compliance-explained/)
- [ZUGFeRD Validator (PDF/A-3 + XML) — kostenlose-erechnung.de](https://kostenlose-erechnung.de/zugferd-validator/)

**GoBD / numbering / certification myth (HIGH/MEDIUM):**
- [Keine Pflicht zu lückenlos fortlaufenden Rechnungsnummern — GGD Steuerberater](https://www.ggd-steuer.de/keine-pflicht-zu-lueckenlos-fortlaufenden-rechnungsnummern/)
- [Fortlaufende Nummerierung von Rechnungen: Ausnahmen — Haufe](https://www.haufe.de/finance/buchfuehrung-kontierung/fortlaufende-nummerierung-von-rechnungen-ausnahmen_186_387400.html)
- [Rechnungsnummer GoBD-konform vergeben — kostenlose-erechnung.de](https://kostenlose-erechnung.de/ratgeber/rechnungsnummer-system-pflichten/)

**Kleinunternehmer / exemptions (HIGH):**
- [E-Rechnung Ausnahmen 2025 — Qonto](https://qonto.com/de/blog/kmu/management-buchhaltung/e-rechnung-ausnahmen)
- [E-Rechnung Pflicht für Kleinunternehmer — sevdesk](https://sevdesk.de/ratgeber/buchhaltung-finanzen/rechnungen/e-rechnung/kleinunternehmer-pflicht/)

**PSD2 / bank API 180-day re-auth (HIGH):**
- [90 Becomes 180: EBA Makes Key SCA Change — Vixio](https://www.vixio.com/insights/pc-90-becomes-180-eba-makes-key-sca-change)
- [180 days of secure connectivity in Europe — Plaid](https://plaid.com/blog/eu-reauth-update/)
- [EBA RTS on SCA & CSC amendment](https://www.eba.europa.eu/eba-response/29146)

---
*Pitfalls research for: Multi-tenant German accounting & e-invoicing SaaS*
*Researched: 2026-07-09*
