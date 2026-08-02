# Stack Research — v2.0 (Buchhaltung, Banking, Belege, Monetarisierung)

**Domain:** German accounting/invoicing SaaS — v2.0 additions on an existing .NET 10 + Postgres 18 + React 19 modular monolith
**Researched:** 2026-08-02
**Confidence:** HIGH for Stripe, ELSTER/ERiC facts, finAPI role, TSE/Kassenbuch scope; MEDIUM for exact OCR SDK patch and DATEV EXTF format-version; MEDIUM for OCR-provider choice (a business/DPA decision).

> **Subsequent-milestone note.** The v1.0 stack (.NET 10 modular monolith, Postgres 18 + hand-written RLS, EF Core 10, Keycloak-BFF, Hangfire, QuestPDF, ZUGFeRD-csharp 18 + KoSIT, MailKit/Mailpit, NodaMoney, React 19 PWA / Tailwind v4 / TanStack Query) is **validated and reused** — see the previous STACK.md in git history for its rationale. Everything below is **net-new for v2.0** or an explicit "do not add". Nothing here replaces a v1 choice.

---

## Executive recommendation (the short version)

- **Buchhaltung** needs **no new runtime library** — it is a domain-modelling + SQL problem. Extend the existing `Ledger` module with an append-only double-entry journal, seed SKR03/SKR04 yourself, and render EÜR/GuV/BWA with the QuestPDF you already ship. USt-VA = aggregation → ELSTER **XML file for manual upload** (ERiC direct submission stays OUT of scope by decision).
- **Banking** = one licensed provider: **finAPI** (BaFin-licensed, Berlin-Group XS2A, RegShield "PSD2-licence-as-a-service" so **Numera does not need its own PSD2 licence**). No official .NET SDK exists — generate a typed C# client from finAPI's OpenAPI spec. Do **not** build on GoCardless Bank Account Data (Nordigen) — it is **closed to new signups**. Do **not** hand-roll FinTS/HBCI.
- **Belege/OCR** = two tiers. Incoming structured e-invoices (ZUGFeRD/XRechnung) are parsed by the **ZUGFeRD-csharp you already have — no OCR at all**. For photographed/scanned receipts use **Azure AI Document Intelligence** (prebuilt-receipt / prebuilt-invoice) pinned to an **EU region (Germany West Central / Sweden Central)** behind an interface, with a self-hosted PaddleOCR/docTR escape hatch. E-mail intake = **your own catch-all mailbox polled by MailKit in the Worker** (MailKit already in the stack), not a US inbound-parse SaaS.
- **Monetarisierung** = **Stripe Billing** with the **Stripe.net 52.x** SDK. Webhooks (Hangfire-processed, idempotent) drive `tenants.plan`. Use Stripe Checkout + Customer Portal (hosted) — do not build card forms.
- **Biggest scope trap to avoid:** turning the *elektronisches Kassenbuch* into a **Registrierkasse/POS with a certified TSE (§146a AO)**. A cash *journal* needs GoBD immutability, **not** a TSE. A KassenSichV/TSE POS is a separate, certification-heavy product.

---

## Recommended Stack

### Core Technologies (net-new for v2.0)

