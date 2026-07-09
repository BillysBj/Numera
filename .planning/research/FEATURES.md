# Feature Research

**Domain:** German SME accounting & invoicing SaaS (Lexware Office / sevDesk competitor)
**Researched:** 2026-07-09
**Confidence:** HIGH (regulatory timeline + competitor feature matrices verified against official/current sources; some pricing-tier details MEDIUM)

## Context & Framing

German accounting SaaS is a mature, feature-dense market. The competitor set splits into two archetypes:

- **Full-suite "Buchhaltung"** (Lexware Office, sevDesk, orgaMAX, BuchhaltungsButler): invoicing + preliminary bookkeeping (vorbereitende Buchhaltung) + banking + DATEV/tax-advisor handoff. This is Numera's target.
- **Invoicing-first / lightweight** (Billomat, FastBill, Papierkram, easybill): strong invoicing, lighter bookkeeping. These are the "v1 shape" but users churn up to the full suites as they grow.

**Two regulatory facts dominate the roadmap and are non-negotiable:**

1. **GoBD** (Grundsätze zur ordnungsmäßigen Führung und Aufbewahrung von Büchern in elektronischer Form): every document and posting must be **unveränderbar** (immutable), auditable, and retained 10 years. This is an *architectural* constraint, not a feature — it shapes the data model from day one (append-only, audit trail, versioning). Retrofitting it later = rewrite.
2. **E-Rechnung mandate (§ 14 UStG, Wachstumschancengesetz):**
   - Since **01.01.2025**: all domestic B2B businesses must be able to **receive & process** structured e-invoices (XRechnung / ZUGFeRD ≥ 2.0.1, EN 16931). *This is already table stakes today.*
   - **01.01.2027**: businesses with prior-year turnover > €800k must **issue** e-invoices (paper/PDF no longer allowed for them).
   - **01.01.2028**: **all** domestic B2B must issue e-invoices. EDI transition ends.
   - EU **ViDA** e-reporting (near-real-time transaction reporting) slipping to ~2030-2032 — watch, don't build yet.

**Implication:** e-invoice *receive+parse* and *issue (XRechnung + ZUGFeRD)* are the single most defensible reason to build now. The market window is the 2027/2028 forced migration off paper/PDF.

## Feature Landscape

### Table Stakes (Users Expect These)

Missing any of these = product feels incomplete or non-compliant. Grouped by area.

#### Invoicing & Documents (v1 core)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Rechnungen erstellen (invoices) with legally-required fields | Core purpose; § 14 UStG mandates specific fields (USt-ID, Steuernummer, fortlaufende Nummer, Leistungsdatum) | MEDIUM | Field validation is the hard part, not the CRUD |
| Angebote / Auftragsbestätigungen / Lieferscheine | Standard document chain; convert offer→order→delivery→invoice | MEDIUM | Shared document engine; status transitions |
| PDF generation with own logo/branding | Every competitor has it; users judge on invoice look | MEDIUM | Template engine; German + English layouts |
| Fortlaufende, lückenlose Rechnungsnummern | Legal requirement (GoBD); gaps trigger audit flags | LOW | Must be gapless even on delete/cancel → use Storno, never delete |
| Storno / Rechnungskorrektur (cancellation invoice) | Cannot delete a sent invoice under GoBD; must cancel | MEDIUM | Depends on immutability model |
| Standardprodukte / Artikelstamm (product catalog) | Speeds repeat invoicing; expected | LOW | Feeds line items |
| Kunden-/Lieferantenstammdaten (contacts) | Nothing works without it | LOW | Foundation for CRM later |
| USt-Sätze (19/7/0%), Kleinunternehmer §19, Reverse-Charge basics | German VAT correctness; §19 users are a huge freelancer segment | MEDIUM | §19 = no VAT shown; must suppress correctly |
| **E-Rechnung empfangen & auslesen** (receive + parse XRechnung/ZUGFeRD) | Legally required since 2025 | HIGH | XML parsing, PDF/A-3 embedded extraction, validation vs EN 16931 |
| **E-Rechnung erstellen & übermitteln** (XRechnung + ZUGFeRD) | Forced by 2027/2028; core differentiator-turned-tablestakes | HIGH | Correct EN 16931 XML generation, validation, ZUGFeRD PDF/A-3 embedding |
| Zahlungserinnerungen & Mahnungen (dunning, multi-level) | Expected; sevDesk/Lexware standard | MEDIUM | Depends on Offene Posten (open items) |
| Wiederkehrende/Serienrechnungen (recurring/batch invoices) | Table stakes at sevDesk (from entry tier); subscription businesses need it | MEDIUM | Scheduler + template |
| E-Mail-Versand von Belegen | Sending invoices by email is baseline | LOW | Deliverability, own-domain sending later |
| German + English invoice output | Founder requirement; EU/export customers | MEDIUM | i18n of document templates specifically |

