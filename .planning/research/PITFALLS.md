# Pitfalls Research

**Domain:** German accounting/invoicing SaaS — v2.0 new domains (Buchhaltung, Banking, Belege/OCR, Monetarisierung) added to an existing GoBD/DSGVO multi-tenant .NET 10 + Postgres RLS system
**Researched:** 2026-08-02
**Confidence:** HIGH for German accounting/GoBD rules and integration with the existing v1 (well-established law + known codebase); MEDIUM for finAPI/Stripe operational specifics (verify current API behaviour during the owning phase)

> Phase names below refer to *suggested* v2.0 phases (the v2 roadmap is not yet cut). Map them to real phase numbers when the roadmap is created. Suggested domain/phase buckets:
> **B1 Buchhaltungs-Fundament** (Kontenrahmen SKR03/04, Buchungslogik, Journal, Festschreibung) · **B2 Kassenbuch** · **B3 Auswertungen** (EÜR/GuV/BWA, USt-VA) · **BK1 Banking-Anbindung** (finAPI, Umsatzimport) · **BK2 Zahlungsabgleich** · **BK3 SEPA-Überweisung (PIS)** · **BE1 Belegscan/OCR** · **BE2 E-Mail-Intake** · **BE3 GoBD-Archiv** · **M1 Stripe-Abrechnung**.

---

## Critical Pitfalls

### Pitfall 1: Unbalanced postings / Soll≠Haben slips into the journal

**What goes wrong:**
A booking (Buchungssatz) is persisted where the sum of debits (Soll) ≠ sum of credits (Haben), or a multi-line split booking rounds each line independently and the total drifts by a cent. Every downstream report (GuV, BWA, USt-VA, Summen- und Saldenliste) is then silently wrong, and a Bilanz will not close.

**Why it happens:**
Teams model a booking as a flat row (one debit account + one credit account + amount) and only later discover split bookings (one gross line → net + multiple tax lines, or one payment → several open items). Balance is then enforced per-UI-form instead of as a hard invariant in the domain/DB.