| Technology | Version | Purpose | Why recommended for THIS stack |
|------------|---------|---------|--------------------------------|
| **Stripe.net** | **52.2.0** (stable, 2026-07-29) | Stripe Billing / Checkout / Portal / Webhooks SDK | Official Stripe .NET client; targets .NET Standard 2.0 → runs on .NET 10. Maps 1:1 to Billing objects (Subscriptions, Prices, Checkout Sessions, Billing Portal). Webhook signature verification built in. Directly feeds the existing server-authoritative `tenants.plan` feature-gate from v1. |
| **finAPI Access (XS2A) + Web Form 2.0 + RegShield** | current SaaS (contract) | Multibanking: account/transaction retrieval (AIS) + SEPA credit-transfer initiation (PIS) | BaFin-licensed, **100% Berlin-Group XS2A**; best German-bank coverage; **RegShield = PSD2-licence-as-a-service** so Numera avoids becoming a licensed AISP/PISP itself. Web Form 2.0 renders the SCA/consent flow (white-labelable). Pre-noted candidate — research confirms it is still the right one for a DE-focused product. |
| **Generated finAPI C# client** | from finAPI OpenAPI spec (docs.finapi.io) via **NSwag** or **openapi-generator 7.x** | Typed access to finAPI REST from `Numera.Api`/`Worker` | finAPI ships **no official .NET SDK** (empty GitHub org). Generate a client at build time, wrap it behind a thin `Banking` module port. Keeps the integration versioned and testable. |
| **Azure AI Document Intelligence** (SDK **Azure.AI.DocumentIntelligence 1.x**) | API `2024-11-30` GA / model doc-intel **4.0** | Receipt/invoice OCR + field extraction for photographed/uploaded Belege | Highest-accuracy managed prebuilt **receipt** and **invoice** models incl. German receipts; **deployable in EU regions** (Germany West Central / Sweden Central), input deleted ≤24 h, DPA available, **disconnected-container** option for full on-prem residency. Wrap behind an `IReceiptExtractor` port so the provider is swappable. |
| **MailKit** (already in stack) | 4.x | E-mail Beleg-intake: IMAP-poll a per-tenant catch-all mailbox in the Worker | Already used for outbound; reuse for **inbound** by polling IMAP on `inbox.numera.de` and routing on the plus-/sub-addressed recipient (`{tenant}.{token}@inbox.numera.de`). Keeps mail data on infra you control (DSGVO) instead of a US inbound-parse SaaS. |
| **Hangfire** (already in stack) | Postgres-backed | Background jobs: nightly transaction sync, OCR extraction, mailbox polling, Stripe-webhook post-processing, XML/EXTF generation | Reuse the existing Worker tier. All four domains are I/O-bound and async — no new job runner needed. |
| **QuestPDF** (already in stack) | current | Render EÜR / GuV / BWA / USt-VA-Vorschau / Kassenbuch PDFs | Already the PDF engine for invoices; reports are the same code path. **No charting/PDF lib to add.** |

### Supporting Libraries / Assets

| Library / Asset | Version | Purpose | When to use |
|-----------------|---------|---------|-------------|
| **SKR03 / SKR04 seed dataset (self-curated)** | fiscal year 2026 | Chart-of-accounts master data (account no., name, category, BU-/Steuerschlüssel, EÜR/GuV mapping) | DATEV publishes SKRs only as free **PDF + Excel Kontenfunktionen**, no official CSV/API. Curate your own seed table (checked-in, versioned per fiscal year) rather than depending on any package. |
| **Custom EXTF (DATEV-Format) writer** | Buchungsstapel format-version **700** (verify on developer.datev.de) | Steuerberater export = DATEV "Buchungsstapel" CSV | "Erweitertes Transferformat": semicolon-separated, **CP1252/Windows-1252** encoded, 2 header rows + booking rows. No maintained official .NET package — implement a small serializer; `ledermann/datev` (Ruby) is a good field-reference. |
| **Custom ELSTER UStVA XML generator** | schema namespace `http://finkonsens.de/elster/elsteranmeldung/ustva/vYYYY`, charset **ISO-8859-15** | USt-Voranmeldung export for **manual upload to Mein ELSTER** | Aggregate ledger by tax key → Kennziffern → emit XML validated against the ERiC **Datensatzbeschreibung/XSD** (from the ERiC release package). This is the sanctioned path while ERiC direct submission is out of scope. |
| **NodaMoney** (already in stack) | current | Money type for journal lines / balances | Reuse — decimal + EN-16931 rounding already proven in v1. Do not introduce a second money type. |
| **Self-hosted OCR fallback: PaddleOCR or docTR (Dockerized)** | current | On-prem OCR if Azure DI is rejected on data-residency grounds | Only if a customer/DPA forbids cloud OCR entirely. Runs as a sidecar container behind the same `IReceiptExtractor` port. Higher engineering + accuracy cost — keep as escape hatch, not default. |

### Development / Infra Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| **Stripe CLI** | Local webhook forwarding + fixture events | `stripe listen --forward-to` against the BFF during dev; replaces guessing webhook payloads. |
| **finAPI Sandbox** | Test AIS/PIS with mock banks | Provisioned by finAPI; test the consent/SCA + transaction flow without real bank credentials. |
| **NSwag / openapi-generator** | Generate the finAPI C# client in CI | Pin the spec version; regenerate on finAPI API bumps. |
| **ERiC release package (Doku only)** | Source of UStVA XSD + Plausibilitätsprüfung docs | Download the ERiC-*-Dokumentation.zip for the schemas even though you don't ship the ERiC binary yet. |
| **Mailpit** (already in stack) | Local inbound/outbound mail testing | Reuse for e-mail-intake dev. |