#### Bookkeeping & Tax (later milestones, but table stakes for the "full suite" positioning)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Belegerfassung / Belegscanner + OCR (receipt capture) | Every full-suite competitor; core of "digitale Buchhaltung" | HIGH | OCR/extraction; mobile photo capture; buy vs build the OCR |
| GoBD-konformes Belegarchiv (immutable long-term archive) | Legal 10-yr retention, unveränderbar | HIGH | Architectural — WORM-style storage, audit trail |
| Offene Posten (open items — OP-Liste) | Foundation for dunning + reconciliation | MEDIUM | Debtor/creditor open-item ledger |
| USt-Voranmeldung (UStVA) + ELSTER submission | Core recurring tax obligation for most SMEs | HIGH | ELSTER ERiC integration; certificate handling |
| Zusammenfassende Meldung (ZM) | Required for EU B2B sales | MEDIUM | Depends on EU-invoice handling |
| EÜR (Einnahmen-Überschuss-Rechnung) | Default profit calc for freelancers/small biz | MEDIUM | Cash-basis P&L; Anlage EÜR form |
| DATEV export (Buchungsstapel / DATEV-Format CSV) | The #1 "does my Steuerberater accept it" gate | HIGH | DATEV ASCII/CSV format is finicky; near-universal expectation |
| Elektronisches Kassenbuch (cash book) | Table stakes for retail/gastro; GoBD cash rules | MEDIUM | GoBD Kassenführung rules |
| GuV & BWA (P&L, business eval report) | Expected once bookkeeping exists | MEDIUM | Reporting layer over postings |

#### Banking (later milestone; table stakes for full suite)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Kontoanbindung / Multibanking (bank feeds) | Core of "automatische Buchhaltung"; every full suite has it | HIGH | Use aggregator (finAPI/Tink/GoCardless), NOT self-built FinTS — see Stack |
| **Automatischer Zahlungsabgleich** (auto payment reconciliation) | The single most-loved feature in reviews; matches txns↔invoices | HIGH | Depends on banking + Offene Posten + matching logic |
| Überweisungen auslösen (SEPA payments / PIS) | Expected in banking modules | MEDIUM | PIS via aggregator; SCA/2FA flows |

#### Platform / Account

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Multi-tenant accounts, roles (owner/employee/Steuerberater) | SaaS baseline; tax-advisor access is expected | MEDIUM | RBAC; tenant isolation critical |
| Responsive PWA + mobile (photo receipt capture) | sevDesk/Lexware mobile apps are a decision factor | MEDIUM | Founder requirement; PWA covers it |
| Steuerberater-Zugang (accountant login) | Nearly universal; reduces churn | LOW–MEDIUM | A role + scoped view; enables Pendelakte |
| Data export (DSGVO / portability) | GDPR + user trust; "not locked in" | LOW | |

### Differentiators (Competitive Advantage)

