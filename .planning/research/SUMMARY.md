# Project Research Summary

**Project:** Numera
**Domain:** Multi-tenant SaaS accounting & e-invoicing platform for the German market (Lexware Office / sevDesk competitor), delivered as a responsive PWA
**Researched:** 2026-07-09
**Confidence:** HIGH (regulatory/e-invoicing/architecture verified against official sources; MEDIUM on bank-API pricing and UI specifics)

## Executive Summary

Numera enters a market where the product IS trust: legally regulated software whose correctness (EN 16931 arithmetic, GoBD immutability, tenant isolation) is a liability question, not a quality preference. The research converges on a clear strategy: **v1 = invoicing + e-invoicing done correctly and compliantly**, built on a data model that lets bookkeeping, banking, and DATEV layer on later without rework. The E-Rechnung mandate is the strategic wedge — receiving is mandatory since 01.01.2025, issuing becomes mandatory for most B2B in 2027/2028, and incumbents (Lexware) gate full e-invoicing behind their most expensive tier.

The recommended build is a **modular monolith** (.NET 10 LTS + ASP.NET Core) with a separate worker tier, a single **PostgreSQL 18 database with Row-Level Security** for tenant isolation, a **React 19 + Vite PWA** frontend, and the domain-specific e-invoicing layer built on **ZUGFeRD-csharp** (generate + parse) validated by the official **KoSIT validator** running as a Java sidecar. Microservices for the financial core is an explicit anti-pattern: invoice finalization must atomically assign the number, freeze the document, create the open item, and write the audit trail in one transaction.

The main risks are all "retrofit-impossible" foundations: money as decimal (never float) with EN 16931 rounding rules, GoBD immutability (Draft→Finalized state machine, Storno instead of edit, DB-level enforcement), per-tenant race-safe numbering, RLS on every tenant table, and VAT modeled as EN 16931 category codes (S/AE/K/E/Z) rather than a bare percentage. All of these must land in the first phases; each is cheap on day one and a full-schema migration later.

## Key Findings

### Recommended Stack

The e-invoicing library ecosystem decides the backend language: .NET and Java have mature EN 16931/XRechnung/ZUGFeRD libraries; Node.js does not. .NET 10 (LTS, supported to Nov 2028) is recommended over Java for solo/small-team DX. Full details in `STACK.md`.

**Core technologies:**
- **.NET 10 LTS + ASP.NET Core + C# 14**: backend — best e-invoicing ecosystem, `decimal` primitive for money
- **ZUGFeRD-csharp 18.x**: generate AND parse ZUGFeRD 2.3 / Factur-X / XRechnung (CII); **KoSIT Validator 1.6.0** (Java sidecar in Docker) as the authoritative EN 16931/XRechnung validator for every inbound and outbound invoice
- **QuestPDF 10.x**: invoice PDFs + PDF/A-3 with embedded ZUGFeRD XML (free < $1M revenue; verify PDF/A-3b conformance in an early spike)
- **PostgreSQL 18 + EF Core 10/Npgsql**: shared schema, `tenant_id` + **RLS** (`SET LOCAL app.current_tenant` per request; composite indexes with leading tenant_id)
- **React 19 + Vite + vite-plugin-pwa, shadcn/ui + TanStack Query/Table, react-i18next**: PWA frontend, DE+EN from day one
- **Keycloak 26.x** (self-hosted): multi-tenant auth, future Steuerberater cross-tenant role, DSGVO sovereignty
- **finAPI** (BaFin-licensed, FinTS+XS2A) for multibanking later — GoCardless/Nordigen is closed to new accounts since July 2025; abstract the provider behind an interface
- **Hetzner** (Nuremberg/Falkenstein) hosting; UbiCloud if managed Postgres is wanted
- **Hangfire** (background jobs), **Serilog** (audit-relevant logging), **Testcontainers** (RLS isolation tests)

### Expected Features

Full landscape with dependency graph in `FEATURES.md`.

**Must have (table stakes):**
- Contacts (customers/suppliers), product catalog, quotes → order confirmations → delivery notes → invoices (document chain)
- E-invoice **receive/parse/render** (XRechnung + ZUGFeRD — already mandatory) and **create/send** (the 2027/28 wedge; make it available in every tier, unlike Lexware)
- Offene-Posten ledger (design into v1 even though reconciliation ships later), Mahnwesen, PDF layouts/Briefpapier
- GoBD-conformant behavior throughout (immutability, audit trail, archive later)