**How to avoid:**
- Model a booking as a **header + N lines**, each line carrying a signed amount or an explicit Soll/Haben side. Enforce `SUM(soll) = SUM(haben)` as a domain invariant *and* a DB-level check (deferrable constraint or a trigger over the lines) — never trust the UI.
- Store all amounts as `decimal` (reuse v1's money type). Compute tax on the document total, then derive the balancing net; never round each split line independently and hope they add up. Apply one documented rounding rule and put the rounding remainder on a defined line.
- Golden-file tests: a set of canonical bookings (gross with 19%, 7%, §13b reverse-charge, i.g. Erwerb, split payment across 3 open items) that must balance to the cent.

**Warning signs:**
GuV total ≠ Summen-/Saldenliste; a "Differenzkonto"/"Verrechnungskonto" slowly accumulates cents; USt-VA Zahllast off by small amounts.

**Phase to address:** B1 Buchhaltungs-Fundament (must be an invariant from the first booking written).

---

### Pitfall 2: Editing/deleting a posted booking — violating GoBD immutability (Unveränderbarkeit)

**What goes wrong:**
A "festgeschriebene" (recorded/finalised) booking is edited or hard-deleted in place. This breaks GoBD (Unveränderbarkeit, Nachvollziehbarkeit) exactly like editing a finalised invoice would — and it is the single most common way German accounting software becomes non-compliant. The tax office can reject the whole bookkeeping (Verwerfung der Buchführung) and estimate (Schätzung).

**Why it happens:**
Developers reuse ordinary CRUD/EF update semantics. In accounting there is a legal line: a booking may be freely edited only while it is a *draft/Stapel (unfestgeschrieben)*; once *festgeschrieben* it is immutable and corrections happen only via a **Stornobuchung/Generalumkehr** (reversal) plus a new correct booking.

**How to avoid:**
- Two-state model: **Stapelbuchung (draft, editable/deletable)** vs **festgeschriebene Buchung (immutable)**. After Festschreibung, block UPDATE/DELETE at the domain layer and with Postgres (revoke row updates via RLS/trigger; the trigger raises on any change to a festgeschrieben row). Reuse the v1 GoBD-immutability pattern already proven on the Belegkette.
- Corrections = new reversal booking (Storno/Generalumkehr) that references the original, never mutation. Keep the append-only audit log (reuse v1's unveränderbares Audit-Log).
- Every booking carries an immutable, gap-free journal number (fortlaufende Belegnummer/Journalnummer) assigned at Festschreibung, using the v1 race-safe numbering pattern.

**Warning signs:**
An `UPDATE ledger_entry` appears anywhere outside draft state; "delete booking" button on a posted item; audit log has fewer rows than corrections made.

**Phase to address:** B1 Buchhaltungs-Fundament (Festschreibung + reversal model is foundational; retrofitting is a rewrite).

---

### Pitfall 3: Period locks / Festschreibung done too weakly (or too late)

**What goes wrong:**
Bookings can still be added to or changed in a period that was already reported to the Finanzamt (USt-VA submitted). A late booking into a closed period changes the VAT that was already declared → the declaration is now wrong and must be corrected (berichtigte Anmeldung), or worse, goes undetected.

**Why it happens:**
Festschreibung is treated as a per-booking flag only, with no concept of a **closed accounting period (Voranmeldungszeitraum / Wirtschaftsjahr)**. Or Festschreibung is deferred "until the user clicks export", leaving a long window of mutable history.

**How to avoid:**
- Two levels: (1) per-booking Festschreibung, (2) **period close** (Zeitraum-Sperre) that hard-blocks new/changed bookings with a `buchungsdatum` inside a locked period. A booking into a locked period must be rejected or forced into the next open period with an explicit user decision.
- Lock the period automatically when its USt-VA is generated/exported; require an explicit "Berichtigung" flow to touch a locked period (creates a correcting declaration, never silent).
- GoBD expects Festschreibung "zeitnah" — at the latest by the USt-VA deadline of the following month. Prompt/auto-festschreiben rather than leaving drafts open for months.

**Warning signs:**
`buchungsdatum` in a period whose USt-VA status = submitted; two different USt-VA totals for the same period without a Berichtigung record.

**Phase to address:** B3 Auswertungen (period lock tied to USt-VA), foundations in B1.

---

### Pitfall 4: Wrong SKR03/04 account mapping and misused VAT keys (Steuerschlüssel / Automatikkonten)

**What goes wrong:**
Accounts are mapped incorrectly between SKR03 and SKR04, or a VAT key (Steuerschlüssel) is applied on top of an **Automatikkonto** that already carries a built-in tax rate — producing double VAT or the wrong Kennziffer on the USt-VA. Classic: posting 19% to an account that is a 7% automatic account, or manually adding a tax key to an Automatikkonto (e.g. SKR03 8400) that already implies 19%.

**Why it happens:**
SKR03 (Prozessgliederung, account 8400 = Erlöse 19%) and SKR04 (Abschlussgliederung, revenues in the 4000s) are *different numbering schemes*, and many accounts are **Automatikkonten** with an implicit tax rate + implicit USt-VA Kennziffer. Teams model accounts as dumb numbers and bolt VAT on separately, unaware of the automatic behaviour DATEV/Lexware users expect.

**How to avoid:**
- Ship **both** SKR03 and SKR04 as seeded, versioned chart-of-accounts data (per-tenant choice, fixed at company setup). Store each account's properties: type (Aktiv/Passiv/Aufwand/Ertrag), whether it is an Automatikkonto, its implicit Steuerschlüssel, and its USt-VA Kennziffer mapping.
- Model Steuerschlüssel as first-class (e.g. DATEV-style keys plus reverse-charge/§13b and i.g. keys). Validation: reject a manual tax key on an Automatikkonto that already implies one; reject a revenue account posting whose rate contradicts the account.
- Drive the USt-VA from the **account + Steuerschlüssel**, not from re-deriving VAT at report time. This is the single source of truth DATEV-literate accountants and Steuerberater will audit.

**Warning signs:**
VAT appears twice on a booking; a Steuerberater flags "falscher Steuerschlüssel"; the same economic transaction maps to different Kennziffern depending on SKR chosen.

**Phase to address:** B1 Buchhaltungs-Fundament (chart + Steuerschlüssel are the core data model).

---

### Pitfall 5: USt-Voranmeldung Kennziffern wrong, or wrong period / Ist- vs Soll-Versteuerung

**What goes wrong:**
The USt-VA reports the wrong Kennziffern (e.g. revenue in Kz 81/86, Vorsteuer in Kz 66, §13b in Kz 60/67, i.g. Erwerb in Kz 89/61) or computes the wrong Zahllast (Kz 83). Or — the deeper trap — VAT is recognised in the wrong period because the tenant is on **Ist-Versteuerung** (VAT due when payment received) but the software books it **Soll** (VAT due when invoice issued), or vice-versa. Wrong period = wrong declaration every single month.

**Why it happens:**
Ist- vs Soll-Versteuerung (§20 vs §16/§13 UStG) is a per-tenant tax setting most developers have never heard of. Under Ist-Versteuerung the *bank/payment date* drives the USt-VA period, not the invoice date — which couples Banking/reconciliation directly into the VAT engine. Kennziffern mapping is fiddly and version-dependent (the ELSTER form changes yearly).

**How to avoid:**
- Store **Besteuerungsart (Ist/Soll)** per tenant/company and make it drive which date (Leistungs-/Rechnungsdatum vs Zahlungsdatum) determines the USt-VA period. Under Ist, unpaid open items must NOT yet appear in the USt-VA — only reconciled payments do.
- Encode the Kennziffern mapping as versioned data keyed by year (the UStVA form is amended annually). Test the calculation against worked examples for 19%, 7%, 0/§19 Kleinunternehmer, §13b reverse-charge, i.g. Lieferung/Erwerb.
- Dauerfristverlängerung and Sondervorauszahlung (Kz 38/39) exist — model the filing calendar (monthly vs quarterly) per tenant.
- Produce the official **ELSTER-compatible export** (UStVA XML/ERiC datamodel) so numbers can be re-validated; do not invent your own format. (Direct ERiC transmission is explicitly out of scope per PROJECT.md — calc + export only.)

**Warning signs:**
A Kleinunternehmer (§19) tenant shows a Zahllast; an Ist-Versteuerung tenant's USt-VA moves when an invoice is issued rather than paid; Kennziffer sums don't reconcile to the booking journal.

**Phase to address:** B3 Auswertungen (USt-VA), but Ist/Soll must be a tenant setting from B1 and consumed by BK2 Zahlungsabgleich.

---

### Pitfall 6: EÜR vs GuV vs Bilanz confusion — building the wrong report for the tenant type

**What goes wrong:**
The system offers a GuV/Bilanz to a Freiberufler who files **EÜR (§4(3) EStG cash-basis)**, or an EÜR to a GmbH that is legally required to keep **doppelte Buchführung + Bilanz**. Users get a legally inappropriate report and mis-file; the double-entry engine and the cash-basis EÜR have fundamentally different revenue-recognition timing (accrual vs cash).

**Why it happens:**
The three are conflated as "the P&L". In reality: EÜR = Einnahmen-Überschuss-Rechnung (cash in/out, for Freiberufler/small businesses under thresholds), GuV = accrual P&L (part of double-entry), Bilanz = balance sheet (mandatory for GmbH/UG/Kaufleute). Bilanzierungspflicht depends on legal form and §141 AO revenue/profit thresholds.

**How to avoid:**
- Store **Gewinnermittlungsart** per tenant (EÜR vs Betriebsvermögensvergleich/Bilanzierung), driven by legal form (Freiberufler/Einzelunternehmen vs GmbH/UG) and thresholds. Gate report availability on it.
- Even with a single double-entry engine underneath, EÜR needs cash-basis timing and its own official form (Anlage EÜR); GuV/Bilanz need accrual. Don't ship "a P&L" — ship the *right* Gewinnermittlung per tenant.
- v2.0 target is EÜR/GuV/BWA + USt-VA; a full Bilanz for GmbH/UG is a heavier follow-on — scope explicitly and don't half-build a Bilanz.

**Warning signs:**
A GmbH tenant has no balance sheet; a Freiberufler is asked for opening balances (Eröffnungsbilanz) they don't need; BWA numbers don't tie to the chosen Gewinnermittlungsart.

**Phase to address:** B3 Auswertungen; tenant `Gewinnermittlungsart`/`Rechtsform` captured in B1.

---

### Pitfall 7: Non-idempotent bank transaction import → duplicate Umsätze

**What goes wrong:**
The same bank transaction is imported twice (finAPI re-fetch, webhook retry, overlapping date windows, user manual re-sync) and appears as two Umsätze. Reconciliation then double-matches open items, or the Kassenbuch/bank balance doubles. In accounting, duplicated money is a correctness catastrophe.

**Why it happens:**
Bank transactions often lack a stable unique ID across fetches; teams key on `(date, amount, purpose)` which collides for legitimately identical transactions (e.g. two €9.99 charges same day). finAPI delta imports and webhook re-delivery are assumed to be exactly-once.

**How to avoid:**
- Persist finAPI's transaction id and store an **idempotency key** per imported transaction; upsert on it. Where the provider id is unstable, build a composite fingerprint but retain a per-import dedup window and mark suspected duplicates for review rather than auto-hiding real duplicates.
- Make the import job idempotent end-to-end (safe to re-run any window). Track a last-synced cursor per bank connection per tenant.
- Never let import auto-create bookings; import creates *Umsatz records*, reconciliation proposes bookings, a human/rule confirms.

**Warning signs:**
Bank balance in Numera drifts from the real bank balance; two Umsätze with identical provider id; an open item matched twice.

**Phase to address:** BK1 Banking-Anbindung (import idempotency); BK2 for match idempotency.

---

### Pitfall 8: Reconciliation mis-matches — partial payments, over/underpayment, one-to-many

**What goes wrong:**
Auto-reconciliation matches a €500 incoming payment to a €500 open item that was actually paid in two €250 tranches, or matches a bulk payment covering five invoices to just one, or auto-clears an open item on a fuzzy purpose-text match that is wrong. This corrupts the v1 Offene-Posten/Zahlungserfassung ledger.

**Why it happens:**
Reconciliation is modelled as 1 payment ↔ 1 invoice with a similarity score, ignoring that real payments are many-to-many (Sammelüberweisung, Teilzahlung, Skonto deduction, bank fees, overpayment/Guthaben). Teams auto-clear on weak confidence to look "smart".

**How to avoid:**
- Model matching as **N Umsätze ↔ M Offene Posten** with allocation amounts (reuse v1's Teilzahlung support). Support Skonto, rounding differences, and bank charges as explicit allocation lines.
- **Confidence tiers:** auto-match only on strong signals (exact amount + Verwendungszweck contains invoice number/Kundennummer); everything else is a *proposal* the user confirms. Never auto-clear a partial/ambiguous match.
- Matching must be idempotent and reversible: un-matching restores the open item exactly (important once a booking may already be festgeschrieben — then un-match = reversal, not delete).

**Warning signs:**
Open items disappear without full payment; Skonto/fees create phantom residuals; users complain the auto-match "guessed wrong".

**Phase to address:** BK2 Zahlungsabgleich (integrates tightly with v1 Offene Posten).

---

### Pitfall 9: PSD2 consent expiry and finAPI sandbox-vs-prod surprises

**What goes wrong:**
Bank connections silently stop syncing because the PSD2 access consent expired and needs re-authentication (SCA) by the user — but the product has no re-consent UX, so data goes stale and reconciliation quietly rots. Separately: everything works in finAPI sandbox (mock banks) and breaks in production (real bank quirks, TPP licence, webhooks, rate limits).

**Why it happens:**
PSD2/RTS requires periodic SCA renewal for AIS access. Originally 90 days; the **EBA RTS amendment (final report 2022) extended the renewal from 90 to 180 days** and introduced a mandatory AISP exemption — but banks implement differently and consent still expires and must be renewed by the user. Teams assume a one-time connect. Sandbox hides licensing, real-bank field variance, and the consent lifecycle.

**How to avoid:**
- Track consent/validity per bank connection; proactively notify the user before expiry and provide a first-class **re-consent (SCA) flow**. Show connection health ("letzter erfolgreicher Abruf", "Zustimmung läuft ab am").
- Decide the TPP model early: Numera acting under **finAPI's licence** (finAPI/SCHUFA as regulated AISP/PISP) vs needing own BaFin registration. Using finAPI as the licensed provider avoids an own PSD2 licence — confirm contractually before building.
- Test against finAPI **XS2A test banks in sandbox AND run a real bank in a staging tenant** before GA. Handle provider webhooks/notifications and rate limits.

**Warning signs:**
Sync last-success timestamps aging; support tickets "meine Bank aktualisiert nicht"; a code path only ever exercised against sandbox mock banks.

**Phase to address:** BK1 Banking-Anbindung (consent lifecycle + connection health as a first-class feature, not an afterthought).

---

### Pitfall 10: SEPA transfers (PIS) without correct SCA / confirmation / idempotency → double or wrong payments

**What goes wrong:**
A SEPA-Überweisung is initiated twice (retry/double-click), or executed without proper strong customer authentication, or with a rounding/IBAN error. Unlike read-only AIS, this **moves real money** — the blast radius is a wrong or duplicated outgoing payment.

**Why it happens:**
PIS (Payment Initiation) is treated like another API call. But each payment needs per-payment SCA, and network retries without an idempotency key can submit twice. Amount handling and IBAN/BIC validation are underspecified.

**How to avoid:**
- Per-payment **idempotency key**; the initiation endpoint must be safe to retry. Persist a payment-initiation state machine (created → SCA pending → submitted → executed/failed) and never resubmit an already-submitted key.
- Require explicit user confirmation + provider SCA per transfer; surface the exact amount, IBAN, and Verwendungszweck. Validate IBAN (checksum) before submit.
- Reconcile the outgoing payment back against the bank statement to confirm execution; don't assume "API returned 200" = money moved.

**Warning signs:**
Two identical outgoing transfers; a transfer in "submitted" state with no matching bank Umsatz; missing SCA step in the flow.

**Phase to address:** BK3 SEPA-Überweisung (PIS). Ship AIS/reconciliation first; PIS is a separate, higher-risk phase.

---

### Pitfall 11: OCR auto-posting without human review (accuracy + liability)

**What goes wrong:**
OCR reads a Beleg (amount, VAT, date, supplier) and the system **auto-creates a booking** from it. OCR misreads 1.799,00 as 1.799,90 or 7% as 19%, and a wrong VAT booking flows into the USt-VA. Because it's festgeschrieben, fixing it needs a reversal — and the tenant may have already filed.

**Why it happens:**
OCR demos look magical; teams wire "scan → booking" to impress. But OCR is probabilistic and the responsible taxpayer (and their Steuerberater) is liable for the numbers. GoBD requires the booking to be verifiable against the original Beleg.

**How to avoid:**
- OCR **proposes** field values with confidence; a human **reviews and confirms** before any booking is created/festgeschrieben. Never auto-festschreiben from OCR. Low-confidence fields flagged.
- Always link the booking to the **immutable original Beleg** (image/PDF) so it's verifiable. Extraction is a convenience layer; the scanned original is the legal document.
- Learn per-supplier mappings (this supplier → this Aufwandskonto + Steuerschlüssel) to raise confidence over time, but keep confirmation in the loop.

**Warning signs:**
Bookings exist that no human ever confirmed; VAT rate on booking ≠ VAT on the pictured receipt; "how did this posting appear?" support tickets.

**Phase to address:** BE1 Belegscan/OCR (review-before-post is a hard product rule).

---

### Pitfall 12: Beleg originals not stored immutably for GoBD (revisionssicher, 10 years)

**What goes wrong:**
Scanned/received Belege are stored in mutable, deletable object storage with no versioning, no retention lock, no documented process. GoBD requires **revisionssichere** archiving of originals for typically **10 years** (§147 AO), unveränderbar and reproducible. A tenant deletes a bucket, or a file is silently overwritten → the audit trail is gone.

**Why it happens:**
"It's just file upload." The revisionssicher requirement (immutability, retention, index, ability to reproduce the original + metadata + who/when) is unknown to web devs. Also: the *first* received form is the original that must be kept (e.g. a PDF received by email must be archived as received, not re-rendered).

**How to avoid:**
- Store originals **write-once**: object-lock / immutability / retention (WORM-style), content hash on ingest, versioning on, hard-delete disabled for the retention window. Keep the file exactly as received (no re-encoding of the original).
- Maintain an index/metadata record (who uploaded/received, when, hash, source) and link every booking to its Beleg. Extend v1's GoBD Verfahrensdokumentation to cover the new Beleg pipeline.
- Retention: default 10 years; block deletion (including tenant-initiated and DSGVO-erasure) inside retention — GoBD retention overrides DSGVO erasure for tax-relevant docs (document this tension explicitly).

**Warning signs:**
Belege can be deleted/overwritten; no content hash; no retention config; a DSGVO "delete my data" would wipe tax originals.

**Phase to address:** BE3 GoBD-Archiv (design storage immutability before BE1 lets users scan into it).

---

### Pitfall 13: Per-tenant email intake — spoofing, spam, and mis-routing to the wrong tenant

**What goes wrong:**
Each tenant gets an inbound address (e.g. belege-<token>@intake.numera.de). Attackers spoof the sender, flood it with spam/malware attachments, or a guessable address lets one tenant's Belege land in another's archive. A spoofed "invoice" gets auto-archived as a genuine Beleg.

**Why it happens:**
Inbound email is treated as trusted. Addresses are guessable (belege-<firmenname>@), no SPF/DKIM/DMARC checks, attachments processed without scanning, no rate limiting, no cross-tenant isolation on the intake path.

**How to avoid:**
- **Unguessable** per-tenant address (high-entropy token), rotatable. Optionally allow-list sender domains per tenant.
- Verify **SPF/DKIM/DMARC**; quarantine failures for manual review rather than auto-archiving. **Virus/malware-scan** every attachment; size/type limits; rate-limit per address.
- Map intake strictly to one tenant_id and run the whole pipeline under that tenant's RLS context. Everything received is a *proposal* pending human review — never auto-post.
- Store the raw email (headers included) as the received original for GoBD.

**Warning signs:**
Belege appear from unknown senders; attachments not scanned; intake address guessable; any code path where an inbound message could resolve to more than one tenant.

**Phase to address:** BE2 E-Mail-Intake.

---

### Pitfall 14: Stripe webhooks without signature verification or idempotency

**What goes wrong:**
The billing webhook endpoint doesn't verify the Stripe signature (so anyone can POST a fake `invoice.paid` and unlock a paid plan), or processes the same event twice (Stripe retries on any non-2xx or timeout) — double-provisioning, double emails, or flipping entitlements twice.

**Why it happens:**
Webhooks look like a normal POST. Teams skip `Stripe-Signature` verification and assume exactly-once delivery. Events also arrive **out of order**.

**How to avoid:**
- **Verify the webhook signature** against the endpoint's signing secret on every request; reject unverified.
- **Idempotency:** persist processed `event.id`; ignore duplicates. Make handlers idempotent (converge to state, don't increment).
- Don't rely on event ordering — always reconcile against the current subscription object (retrieve/expand) rather than trusting the event payload alone. Return 2xx fast; do work async (fits the existing Hangfire worker).
- Separate **test vs live** keys/secrets/webhook endpoints; never let a test event touch a live tenant.

**Warning signs:**
Endpoint accepts unsigned payloads; no `stripe_event` dedup table; entitlement toggled twice; a test webhook hitting prod.

**Phase to address:** M1 Stripe-Abrechnung.

---

### Pitfall 15: tenant.plan gate drifts from Stripe subscription state (source-of-truth confusion)

**What goes wrong:**
The v1 server-authoritative S/M/L/XL feature gate (`tenant.plan`) is set on checkout but then diverges from Stripe: a payment fails and enters dunning, the customer cancels, a card expires, a downgrade takes effect at period end — and Numera still grants the old plan (or revokes too early). Users get features they didn't pay for, or lose features they did.

**Why it happens:**
`tenant.plan` is set once at checkout and treated as the source of truth. But the real source of truth is the **Stripe subscription** (status: active/trialing/past_due/canceled, current_period_end, cancel_at_period_end, the priced items). Plan changes, proration, trials and dunning all mutate Stripe state asynchronously.

**How to avoid:**
- **Stripe subscription = source of truth; `tenant.plan` = a cached projection** rebuilt from webhook events (`customer.subscription.updated/deleted`, `invoice.payment_failed`, etc.) and reconciled on read via a periodic sync job.
- Map subscription status → entitlement explicitly: `trialing/active` = full plan; `past_due` = grace period then restrict; `canceled` = downgrade at period end (respect `cancel_at_period_end` and `current_period_end`, don't cut mid-period). Handle **downgrade = keep until period end; upgrade = immediate with proration**.
- Define what happens to data above a downgraded plan's limits (read-only, not deleted). Reuse v1's "Upgrade-Hinweis statt Fehler" UX for degraded states.

**Warning signs:**
A canceled/past_due Stripe sub with an active plan in Numera; entitlement changes that no webhook explains; a downgrade cutting access mid-paid-period.

**Phase to address:** M1 Stripe-Abrechnung (entitlement projection + reconciliation job).

---

### Pitfall 16: New v2 tables shipped without RLS (or with weak/mis-scoped RLS)

**What goes wrong:**
The many new tables (chart of accounts, bookings, journal lines, bank connections, Umsätze, Belege, OCR results, email-intake, Stripe subscriptions/events) are created without the per-table hand-written RLS the v1 system relies on — or with RLS that misses a tenant_id, or a background job (Hangfire, finAPI import, Stripe webhook) runs *without* a tenant context and bypasses RLS. One tenant sees another's bank data or Belege — a catastrophic breach in accounting/DSGVO.

**Why it happens:**
v1 proved RLS via a Cross-Tenant test suite, but that suite doesn't cover tables that don't exist yet. Background/system contexts (webhooks, email intake, bank import) legitimately run outside a user request and are exactly where tenant scoping is forgotten.

**How to avoid:**
- Every new table gets tenant_id + hand-written RLS following the v1 pattern, and is **added to the Cross-Tenant test suite** in the same phase it's created (make it a phase success criterion).
- For system contexts (Hangfire jobs, Stripe webhook, email intake, finAPI callbacks): resolve tenant_id from the payload and **explicitly set the RLS tenant context** for the unit of work; never run these with an admin/bypass role by default. Stripe events map to tenant via the stored customer→tenant link.
- Bank data and Belege are especially sensitive — add targeted cross-tenant tests (tenant A cannot read tenant B's Umsätze/Belege/OCR text).

**Warning signs:**
A new table with no policy in the migration; a job connecting with a superuser/bypass role; the cross-tenant suite not growing with the schema.

**Phase to address:** Every v2 phase that adds tables (make "new tables have RLS + cross-tenant test" a standing Definition of Done).

---

### Pitfall 17: Money precision & rounding in the booking/tax engine

**What goes wrong:**
Amounts hit float somewhere (an OCR parse, a finAPI amount as double, a Stripe amount, a report aggregation), or per-line rounding accumulates, and the books are off by cents that never balance. In double-entry a single cent means Soll≠Haben.

**Why it happens:**
External inputs arrive in different shapes: **Stripe uses integer minor units (cents)**, finAPI returns decimal strings, OCR yields free text. Careless conversion introduces float; aggregation across thousands of lines compounds it.

**How to avoid:**
- Keep the v1 `decimal` money type end-to-end; parse all external amounts into decimal at the boundary (Stripe cents → decimal explicitly; never through double). Reuse v1's EN-16931 rounding rule and Golden-file discipline for the booking/VAT engine.
- One documented rounding policy; place remainders on a defined line/account; test split bookings and VAT rounding to the cent.
- Store currency per amount; the booking engine is EUR but bank/Stripe/foreign-currency inputs must be converted explicitly (v1 already handles Fremdwährung invoices — reuse).

**Warning signs:**
Any `double`/`float` on a money path; report totals that differ by cents from the journal; VAT that doesn't tie to net×rate.

**Phase to address:** B1 Buchhaltungs-Fundament; enforce at each external boundary (BK1, BE1, M1).

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Booking as flat 2-account row (no split lines) | Faster first CRUD | Can't do split VAT / multi-open-item payments; full engine rewrite | Never — model header+lines from day 1 |
| Festschreibung as a boolean, no period lock | Ship reports sooner | Late bookings corrupt filed USt-VA; GoBD gap | Only pre-first-real-tenant; add period lock before B3 ships |
| Only SKR03 (or only SKR04) at launch | Half the seed data | Migrating a tenant's chart later is painful; loses market segment | Acceptable to *sequence* (one first) only if the model supports both from the start |
| OCR auto-post to look smart | Demo wow | Wrong VAT in filings, liability, reversals | Never |
| Belege in plain object storage without object-lock | Quick storage | Non-revisionssicher → GoBD fails; can't retrofit past docs | Never for originals |
| `tenant.plan` set at checkout, no webhook sync | Billing "works" in demo | Entitlement drift on dunning/cancel/downgrade | Never for live billing; ok in Stripe *test* only |
| Import bank tx keyed on (date,amount,text) | No provider-id plumbing | Duplicate/merged Umsätze corrupt books | Never — use provider id + idempotency key |
| New table without RLS "we'll add it later" | Faster migration | Cross-tenant data leak (bank/Belege = worst case) | Never |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| finAPI (AIS) | Treat connect as one-time; only test sandbox mock banks | Track/renew PSD2 consent (SCA renewal ~180d post-2022 RTS, bank-dependent); test a real bank in staging; operate under finAPI's TPP licence |
| finAPI (PIS/SEPA) | Fire-and-forget transfer, no idempotency/SCA | Per-payment idempotency key + SCA + state machine + reconcile execution against statement |
| finAPI import | Assume exactly-once, key on natural fields | Provider transaction id + idempotency upsert + per-connection cursor |
| Stripe webhooks | No signature check / no dedup / trust ordering | Verify signature, dedup on event.id, reconcile against live subscription object, async via Hangfire |
| Stripe amounts | Read cents as float / mix test+live keys | Parse minor-units → decimal explicitly; strict test/live key + webhook separation |
| Stripe as billing SoT | `tenant.plan` set once at checkout | Subscription is SoT; plan = projection rebuilt from events + periodic reconcile |
| Cloud OCR provider | Send Belege to a non-EU OCR without AVV/DPA | EU-region OCR (or self-host) with AVV/DPA; DSGVO Art. 28 processor contract; data-flow documented |
| Inbound email (intake) | Trust sender, no SPF/DKIM/DMARC, guessable address | Verify SPF/DKIM/DMARC, unguessable token address, virus-scan, per-tenant RLS routing, human review |
| ELSTER/USt-VA | Invent own export format / hardcode one year's Kennziffern | Use the official UStVA datamodel; version Kennziffern by year; calc+export only (ERiC out of scope) |
| DATEV expectation | Ignore Automatikkonten & Steuerschlüssel semantics | Model Automatikkonten + Steuerschlüssel so Steuerberater/DATEV-literate users trust the output |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Recompute GuV/BWA/USt-VA by scanning all journal lines every view | Reports slow as history grows | Pre-aggregate per period/account (Salden), incremental update on Festschreibung | Tenants with multi-year history / many bookings |
| Reconciliation compares every Umsatz against every open item (N×M) | Sync gets slow, UI stalls | Index by amount/date/invoice-number; candidate-filter before scoring | Tenants with thousands of open items/Umsätze |
| OCR done synchronously in the request | Upload times out on large PDFs | Offload to Hangfire worker; async status; thumbnails | Multi-page PDFs / bulk email intake bursts |
| Storing full Beleg images in Postgres | DB bloat, slow backups | Object storage (with object-lock) + metadata in DB | Once Belege volume grows |
| Per-tenant bank sync all at once on a timer | Thundering herd, finAPI rate limits | Stagger/queue syncs, respect rate limits, backoff | Many connected tenants |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| New v2 tables without RLS or jobs bypassing tenant context | Cross-tenant leak of bank data/Belege — worst-case breach | Per-table RLS + explicit tenant context in every job/webhook/intake; grow cross-tenant test suite |
| Storing raw bank credentials/tokens insecurely | PSD2/DSGVO breach, financial theft | Never store bank login; rely on finAPI OAuth/consent tokens; encrypt at rest; least-privilege |
| Unsigned Stripe/finAPI webhooks | Forged events unlock plans / inject data | Verify signatures; allow-list source; reject unverified |
| Guessable email-intake address, no attachment scan | Spoofed/malicious Belege injected | High-entropy address, SPF/DKIM/DMARC, malware scan, human review |
| Sending Belege/bank data to non-EU processors without AVV | DSGVO Art. 28/44 violation, fines | EU-region processors with DPA/AVV; document sub-processors; TOMs |
| GoBD retention vs DSGVO erasure conflict handled ad-hoc | Either lose tax originals or fail erasure requests | Retention lock overrides erasure for tax-relevant docs; document the rule; erase only non-tax PII |
| PII in logs (bank tx text, OCR content, emails) | Leak via logs | Redact financial/PII fields from logs |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Auto-posting bank matches / OCR without confirmation | Users lose trust after one wrong booking; hidden errors | Propose → confirm; show confidence; make reversal easy |
| No re-consent UX when PSD2 consent expires | Silent stale data, wrong reconciliation | Proactive expiry warning + one-click re-consent; connection health widget |
| Hard errors on locked periods / plan limits | Confusion, support load | Reuse v1 "Upgrade-Hinweis statt Fehler"; explain "Periode gesperrt, buchen in Folgeperiode?" |
| Exposing raw Kennziffern/Steuerschlüssel to non-accountants | Freelancers overwhelmed | Smart defaults per account; hide advanced tax keys behind expert mode; richer Steuerberater view |
| Downgrade deletes data over the new limit | Data loss, churn | Read-only above limit, never delete; clear restore-on-upgrade path |
| EÜR vs GuV/Bilanz shown regardless of legal form | Users file the wrong report | Gate reports on Gewinnermittlungsart/Rechtsform |

## "Looks Done But Isn't" Checklist

- [ ] **Booking engine:** balances to the cent on split VAT / multi-open-item payments — verify with golden-file tests, not just the happy path
- [ ] **Festschreibung:** posted bookings truly cannot be UPDATE/DELETE'd at the DB level (not just UI-hidden) — verify with a direct SQL attempt in a test
- [ ] **Period lock:** a booking dated into a submitted USt-VA period is rejected/redirected — verify
- [ ] **USt-VA:** correct for §19 Kleinunternehmer, §13b reverse-charge, i.g., AND for an Ist-Versteuerung tenant (period follows payment) — verify each
- [ ] **Bank import:** re-running the same window creates zero duplicates — verify idempotency
- [ ] **Reconciliation:** un-matching restores the open item exactly; partial/Skonto handled — verify
- [ ] **PSD2 consent:** expiry produces a re-consent prompt, not silent failure — verify
- [ ] **OCR:** no booking exists that a human didn't confirm; booking links to the immutable original — verify
- [ ] **Beleg archive:** originals are genuinely write-once / retention-locked for 10y — verify deletion is blocked
- [ ] **Email intake:** SPF/DKIM/DMARC-failing and malware attachments are quarantined, not archived — verify
- [ ] **Stripe:** unsigned webhook rejected; duplicate event.id ignored; past_due/canceled/downgrade reflected in `tenant.plan` — verify each transition
- [ ] **RLS:** every new v2 table is in the cross-tenant test suite; every job/webhook sets tenant context — verify the suite grew with the schema
- [ ] **Money:** no float on any money path (Stripe cents, finAPI, OCR) — verify at each boundary

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Unbalanced bookings in production | HIGH | Freeze; reconcile journal vs Salden; reversal bookings for bad entries; add the missing balance invariant; re-run golden files |
| Editing posted bookings happened (GoBD breach) | HIGH | Reconstruct history from audit log; switch to reversal-only; document remediation in Verfahrensdokumentation; likely tax-advisor consultation |
| Wrong USt-VA already filed | MEDIUM | File berichtigte Voranmeldung for affected periods; add period lock to prevent recurrence |
| Duplicate bank transactions | MEDIUM | Dedup by provider id; reverse duplicate-derived bookings; add idempotency key |
| OCR auto-posted wrong VAT | MEDIUM | Reversal bookings; switch to review-before-post; per-supplier learning |
| Beleg stored non-revisionssicher | HIGH | Migrate existing originals into WORM storage with hashes where still possible; document the gap; enforce object-lock going forward |
| tenant.plan drifted from Stripe | LOW | Run the reconciliation job to rebuild the projection from Stripe; add webhook handlers for missed events |
| Cross-tenant leak via missing RLS | HIGH | Incident response + DSGVO breach assessment/notification; add RLS + tests; audit access logs for actual exposure |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| 1 Unbalanced postings | B1 Buchhaltungs-Fundament | Golden-file balance tests incl. split VAT/multi-OP |
| 2 Editing posted bookings | B1 | Direct-SQL update on a festgeschrieben row is blocked |
| 3 Period locks | B1 + B3 | Booking into a submitted USt-VA period rejected |
| 4 SKR/Steuerschlüssel mapping | B1 | Automatikkonto double-tax rejected; both SKR seeded |
| 5 USt-VA Kennziffern / Ist-Soll | B3 (setting in B1) | Worked-example tests incl. §13b/§19/Ist tenant |
| 6 EÜR/GuV/Bilanz | B3 | Report availability gated on Gewinnermittlungsart |
| 7 Import idempotency | BK1 | Re-run window = 0 duplicates |
| 8 Reconciliation mis-match | BK2 | Partial/Skonto/un-match tests |
| 9 PSD2 consent / sandbox | BK1 | Re-consent flow + real-bank staging test |
| 10 SEPA/PIS safety | BK3 | Idempotency + SCA + execution-reconcile tests |
| 11 OCR auto-post | BE1 | No unconfirmed booking; booking↔original link |
| 12 Beleg immutability | BE3 | Deletion blocked within retention; hash on ingest |
| 13 Email intake abuse | BE2 | SPF/DKIM/DMARC + malware quarantine tests |
| 14 Stripe webhook safety | M1 | Unsigned rejected; duplicate event.id ignored |
| 15 Plan/Stripe drift | M1 | Each sub status transition reflected in entitlement |
| 16 RLS on new tables | Every table-adding phase | Cross-tenant suite covers all new tables + jobs |
| 17 Money precision | B1 + all boundaries | No float on money paths; boundary parse tests |

## Sources

- German tax/accounting law & GoBD: §146/§147 AO (Aufbewahrung, Unveränderbarkeit, 10 Jahre), §14 UStG, §20 UStG (Ist-Versteuerung), §4(3) EStG (EÜR), §141 AO (Bilanzierungsschwellen), §13b UStG (reverse charge); BMF GoBD-Schreiben (Unveränderbarkeit, Festschreibung, Verfahrensdokumentation) — HIGH (established law)
- SKR03/SKR04 & Steuerschlüssel/Automatikkonten (DATEV conventions), USt-VA Kennziffern (ELSTER UStVA form) — HIGH (established, year-versioned; re-verify current form during B3)
- PSD2 SCA renewal 90→180 days: EBA final report amending the RTS on SCA&CSC (2022) — verified via web search: [EBA press release](https://www.eba.europa.eu/publications-and-media/press-releases/eba-publishes-final-report-amendment-its-technical-standards), [PayTechLaw RTS overview](https://paytechlaw.com/en/regulatory-technical-standards-rts-sca-csc/), [LUXHUB summary](https://luxhub.com/strong-customer-authentication-eba-shares-its-latest-proposed-amendment-of-the-psd2-rts/) — MEDIUM (banks implement variably; confirm per finAPI/bank in BK1)
- finAPI (SCHUFA) AIS/PIS, XS2A sandbox vs prod, TPP licensing model — MEDIUM (verify current finAPI docs/contract in BK1)
- Stripe billing: webhook signature verification, event idempotency, subscription as source of truth, proration on plan change, minor-unit amounts, test/live separation — HIGH (long-standing Stripe guidance; confirm current API version in M1)
- DSGVO: Art. 28 (Auftragsverarbeitung/AVV), Art. 44+ (Drittlandtransfer for cloud OCR), retention-vs-erasure tension — HIGH
- Existing Numera v1: PROJECT.md, prior .planning/research (RLS proven via Cross-Tenant suite, decimal money + EN-16931 rounding, GoBD-Unveränderbarkeit on Belegkette, race-safe numbering, Hangfire worker, server-authoritative Tarif-Gates) — HIGH (in-repo)

---
*Pitfalls research for: German accounting SaaS v2.0 (Buchhaltung, Banking, Belege/OCR, Monetarisierung)*
*Researched: 2026-08-02*