Not required, but where Numera can win. Align these with the founder's core value.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **E-Rechnung done right, in every tier** | Lexware gates XRechnung/ZUGFeRD *sending* behind its top XL tier (€32.90); sevDesk is more generous. Making full e-invoicing standard/cheap is a sharp wedge into the 2027/28 migration | HIGH | The market's biggest current friction point |
| **Modern, fast PWA UX (desktop + mobile parity)** | sevDesk wins praise for its app; Lexware feels dated. A genuinely fast responsive PWA is a real differentiator | MEDIUM | Founder is already building this way |
| **First-class German + English UI** | Most competitors are German-only in UI; serves international founders/GmbHs in DE, expat freelancers | MEDIUM | Full app i18n, not just invoice output |
| Umsatzsteuerprognose / liquidity forecast | Lexware markets USt-Prognose; cashflow planning is high-value, low-penetration | MEDIUM | Depends on banking + open items + tax data |
| Online-Kundenportal (customer portal: view/pay invoices) | Reduces payment friction; not universal | MEDIUM | Depends on invoicing + payments |
| Elektronische Pendelakte (structured accountant handoff) | Deeper than raw DATEV export; smoother Steuerberater workflow = stickiness | MEDIUM | Depends on Belegarchiv + DATEV export |
| Belegempfang per E-Mail (dedicated inbox address for receipts) | Forward supplier invoices → auto-captured; loved feature | MEDIUM | Depends on Belegerfassung/OCR |
| Public API + Webhooks | Lexware/sevDesk gate API to higher tiers; integrations (shops, tools) drive stickiness | MEDIUM | Design early, expose later |
| Anlagenverwaltung (fixed-asset register + depreciation) | Present in higher tiers; nice for GmbH/UG | MEDIUM | Depends on bookkeeping |
| Besondere Rechnungstypen (Abschlag/partial, §13b Bauleistungen, EU, Fremdwährung) | sevDesk includes these earlier than Lexware; §13b + foreign currency serve real niches | MEDIUM–HIGH | Reverse-charge & currency logic |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **Lohn & Gehalt (payroll)** | "Do everything in one place" | Requires **ITSG/GKV certification**, SV-Meldeverfahren, huge annual compliance burden; a whole product | **Explicitly out of scope** (founder agrees). Integrate/partner instead (e.g. link to a payroll provider) |
| **Full double-entry GL / Finanzbuchhaltung (Bilanz/GuV for accountants)** | Larger GmbHs want full Fibu | Competes with DATEV Kanzlei-Rechnungswesen; heavy; wrong audience | Do **vorbereitende Buchhaltung** + clean DATEV export; let the Steuerberater do the Abschluss |
| **Warenwirtschaft / full ERP (inventory, stock, warehousing)** | E-commerce sellers ask for it | Lexware doesn't have it; sevDesk only as add-on; deep rabbit hole | Product catalog + delivery notes only; integrate via API to shop/WaWi systems |
| **Building own FinTS/HBCI bank connectors** | "Direct bank access, no middleman" | Per-bank quirks, PSD2/SCA maintenance, TPP licensing = never-ending ops cost | Use a licensed aggregator (finAPI / Tink / GoCardless) |
| **Building own OCR/receipt-extraction engine** | Control, no per-scan cost | ML/OCR is a product itself; German-invoice accuracy is hard | Buy OCR (cloud OCR / DATEV-grade extraction API); revisit only at scale |
| **Own ELSTER protocol implementation from scratch** | Avoid dependency | ERiC is a certified native library with strict rules | Use official **ERiC** library / a compliant wrapper — do not reinvent |
| **"Real-time everything" / live collaborative editing on invoices** | Feels modern | Invoices are immutable-once-sent under GoBD; real-time co-edit adds complexity without accounting value | Immutable documents + clear status states; optimistic UI where safe |
| **Steuerberatung / tax advice content in-app** | Users want answers | Legal exposure (Steuerberatungsgesetz — advice is regulated) | Surface data + hand off to the user's Steuerberater; no advice |
| **Kryptowährung / niche tax handling in v1** | Vocal minority | Long tail; distracts from core | Defer indefinitely |

## Feature Dependencies