**Should have (competitive):**
- Modern fast PWA (Lexware feels dated; sevDesk's app is the benchmark), genuine DE+EN UI (competitors are German-only)
- E-invoicing in all tiers at honest pricing
- Later: automatic Zahlungsabgleich (crown-jewel retention feature; needs banking + OP ledger + matching engine), Belegscanner/OCR, DATEV export + Steuerberater access

**Defer (v2+):**
- Bookkeeping engine (SKR03/04), USt-VA/ELSTER, EÜR/GuV/BWA, Anlagenverwaltung, Kassenbuch
- Banking + reconciliation, customer portal, public API, Stripe billing, Serienrechnungen/special invoice types

**Anti-features (deliberately NOT building):**
- Lohn & Gehalt (ITSG certification — separate product), full Fibu/Bilanz competing with DATEV Kanzlei, Warenwirtschaft/ERP, self-built FinTS connectors, own OCR engine, own ELSTER protocol implementation

### Architecture Approach

Modular monolith + worker tier + single Postgres with RLS. Invoice finalization is one atomic transaction (number + freeze + open item + audit + domain event). The ledger is designed as a clean later-add: v1 emits domain events (`InvoiceFinalized`, `PaymentReceived`, `InvoiceCancelled`) and ships the inert ledger schema (accounts/journal_entries/postings) from day one. Full details in `ARCHITECTURE.md`.

**Major components:**
1. **Platform kernel** — tenancy/RLS, auth (Keycloak), Money type, audit log, entitlement/feature-gating, inert ledger schema
2. **Master data** — CRM (customers/suppliers), product catalog
3. **Sales document core** — Angebot→Auftrag→Lieferschein→Rechnung chain, immutability state machine, gapless-enough per-tenant numbering (locked `number_sequences` row, never a Postgres SEQUENCE), Storno flow, domain events
4. **Worker tier** — PDF rendering, e-invoice generation/validation, e-mail dispatch, dunning runs
5. **E-invoice engine** — EN 16931 semantic model → XML (UBL+CII) + PDF/A-3; inbound parsing/validation; KoSIT sidecar
6. **Open items + dunning** — receivables, payment status, Mahnstufen
7. Later: ledger/bookkeeping, tax reports, banking module (isolated), DATEV export, portal, public API

### Critical Pitfalls

Top 5 of 8 from `PITFALLS.md` (all are gating requirements, not backlog items):

1. **PDF ≠ e-invoice** — the structured XML is the legally binding artifact. Build invoice as data-first (EN 16931 semantic model), render XML and PDF from one source; KoSIT-validate every issued invoice; PDF and embedded XML figures must match exactly.
2. **Float money / wrong rounding order** — EN 16931 (BR-CO-10/15, BR-S-08) mandates rounding per VAT category first, then summing. Use `decimal`/`numeric` end-to-end; golden-file tests against KoSIT-validated references.
3. **Editing finalized invoices** — GoBD Unveränderbarkeit. Draft→Finalized one-way gate; corrections are new documents (Storno/Korrektur); enforce at DB level.
4. **Multi-tenant data leakage** — RLS as primary control on every tenant table; `SET LOCAL` (never `SET`) with pooling; cross-tenant test suite; watch caches, jobs, blob paths, exports.
5. **Numbering myths and races** — per-tenant atomic counter (SELECT…FOR UPDATE) at finalization; uniqueness + traceability is the legal rule, NOT gaplessness — never reuse numbers, don't build brittle gapless logic.

Also: VAT as category codes with mandatory legal notes (§13b `AE`, intra-EU `K`, Kleinunternehmer `E`); never claim "GoBD-zertifiziert" (no such certification exists — say "GoBD-konform", ship a Verfahrensdokumentation); 10-year retention overrides GDPR erasure; 180-day (not 90) PSD2 re-auth; iOS PWA limits (finalization online-only, offline drafting at most).

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Platform Kernel (Foundation)
**Rationale:** Everything retrofit-impossible lands here: RLS tenancy, Money type, audit, auth.
**Delivers:** Multi-tenant skeleton — registration/login (Keycloak), tenant context (RLS + EF filters), Money/decimal domain type, append-only audit log, entitlement gates (S/M/L/XL), inert ledger schema, CI with Testcontainers RLS isolation tests, i18n scaffold (DE+EN).
**Avoids:** Pitfalls 2, 4, 8 (float money, tenant leakage, DSGVO/hosting bolted on late).

### Phase 2: Master Data (CRM + Catalog)
**Rationale:** Validates tenancy plumbing on simple domains before the financial core; invoicing needs customers and products.
**Delivers:** Customers/suppliers CRUD with history/notes, product/service catalog, first real PWA screens.

### Phase 3: Sales Document Core
**Rationale:** The heart of v1; immutability + numbering must land WITH this phase, not after.
**Delivers:** Angebot→Auftragsbestätigung→Lieferschein→Rechnung chain, draft/finalized state machine, per-tenant atomic numbering, Storno/Korrektur flow, VAT category model (S/AE/K/E incl. Kleinunternehmer), domain events, offene-Posten ledger entries on finalization.
**Avoids:** Pitfalls 3, 5, 6 (mutable invoices, numbering races, VAT edge cases).

### Phase 4: PDF Rendering + Worker Tier
**Rationale:** Finalization must stay fast; rendering is async by design.
**Delivers:** Hangfire worker tier, QuestPDF invoice layouts/Briefpapier, e-mail dispatch, PDF/A-3 spike (QuestPDF conformance vs iText fallback).

### Phase 5: E-Invoice Engine
**Rationale:** The strategic wedge; depends on the semantic model from Phase 3.
**Delivers:** EN 16931 semantic model, XRechnung (UBL + CII) generation, ZUGFeRD PDF/A-3 hybrid, KoSIT validator sidecar (CI + runtime gate), inbound parsing/validation/rendering of received e-invoices, human-readable view.
**Avoids:** Pitfall 1 (PDF-as-e-invoice), PDF/XML mismatch.

### Phase 6: Open Items + Mahnwesen
**Rationale:** Completes the v1 money loop (invoice → outstanding → reminder).
**Delivers:** OP-Übersicht, payment status (manual payment recording), Zahlungserinnerung + Mahnstufen with scheduled dunning runs.

### Phase 7: v1 Polish — PWA, Feature Gates, Compliance Pass
**Rationale:** Launch readiness.
**Delivers:** PWA install/offline-draft behavior, tier gating UX, DSGVO artifacts (AVV, data export), Verfahrensdokumentation draft, "GoBD-konform" copy audit, cross-tenant security test pass.

### Later milestones (v1.x / v2)
Belegerfassung/OCR + GoBD archive → Banking (finAPI) + Zahlungsabgleich → Ledger/bookkeeping (SKR03/04) + Kassenbuch → USt-VA/ZM (calculation+export, ERiC later) + EÜR/GuV/BWA → DATEV export + Steuerberater-Zugang → Portal, Public API, Stripe billing, Anlagenverwaltung.

### Phase Ordering Rationale

- Dependency-driven: tenancy/money/audit gate everything; document chain precedes e-invoicing (shared semantic model); OP ledger precedes dunning and later reconciliation.
- The two "pay now or rewrite later" seams land earliest: RLS + Money (Phase 1) and immutability + numbering (Phase 3).
- E-invoicing gets its own phase because EN 16931 XML correctness is where products fail — isolated, testable, KoSIT-gated.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Sales Document Core):** GoBD immutability/audit-trail design details, VAT category mapping table (Steuerberater review recommended)
- **Phase 5 (E-Invoice Engine):** EN 16931 BT-field mapping, XRechnung 3.0.2 vs upcoming 4.0, KoSIT validator integration, PDF/A-3 conformance
- **Banking milestone:** finAPI production pricing (not public), 180-day re-consent UX
- **DATEV milestone:** EXTF format spec, SKR03/04 mapping
- **USt-VA milestone:** ERiC native library integration