## Installation

```bash
# --- Backend (.NET 10) ---
dotnet add Numera.Monetization package Stripe.net                    # 52.2.0
dotnet add Numera.Banking     package <generated finAPI client>      # NSwag/openapi-generator output
dotnet add Numera.Belege      package Azure.AI.DocumentIntelligence  # 1.x
# MailKit, Hangfire, QuestPDF, NodaMoney already referenced — no new adds

# --- Client generation (build step, not a package) ---
npx @openapitools/openapi-generator-cli generate \
  -i https://docs.finapi.io/spec/access.json -g csharp -o ./gen/FinApiClient
# (or NSwag equivalent)

# --- Frontend (React 19) ---
# Stripe uses HOSTED Checkout + Customer Portal → NO @stripe/stripe-js card elements needed.
# Only add @stripe/stripe-js if you later embed Elements; for v2.0 redirect to hosted pages.
```

## Alternatives Considered

| Recommended | Alternative | When the alternative would win |
|-------------|-------------|--------------------------------|
| **finAPI** (banking) | **Enable Banking** | Cheaper/self-serve for a pan-EU indie build; weaker for a DE-focused product needing RegShield-style licence coverage + German support. |
| **finAPI** | **Tink (Visa)** / **TrueLayer** | If/when Numera expands beyond DE across EU+UK with high volume; production is sales-gated. |
| **finAPI** | **GoCardless Bank Account Data (Nordigen)** | **Never for a new build — closed to new signups / being wound down.** |
| **finAPI PIS** for SEPA | **libfintx (FinTS/HBCI)** | Only as a cost-cutting fallback for AIS on banks finAPI misses; FinTS coverage + maintenance burden make it unsuitable as the primary rail. |
| **Azure AI Document Intelligence** | **Klippa/Doxis, Rossum, Taggun** | Klippa/Doxis (EU, ISO 27001) or Rossum (EU, strongest pure-invoice AI) if you want a receipt-specialist vendor; Taggun (GDPR-ready, no-storage) for a lighter receipt-only API. All are viable EU-compliant swaps behind the port. |
| **Azure DI (cloud, EU region)** | **Self-hosted PaddleOCR/docTR** | Hard data-residency/air-gap requirement, or extreme volume where per-page cloud cost dominates. |
| **Own mailbox + MailKit** (intake) | **Postmark Inbound / Mailgun Routes / SES Inbound** | If you don't want to run a catch-all mailbox; but these route mail through a third party (DSGVO DPA needed) — SES-inbound→S3 (EU region) is the most residency-friendly of these. |
| **Self-generated ELSTER XML** | **ERiC native library (ericapi.dll/.so) direct submission** | Explicitly a **later milestone** — needs a Hersteller-ID registration + shipping/operating the native C library. Out of scope now by decision. |
| **Stripe Billing** | **Paddle / Lemon Squeezy (Merchant of Record)** | If Numera wanted a MoR to offload EU-VAT/OSS on subscriptions entirely; Stripe was pre-decided and gives finer control + the existing plan-gate fit. |

## What NOT to Use / Build