```
Contacts (Kunden/Lieferanten)
    └──required by──> Invoicing
                         ├──required by──> Offene Posten (open items)
                         │                    ├──required by──> Mahnwesen (dunning)
                         │                    └──required by──> Zahlungsabgleich
                         ├──required by──> E-Rechnung erstellen (XRechnung/ZUGFeRD)
                         └──required by──> Online-Kundenportal

Immutable data model + Audit trail (GoBD)   [ARCHITECTURAL — must exist first]
    └──enables──> GoBD-Belegarchiv
                     ├──required by──> Belegerfassung/OCR
                     │                    └──enhanced by──> Belegempfang per E-Mail
                     └──required by──> Elektronische Pendelakte

Belegerfassung + Invoicing (postings)
    └──required by──> EÜR / GuV / BWA
                         └──required by──> USt-Voranmeldung (UStVA) ──needs──> ELSTER/ERiC
    └──required by──> DATEV-Export ──required by──> Pendelakte

Bank aggregator (finAPI/Tink/GoCardless)
    └──enables──> Multibanking / Kontoanbindung
                     ├──[+ Offene Posten]──required by──> Automatischer Zahlungsabgleich
                     ├──required by──> Überweisungen (PIS/SEPA)
                     └──[+ open items + tax]──required by──> Umsatzsteuerprognose / Liquidität

E-Rechnung empfangen (parse)  ──independent of──> E-Rechnung erstellen (generate)
   (both need EN 16931 mapping layer; share validation)
```

### Dependency Notes

- **Immutability/GoBD is foundational, not a feature phase.** Build the append-only + audit-trail data model *before* invoicing writes real data. Retrofitting = rewrite. Highest-priority architectural constraint.
- **Automatischer Zahlungsabgleich requires banking AND Offene Posten AND a matching engine** — three prerequisites. It's the crown-jewel feature but sits late in the dependency graph.
- **Mahnwesen and Zahlungsabgleich both depend on Offene Posten**, which depends on invoicing having a proper debtor ledger (not just PDF generation). Design the OP ledger even in the invoicing milestone.
- **UStVA depends on ELSTER/ERiC** (certified library) *and* on having categorized bookings — needs bookkeeping first.
- **DATEV export depends on structured postings**, and **Pendelakte builds on DATEV export + Belegarchiv**.
- **E-Rechnung receive and issue are somewhat independent** but share an EN 16931 mapping/validation layer — build that shared core once. Receiving is table stakes *today*; issuing is the 2027/28 wedge.
- **Belegempfang per E-Mail enhances Belegerfassung** (same OCR pipeline, different intake).

## MVP Definition

### Launch With (v1) — Invoicing + E-Invoicing core

Founder's stated v1. Ruthlessly minimal but compliant and defensible.

- [ ] Multi-tenant auth, roles (owner + basic team), tenant isolation — *SaaS foundation*
- [ ] **Immutable/append-only data model + audit trail** — *GoBD architecture, must be right from line one*
- [ ] Contacts (customers/suppliers) — *nothing works without it*
- [ ] Invoices with all § 14 UStG legal fields, gapless numbering, Storno — *core, legally correct*
- [ ] Angebote / Auftragsbestätigungen / Lieferscheine (convertible document chain) — *expected doc set*
- [ ] Standardprodukte (product catalog) + line items — *speed*
- [ ] USt handling: 19/7/0%, Kleinunternehmer §19, basic reverse-charge — *German correctness*
- [ ] PDF generation, own branding, **German + English** templates — *table stakes + differentiator*
- [ ] **E-Rechnung erstellen: XRechnung + ZUGFeRD (EN 16931)** — *the wedge; legally rising to mandatory*
- [ ] **E-Rechnung empfangen & auslesen** (parse/validate XRechnung/ZUGFeRD) — *table stakes since 2025*
- [ ] Email dispatch of documents — *baseline*
- [ ] Offene Posten ledger (even if minimal UI) — *foundation for dunning/reconciliation later*
- [ ] Zahlungserinnerungen & Mahnungen — *high value, low incremental cost once OP exists*
- [ ] Responsive PWA (desktop + mobile) — *founder requirement*

### Add After Validation (v1.x) — Bookkeeping + Banking