Phases with standard patterns (skip research-phase):
- **Phase 2 (Master Data):** standard CRUD
- **Phase 4 (PDF/Workers):** well-documented (QuestPDF, Hangfire)
- **Phase 6 (OP/Mahnwesen):** domain logic is clear once Phase 3 model exists

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Versions verified against NuGet/official docs; MEDIUM on bank-API pricing (not public) and shadcn-vs-MUI choice |
| Features | HIGH | Regulatory timeline verified (BMF/IHK); competitor matrix triangulated; exact tier pricing indicative only |
| Architecture | HIGH | RLS/monolith/immutability patterns consistent across sources; ledger design MEDIUM-HIGH |
| Pitfalls | HIGH | Legal/e-invoicing facts from EU Commission, BMF, EBA; product wisdom MEDIUM |

**Overall confidence:** HIGH

### Gaps to Address

- **QuestPDF PDF/A-3b conformance** — spike in Phase 4/5; fallback iText (licensed) or Java PDF step
- **finAPI pricing** — get a quote before the banking milestone; deciding factor vs Kosma/Tink
- **XRechnung 4.0** (expected end 2026) — confirm library/validator upgrade path during Phase 5
- **Hosting decision** (Hetzner vs UbiCloud managed Postgres) — decide in Phase 1
- **Pricing strategy** (free tier vs trial-only) — business decision, not blocking build

## Sources

### Primary (HIGH confidence)
- BMF FAQ E-Rechnung, EU Commission eInvoicing Germany, IHK — regulatory timeline
- NuGet (ZUGFeRD-csharp 18.0.0), itplr-kosit/validator (1.6.0), QuestPDF docs — library versions
- PostgreSQL 18 release notes, Microsoft .NET support policy — platform versions
- ConnectingEurope/eInvoicing-EN16931 Schematron — validation rules
- EBA RTS amendment — 180-day re-auth

### Secondary (MEDIUM confidence)
- sevDesk/Lexware/BuchhaltungsButler comparison pages — competitor features/tiers
- AWS Prescriptive Guidance, community RLS best practices — multi-tenancy patterns
- openbankingtracker, finAPI/Klarna Kosma sites — bank aggregator landscape

---
*Research completed: 2026-07-09*
*Ready for roadmap: yes*