| Avoid | Specific problem | Do instead |
|-------|------------------|------------|
| **A Registrierkasse / POS with certified TSE (KassenSichV §146a AO)** | Certification-heavy, €25k fines if wrong, needs cert-TSE hardware/cloud + Finanzamt registration — an entirely different product. The v2.0 "elektronisches Kassenbuch" is a **cash journal**, which needs **no TSE**. | Build a GoBD-compliant **cash book**: append-only entries, running balance, immutable change-log, no post-close edits. Explicitly scope out POS/TSE. |
| **GoCardless Bank Account Data (Nordigen)** | Closed to new signups; being wound down. | finAPI (above). |
| **Hand-rolled FinTS/HBCI or EBICS as the primary banking rail** | Per-bank quirks, TAN/SCA handling, ongoing breakage; regulatory exposure. | finAPI Access (AIS) + finAPI PIS for SEPA. |
| **Generating your own SEPA `pain.001` XML for transfers** | finAPI PIS already initiates the transfer end-to-end incl. SCA. | Call finAPI PIS; skip SEPA-XML libraries. |
| **Storing raw card data / building card input forms** | PCI scope explosion. | Stripe **hosted Checkout** + **Customer Portal**; only tokens/IDs touch Numera. |
| **Trusting the client for plan changes** | Same reason v1 gates are server-authoritative. | Stripe **webhooks** are the single source of truth → update `tenants.plan` server-side; verify webhook signatures; make handlers **idempotent** (dedupe by event id). |
| **A US inbound-email SaaS as default for Belege** | Personal/financial documents routed through a US processor → DSGVO transfer risk. | Own catch-all mailbox + MailKit polling on EU infra; or SES-inbound in an EU region if managed intake is required. |
| **Cloud OCR in a US/default region** | Sends receipts (personal data) outside the EU. | Pin Azure DI to Germany West Central / Sweden Central; sign the DPA; document 24 h deletion. |
| **A charting/reporting NuGet or JS lib** | Not needed. | QuestPDF (PDF) + existing custom inline-SVG charts (React) already cover EÜR/GuV/BWA visuals. |
| **UPDATE/DELETE on journal rows** | Breaks GoBD Unveränderbarkeit. | Corrections via **reversing entries (Stornobuchung)**; enforce append-only at the DB (no UPDATE/DELETE grant for `numera_app` on ledger tables, plus period-lock). |

## Double-entry in Postgres (approach, not a package)

An architecture decision inside `Ledger`, using the existing v1 patterns — no library:

- **Tables:** `journal_entries` (header: date, period, doc-ref, tenant_id) + `journal_lines` (account, debit, credit, tax key, amount as `decimal`). Reuse **NodaMoney** and EN-16931 rounding.
- **Balanced invariant:** enforce `SUM(debit) = SUM(credit)` per entry via a deferred constraint trigger.
- **Immutability (GoBD):** append-only; **revoke UPDATE/DELETE** from `numera_app` on ledger tables (mirrors v1's belegkette approach); corrections are reversing entries; **period locking** after USt-VA/EÜR is filed.
- **Isolation:** per-table **RLS** exactly as the 11 phase-6→8 tables already do — reuse the proven cross-tenant test harness.
- **Balances/reports:** aggregate on read (indexed views / on-the-fly sums); EÜR/GuV/BWA and USt-VA Kennziffern are queries mapping accounts + tax keys → report lines.

## Stack Patterns by Variant

**If a tenant is EÜR (Einnahmen-Überschuss-Rechnung — most freelancers/Kleinunternehmer):**
- Cash-basis; SKR03/04 still used but only P&L accounts matter → EÜR report + USt-VA. No Bilanz. Simpler double-entry path.

**If a tenant is bilanzierend (GmbH/UG — doppelte Buchführung):**
- Full journal, GuV + Bilanz, accrual. Same tables, more account classes and period-close rigour.

**If a customer forbids cloud OCR (DPA):**
- Flip the `IReceiptExtractor` implementation to the self-hosted PaddleOCR/docTR container; everything else unchanged.

**If finAPI misses a bank:**
- Manual **CSV/MT940/CAMT.053 import** as a fallback ingestion path into the same reconciliation engine (also useful offline). Do not add FinTS just for coverage.

## Version Compatibility

| Package A | Compatible with | Notes |
|-----------|-----------------|-------|
| Stripe.net 52.2.0 | .NET 10 | Targets netstandard2.0/net6 → runs fine on net10. Pin the **Stripe API version** in the account, not just the SDK. |
| Azure.AI.DocumentIntelligence 1.x | .NET 10 | Use API `2024-11-30` (doc-intel 4.0) models `prebuilt-receipt` / `prebuilt-invoice`. Verify exact NuGet patch at install. |
| Generated finAPI client | .NET 10 / EF-independent | Pure HTTP client; regenerate when finAPI bumps the spec. |
| MailKit 4.x | .NET 10 | Already in solution; inbound reuses the same dependency. |
| ERiC XSD (UStVA) | year-versioned | Schema namespace changes yearly (`.../ustva/v2026`); regenerate the XML mapping each fiscal year. |
| DATEV EXTF format-version 700 | — | Confirm the current header format-version on developer.datev.de before shipping the writer. |

## Licensing / Compliance flags (action items for planning)

- **finAPI** — commercial contract required (from ~€250/mo tier); **RegShield/Web Form 2.0** so Numera need **not** hold its own PSD2 AISP/PISP licence. PSD2 SCA consent + 90-day re-consent constraints must be modelled. *Needs vendor onboarding.*
- **Stripe** — Stripe account + business verification; EU-VAT on subscriptions (Stripe Tax optional). *Needs account setup.*
- **Azure DI** — Azure subscription; region pinned to EU; **sign Microsoft DPA**; document 24 h input deletion in the Verfahrensdokumentation.
- **ELSTER/ERiC** — **Hersteller-ID registration** is required only for *direct* ERiC submission (later milestone). The **XML-upload path needs no registration**.
- **DATEV** — EXTF export needs no DATEV licence; SKR account structures are widely replicated — ship your own curated seed, cite DATEV as the standard, avoid copying DATEV's copyrighted PDF text verbatim.
- **GoBD/DSGVO** — ledger + Kassenbuch + Beleg-archive must be revisionssicher (append-only, change-log, 10-year retention); OCR/e-mail-intake must keep personal data in the EU; extend the existing v1 Verfahrensdokumentation.

## Open Questions for Phase-Level Research

1. **finAPI production pricing + PIS onboarding** — get a concrete quote and confirm PIS (SEPA-transfer) tier + white-label eligibility; deciding factor and gating dependency.
2. **DATEV EXTF format-version** — confirm the current Buchungsstapel format-version (700?) and full column order against developer.datev.de before shipping the writer.
3. **UStVA XSD per fiscal year** — pull the exact ERiC Datensatzbeschreibung for 2026 and map every Kennziffer.
4. **Azure DI EU pricing + accuracy on German receipts** — spike prebuilt-receipt vs prebuilt-invoice on real German Belege; validate field coverage (USt, Steuernummer).
5. **GoBD Beleg-archive storage** — WORM/object-lock vs append-only + hash-chaining for the 10-year immutable archive.
6. **Stripe ↔ plan mapping** — map S/M/L/XL to Stripe Prices; define proration on upgrade/downgrade and trial-to-paid transition semantics.

## Sources

- nuget.org/packages/Stripe.net — **Stripe.net 52.2.0 stable, 2026-07-29** (HIGH)
- finapi.io / documentation.finapi.io — XS2A Berlin-Group compliance, RegShield/Web Form 2.0, Licensed-vs-Unlicensed, pricing from ~€250/mo (HIGH for capabilities/role); GitHub org empty → no official .NET SDK (HIGH)
- openbankingtracker.com / dev.to open-banking comparison 2026 — **GoCardless Bank Account Data (Nordigen) closed to new signups**; Enable Banking/Tink/TrueLayer alternatives (MEDIUM, multi-source)
- elster.de developer portal + ELSTER Anwenderforum — UStVA XML upload, namespace `.../ustva/vYYYY`, ISO-8859-15, ERiC Datensatzbeschreibung/XSD in the ERiC release; ERiC ~v43.x native lib + Hersteller-ID for direct submission (HIGH for facts; direct submission out of scope by decision)
- developer.datev.de / help-center.apps.datev.de — DATEV-Format "Buchungsstapel", EXTF = Erweitertes Transferformat, CSV header structure (HIGH for format existence; MEDIUM for exact format-version 700 → verify)
- datev.de Standard-Kontenrahmen / help-center 1048158 — SKR03/04 published as free PDF + Excel Kontenfunktionen (fiscal 2026), no official CSV/API (HIGH)
- learn.microsoft.com Document Intelligence 4.0 + Azure Q&A — prebuilt receipt/invoice, EU regions Germany West Central / Sweden Central, ≤24 h deletion, disconnected containers (HIGH)
- klippa.com / taggun.io / parseur.com / invoicedataextraction.com — EU OCR vendor + self-hosted (PaddleOCR/docTR/Tesseract) comparison (MEDIUM)
- pingram/smtp2go/robotomail inbound-email comparisons 2026 — Postmark/Mailgun/SES inbound + self-hosted options (MEDIUM)
- steuerschroeder.de / kassenprofis-nord.de / tillhub / etron GoBD 2026 — TSE §146a applies to Registrierkassen/Kassensysteme; Kassenbuch (cash journal) needs GoBD immutability, not a TSE; fines to €25k (HIGH, multi-source)

---
*Stack research for: German accounting/invoicing SaaS — v2.0 (Buchhaltung, Banking, Belege, Monetarisierung)*
*Researched: 2026-08-02*