- [ ] Belegerfassung + OCR (mobile photo + upload) — *trigger: users asking to store receipts / go paperless*
- [ ] GoBD-Belegarchiv (surfacing the archive built into the data model) — *pairs with Belegerfassung*
- [ ] Belegempfang per E-Mail — *trigger: OCR pipeline stable*
- [ ] Bank aggregator integration (Multibanking) — *trigger: reconciliation demand*
- [ ] **Automatischer Zahlungsabgleich** — *the retention feature; needs banking + OP*
- [ ] EÜR / GuV / BWA reporting — *trigger: users doing their own preliminary books*
- [ ] DATEV export (Buchungsstapel) — *trigger: first Steuerberater asks "can I get DATEV?"*
- [ ] Steuerberater-Zugang (role + scoped view) — *pairs with DATEV/Pendelakte*
- [ ] Serienrechnungen / recurring — *trigger: subscription-billing customers*
- [ ] Elektronisches Kassenbuch — *trigger: retail/gastro segment*

### Future Consideration (v2+)

- [ ] UStVA + ZM via ELSTER/ERiC — *defer: heavy certification-adjacent work; large but high-value*
- [ ] Überweisungen (PIS/SEPA payments) — *defer: SCA complexity; needs banking maturity*
- [ ] Umsatzsteuerprognose / liquidity forecast — *defer: needs banking + tax data maturity; strong differentiator later*
- [ ] Online-Kundenportal — *defer until invoicing + payments proven*
- [ ] Elektronische Pendelakte — *defer: needs archive + DATEV export solid*
- [ ] Public API + Webhooks — *design early, expose in v2; integration play*
- [ ] Anlagenverwaltung (fixed assets + depreciation) — *defer: GmbH/UG audience, needs bookkeeping*
- [ ] Besondere Rechnungstypen (§13b Bauleistungen, Abschlagsrechnungen, Fremdwährung) — *defer: niche logic; add per-segment demand*
- [ ] CRM depth (Aufgaben/Erinnerungen/Notizen, Umsatzstatistiken, Kundenakte) — *defer: grows out of contacts*

**Explicitly never (anti-features):** Lohn & Gehalt, full Fibu/Bilanz, Warenwirtschaft/ERP, own FinTS connectors, own OCR engine, in-app Steuerberatung.

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Immutable data model + audit trail (GoBD) | HIGH (invisible but foundational) | MEDIUM | P1 |
| Invoices (legal fields, numbering, Storno) | HIGH | MEDIUM | P1 |
| E-Rechnung erstellen (XRechnung/ZUGFeRD) | HIGH | HIGH | P1 |
| E-Rechnung empfangen & auslesen | HIGH | HIGH | P1 |
| Contacts | HIGH | LOW | P1 |
| Angebote/Lieferscheine/AB doc chain | HIGH | MEDIUM | P1 |
| PDF branding + DE/EN templates | HIGH | MEDIUM | P1 |
| Kleinunternehmer §19 / USt handling | HIGH | MEDIUM | P1 |
| Offene Posten ledger | MEDIUM (enabler) | MEDIUM | P1 |
| Mahnwesen / Zahlungserinnerungen | HIGH | MEDIUM | P2 |
| Responsive PWA (mobile parity) | HIGH | MEDIUM | P1 |
| Belegerfassung + OCR | HIGH | HIGH | P2 |
| Automatischer Zahlungsabgleich | HIGH | HIGH | P2 |
| Multibanking (aggregator) | HIGH | HIGH | P2 |
| DATEV export | HIGH | HIGH | P2 |
| EÜR/GuV/BWA | MEDIUM | MEDIUM | P2 |
| Steuerberater-Zugang | MEDIUM | LOW–MEDIUM | P2 |
| Serienrechnungen | MEDIUM | MEDIUM | P2 |
| UStVA via ELSTER | HIGH | HIGH | P3 |
| Umsatzsteuerprognose | MEDIUM | MEDIUM | P3 |
| Online-Kundenportal | MEDIUM | MEDIUM | P3 |
| Public API + Webhooks | MEDIUM | MEDIUM | P3 |
| Pendelakte | MEDIUM | MEDIUM | P3 |
| Anlagenverwaltung | LOW–MEDIUM | MEDIUM | P3 |
| Lohn & Gehalt | (out of scope) | VERY HIGH | — |

**Priority key:** P1 = must have for launch · P2 = should have, add when possible · P3 = future consideration.

## Competitor Feature Analysis

| Feature | Lexware Office | sevDesk | Smaller (Billomat/FastBill/Papierkram/orgaMAX) | Numera's Approach |
|---------|----------------|---------|-----------------------------------------------|-------------------|
| E-Rechnung *senden* (XRechnung/ZUGFeRD) | Only in top **XL** tier (~€32.90) | Included earlier / more generous | Varies; many support it | **In every tier** — the wedge into 2027/28 migration |
| E-Rechnung *empfangen* | S/M/L tiers receive-only | Yes | Mostly yes | Standard from v1 |
| Special invoice types (Abschlag, §13b, EU, Fremdwährung) | Gated to XL | From entry "Rechnung" tier; also Fremdwährung, Abrechnungsgutschrift | Mixed | Core types v1; special types phased by demand |
| Serienrechnungen | XL only | Entry tier | FastBill/Billomat: yes | v1.x, not gated harshly |
| Belegerfassung + OCR | Yes (higher tiers) | Yes (Belegerkennung) | orgaMAX yes; Papierkram yes | v1.x, buy OCR |
| Automatischer Zahlungsabgleich | Yes | Yes | Billomat/orgaMAX/FastBill: yes | v1.x, via aggregator + OP engine |
| Multibanking | Yes | Yes (integriertes Onlinebanking) | Yes | Aggregator (finAPI/Tink) |
| DATEV export | Yes | Yes | orgaMAX: auto DATEV+ELSTER | Yes, v1.x |
| Warenwirtschaft | **None** | Add-on only | orgaMAX: partial | **Out of scope** (catalog + Lieferscheine only) |
| Lohn & Gehalt | Add-on (~€12.90) | **None** | Mostly none | **Out of scope** |
| Mobile app quality | Dated | **Praised, modern** | Mixed | Modern PWA = parity/lead vs Lexware |
| DE + EN UI | German-focused | German-focused | German-focused | **DE + EN** = differentiator |
| Free tier | 30-day trial only | Permanent free (≤3 invoices/mo) | Varies | (Pricing TBD — free/trial decision downstream) |

## Sources

- BMF FAQ zur E-Rechnung (obligatorische E-Rechnung ab 01.01.2025): https://www.bundesfinanzministerium.de/Content/DE/FAQ/e-rechnung.html
- IHK München — Elektronische Rechnungen (Fristen 2025/2027/2028, EN 16931, XRechnung/ZUGFeRD ≥2.0.1): https://www.ihk-muenchen.de/ratgeber/steuern/elektronische-rechnungen/
- Haufe — Elektronische Rechnung wird Pflicht (Überblick, Übergangsfristen): https://www.haufe.de/steuern/gesetzgebung-politik/elektronische-rechnung-wird-pflicht-e-rechnung-im-ueberblick_168_605558.html
- e-rechnung.tools — E-Rechnungspflicht 2026/2027 (turnover thresholds, EDI transition): https://www.e-rechnung.tools/ratgeber/e-rechnungspflicht
- sevDesk vs Lexware Office feature comparison (tier gating, invoice types, WaWi, payroll): https://sevdesk.de/lexware-office-alternative/ and https://www.mysoftware.de/buchhaltung-finanzen/sevdesk-vs-lexoffice/
- kostenlose-erechnung.de — lexoffice vs sevdesk 2026 feature matrix: https://kostenlose-erechnung.de/ratgeber/lexoffice-vs-sevdesk/
- BuchhaltungsButler competitor overviews (Papierkram, Billomat, orgaMAX, FastBill features): https://www.buchhaltungsbutler.de/alternative-zu-orgamax/ and related pages
- Lexware DATEV-Schnittstelle (Buchungsstapel, GoBD): https://www.lexware.de/funktionen/datev-schnittstelle/
- finAPI — Open Banking / Multibanking (XS2A vs FinTS/HBCI, PSD2): https://www.finapi.io/en/products/open-banking/banking-api/
- heise — Neue und alte Banken-APIs (FinTS/HBCI/PSD2 landscape): https://www.heise.de/hintergrund/Neue-und-alte-Banken-APIs-eine-Uebersicht-4907369.html

---
*Feature research for: German SME accounting & invoicing SaaS (Numera)*
*Researched: 2026-07-09*
