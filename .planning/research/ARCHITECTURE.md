# Architecture Research

**Domain:** Multi-tenant SaaS accounting & e-invoicing (German market, Lexware Office / sevDesk competitor)
**Researched:** 2026-07-09
**Confidence:** HIGH (multi-tenancy, ledger, GoBD, e-invoice pipeline all verified against multiple sources incl. official KoSIT/Mustang docs); MEDIUM on specific stack/library version pins.

## Standard Architecture

Modern SaaS accounting products in this class are built as a **modular monolith with a separate worker tier**, backed by a single Postgres database using row-level tenant isolation. This is the dominant pattern for products at this scale (sevDesk, Lexware Office, and comparable indie competitors): a monolith keeps the tightly-coupled financial domain transactionally consistent, while heavy/slow work (PDF rendering, e-invoice validation, e-mail, later bank sync) is pushed to background workers.

Do **not** start with microservices. The financial core (invoices → open items → ledger postings) must be transactionally consistent; splitting it across services introduces distributed-transaction pain for zero early benefit. Extract services later only if a component (e.g. e-invoice validation, bank sync) genuinely needs independent scaling or isolation.

### System Overview

```
┌───────────────────────────────────────────────────────────────────────┐
│                        CLIENT (Responsive PWA)                          │
│         React/Vue SPA · i18n (DE/EN) · service worker · camera scan     │
└───────────────────────────────────────────────┬──────────────────────-─┘
                                                 │ HTTPS / JSON (internal API)
┌───────────────────────────────────────────────▼──────────────────────-─┐
│                     API / APPLICATION TIER (monolith)                    │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌──────────┐ ┌──────────────┐  │
│  │  Auth &  │ │ Tenant / │ │ Entitle-  │ │  Audit   │ │  API Gateway │  │
│  │ Sessions │ │ Mandant  │ │ ments     │ │  Logger  │ │ (REST layer) │  │
│  └────┬─────┘ └────┬─────┘ └─────┬─────┘ └────┬─────┘ └──────┬───────┘  │
│  ─────┴────────────┴─────── cross-cutting ────┴──────────────┴───────    │
│  ┌──────────────┐ ┌──────────────┐ ┌────────────────┐ ┌──────────────┐  │
│  │  CRM         │ │  Catalog     │ │  Sales Docs    │ │  E-Invoice   │  │
│  │ (customers/  │ │ (products/   │ │ (Angebot→…→    │ │  Engine      │  │
│  │  suppliers)  │ │  services)   │ │  Rechnung)     │ │ (gen/parse)  │  │
│  └──────┬───────┘ └──────┬───────┘ └───────┬────────┘ └──────┬───────┘  │
│         │                │                 │                 │          │
│  ┌──────┴────────┐ ┌─────┴─────────┐ ┌─────┴────────┐ ┌──────┴───────┐  │
│  │ Open Items /  │ │  Dunning      │ │ Numbering    │ │ Ledger       │  │
│  │ Receivables   │ │  (Mahnwesen)  │ │ Sequences    │ │ (v2+, stub)  │  │
│  └───────────────┘ └───────────────┘ └──────────────┘ └──────────────┘  │
└──────────────┬─────────────────────────────────────────┬───────────────┘
               │ enqueue jobs                             │ read/write
┌──────────────▼──────────────────┐        ┌──────────────▼───────────────┐
│      WORKER TIER (queue)         │        │       DATA TIER               │
│  PDF render · e-mail · e-invoice │        │  Postgres (RLS, tenant_id)    │
│  validate · (later) bank sync,   │◄──────►│  Object store (immutable      │
│  OCR/receipt capture             │        │   docs, GoBD archive)         │
└──────────────────────────────────┘        │  Redis (queue + cache)        │
                                             └───────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility (what it owns) | Typical Implementation |
|-----------|-------------------------------|------------------------|
| **Auth & Sessions** | Registration, login, password/2FA, session/JWT issuance, user↔tenant membership | Framework auth + JWT/session; sets tenant context per request |
| **Tenant / Mandant** | Company accounts, tenant lifecycle, user roles within a tenant, per-tenant settings (Briefpapier, tax defaults) | `tenants` table + membership join; sets Postgres session var for RLS |
| **Entitlements (tier gating)** | Resolves what S/M/L/XL plan allows (features + limits) at runtime; single source of truth for gating | Server-side plan→capability map; checked in a guard/middleware, never client-only |
| **Audit Logger** | Append-only record of who-did-what-when across all financial state changes | Dedicated `audit_events` table, no UPDATE/DELETE grant to app DB user |
| **API Gateway / REST layer** | HTTP surface, request validation, authz composition (auth ∧ tenant ∧ entitlement) | Controllers/routers; same layer later exposed as public API |
| **CRM (customers/suppliers)** | Master data for Kunden & Lieferanten, history, notes | Standard CRUD, tenant-scoped |
| **Catalog** | Products & services (Artikel), prices, tax categories | Standard CRUD, tenant-scoped |
| **Sales Documents** | The document chain Angebot→Auftragsbestätigung→Lieferschein→Rechnung; line items, totals, tax; immutability on finalization; Storno | Core domain aggregate; money as integer minor units / decimal, never float |
| **Numbering Sequences** | Gapless (lückenlos), per-tenant, per-doc-type number ranges | DB sequence table with row lock / advisory lock inside the finalize transaction |
| **E-Invoice Engine** | Generate XRechnung (UBL+CII) & ZUGFeRD PDF/A-3; parse/validate inbound e-invoices against EN 16931 | Wraps Mustang/KoSIT (JVM) — usually a small service/sidecar the workers call |
| **Open Items / Receivables** | Forderungen, due dates, payment status, matching payments to invoices | Derived from finalized invoices + payment events |
| **Dunning (Mahnwesen)** | Reminder levels, overdue detection, dunning documents | Scheduled job scanning open items |
| **Ledger (v2+)** | Double-entry journal: accounts (SKR03/04), balanced postings, immutable | Stubbed data model in v1 so invoicing can post later without rework |
| **Worker tier** | All slow/async work: PDF, e-mail, e-invoice validation, later OCR & bank sync | Queue consumers (BullMQ/Redis or DB-backed queue) |
| **Data tier** | Postgres (system of record), object store (immutable document blobs), Redis | Managed or self-hosted (DSGVO-friendly hosting, e.g. Hetzner) |

## Recommended Project Structure

A **feature/domain-module** layout (vertical slices) rather than technical layering. Each module owns its entities, services, and HTTP surface; cross-cutting concerns (auth, tenancy, entitlements, audit) live in a shared kernel.

```
src/
├── platform/               # cross-cutting kernel — everything depends on this
│   ├── tenancy/            # tenant context, RLS session-var setter, middleware
│   ├── auth/               # login, sessions/JWT, membership, roles
│   ├── entitlements/       # plan→capability map, gating guard
│   ├── audit/              # append-only audit writer
│   ├── money/              # Money value object (integer minor units / decimal), rounding, tax calc
│   └── db/                 # connection, migrations, RLS policies
├── modules/
│   ├── crm/                # customers & suppliers
│   ├── catalog/            # products & services
│   ├── sales/              # Angebot, Auftrag, Lieferschein, Rechnung
│   │   ├── documents/      # document aggregate + state machine
│   │   ├── numbering/      # gapless sequence service
│   │   ├── open-items/     # receivables
│   │   └── dunning/        # Mahnwesen
│   ├── einvoice/           # generation + inbound parsing/validation adapters
│   ├── documents-store/    # immutable blob storage + GoBD archive metadata
│   └── ledger/             # (v1: schema + interfaces only; v2: posting engine)
├── jobs/                   # queue definitions + worker handlers (PDF, email, validate…)
├── api/                    # public API surface (later) — thin adapter over modules
└── i18n/                   # DE/EN resource bundles (server-side messages, doc templates)
```

### Structure Rationale

- **platform/ as a shared kernel:** Tenancy, auth, entitlements, audit, and the Money type are needed by every module. Centralizing them prevents each feature from re-implementing (and mis-implementing) tenant isolation or money rounding.
- **modules/sales as one cohesive module:** The document chain, numbering, open items, and dunning are tightly coupled and share transactions. Keeping them together avoids premature service boundaries where they'd hurt.
- **einvoice/ as an adapter module:** Isolates the JVM-based Mustang/KoSIT dependency behind a clean interface so the rest of the app doesn't care whether validation runs in-process, in a worker, or in a sidecar.
- **ledger/ present from day one (even if inert):** Defining the accounts/journal/posting schema and a `PostingSource` interface in v1 lets invoicing later emit postings without touching the invoice tables — see "what v1 must get right" below.

## Architectural Patterns

### Pattern 1: Row-Level Multi-Tenancy (Postgres RLS + tenant_id)

**What:** Every tenant-owned table carries a `tenant_id` column. Postgres RLS policies filter every query by a session variable (`app.current_tenant`) that the tenancy middleware sets at the start of each request/transaction. The database — not application code — enforces isolation.

**When to use:** Default for this product. Escalate to schema-per-tenant or DB-per-tenant only for specific enterprise customers demanding data residency/isolation; do not start there.

**Trade-offs:** Cheapest to operate, one migration set, easy cross-tenant admin/analytics. RLS overhead is typically 1–5% (planner pushes policies into index scans). Risk: a missing/wrong policy or an unset session var is a cross-tenant leak — so isolation correctness must be tested aggressively and the session var must be set in one central place.

**Example:**
```sql
ALTER TABLE invoices ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON invoices
  USING (tenant_id = current_setting('app.current_tenant')::uuid);
-- middleware, per request/transaction:
SET LOCAL app.current_tenant = '…tenant-uuid…';
```

### Pattern 2: Immutable Finalized Documents + Storno (never edit/delete)

**What:** Invoices have a lifecycle `draft → finalized(→sent→paid)`. A `draft` is freely editable. On finalization, the document becomes immutable: it gets its gapless number, its rendered PDF/e-invoice is persisted to the immutable store, and no field may change. Corrections happen only by issuing a **Storno** (cancellation document) and a new corrected invoice under a new number. This is a hard GoBD requirement — "released invoices cannot be corrected or deleted; errors are fixed by cancellation + new booking, never by overwriting."

**When to use:** All legally-relevant outbound documents (at minimum invoices; the same discipline is applied to credit notes and, later, ledger postings and receipt archive).

**Trade-offs:** More records and a slightly more complex UX (you can't just "edit" a sent invoice). But it is non-negotiable for compliance and it makes the audit trail naturally complete. Enforce immutability at the DB level too (trigger blocking UPDATE/DELETE on finalized rows), not just in app code.

**Example (state guard):**
```
finalize(invoice):
  in one DB transaction:
    assert invoice.status == 'draft'
    invoice.number = numbering.next(tenant, 'invoice')   # gapless, locked
    invoice.status = 'finalized'; invoice.finalized_at = now()
    audit.record('invoice.finalized', invoice.id)
  enqueue(render_pdf + generate_einvoice)   # after commit
```

### Pattern 3: Gapless Per-Tenant Numbering Inside the Finalize Transaction

**What:** A `number_sequences(tenant_id, doc_type, next_value)` table. `next()` runs inside the same transaction that finalizes the document, taking a row lock (or advisory lock) so two concurrent finalizations can't produce a gap or a duplicate. The number is assigned only at finalization, never at draft creation (drafts may be deleted → would cause gaps).

**When to use:** Every legally-numbered document type (invoices, credit notes; separate ranges per type/tenant).

**Trade-offs:** The row lock serializes finalizations per tenant — fine at this scale (per-tenant, not global). Do **not** use a plain Postgres `SEQUENCE` object: those are non-transactional and leave gaps on rollback, which violates lückenlose Nummerierung.

### Pattern 4: Command → Domain Transaction → Enqueue Side-Effects

**What:** User actions are handled synchronously only up to the consistent DB write; everything slow (PDF render, e-invoice generation/validation, e-mail, later bank sync) is enqueued and returns immediately. Workers pick up jobs, retry on failure, and write results back.

**When to use:** Any operation that renders, calls an external service, or takes >100–200ms.

**Trade-offs:** Requires a queue (Redis/BullMQ or a DB-backed queue) and idempotent handlers, but keeps the API responsive and makes ret/failure handling explicit. Jobs must carry `tenant_id` and re-establish tenant context.

### Pattern 5: Entitlement Gate (server-side tier resolution)

**What:** A single `entitlements` service maps the tenant's plan (S/M/L/XL) to a capability/limit set, evaluated server-side in a guard/middleware. Never gate security-relevant features in the client only. Modeled as entitlements ("capabilities the subscription grants"), so when billing (Stripe, later) changes the plan, capabilities update in one place.

**When to use:** Every gated feature and every quota check.

**Trade-offs:** Slight indirection, but it decouples "what the plan allows" from feature code and from the (future) billing system — the scatter/gather authz check becomes `auth ∧ tenant ∧ entitlement`.

## Data Flow

### Request Flow (create & finalize an invoice)

```
[User: finalize invoice]
    ↓
[PWA] → [API controller] → [authz: auth ∧ tenant ∧ entitlement]
    ↓
[Sales module.finalize()] ── DB txn ──►[assign gapless number][mark immutable][audit.record]
    ↓ (after commit) enqueue
[Worker: render PDF] → [Worker: generate XRechnung/ZUGFeRD] → [validate EN 16931]
    ↓                              ↓
[Object store: immutable PDF/A-3 + XML]   [Open Items: new receivable created]
    ↓
[Worker: e-mail to customer]   [Audit: each step logged]
```

### Inbound E-Invoice Flow

```
[Upload / (later) e-mail intake]  →  [einvoice module: detect format (UBL/CII/ZUGFeRD)]
    ↓
[extract XML from PDF/A-3 if ZUGFeRD] → [validate against EN 16931 (Mustang/KoSIT)]
    ↓                                          ↓ (invalid)
[map to internal supplier-invoice model]   [flag + surface validation errors to user]
    ↓
[store original immutably (GoBD)] → [create payable / receipt record] → [audit.record]
```

### Key Data Flows

1. **Document chain (copy-forward):** Angebot → Auftragsbestätigung → Lieferschein → Rechnung. Each stage copies line items forward and links back to its predecessor (`source_document_id`), so an invoice knows which quote it came from. Only the invoice (and credit note) are legally immutable/numbered; upstream docs are more lenient.
2. **Invoice → Open Item → Payment → (later) Ledger posting:** Finalizing an invoice creates a receivable (open item). A payment (manual in v1, bank-matched later) settles it. In v2, the same finalize/payment events emit balanced double-entry postings — which is why the ledger schema must exist in v1.
3. **Everything financial → Audit log:** Every state transition (finalize, storno, payment, plan change) appends to the immutable `audit_events` table synchronously in the same transaction.

## What v1 Must Get Right So Double-Entry Can Be Added Without Rework

This is the highest-leverage architectural decision, per the milestone brief. The ledger is a **later** milestone, but v1 choices decide whether it's a clean add or a rewrite:

1. **Money as integer minor units (or fixed decimal), never float.** All amounts, tax lines, and totals stored and computed as exact decimals/integers with explicit rounding rules per line and per tax rate (19%, 7%, §13b reverse-charge, innergemeinschaftlich). A single `Money` value object owns rounding. Floats here = un-postable, un-auditable numbers later.
2. **Model tax at line-item granularity with a tax-category code**, not a single invoice-level rate. The ledger and USt-VA later need per-rate breakdowns; retrofitting them onto flat invoices is painful.
3. **Represent finalization as an immutable, event-emitting act.** Invoicing should emit domain events (`InvoiceFinalized`, `PaymentReceived`, `InvoiceCancelled`) even in v1 — even if only the audit log consumes them. The ledger posting engine later subscribes to the same events. If v1 finalizes by silently mutating rows, there's nothing for the ledger to hook into.
4. **Create the ledger schema now, inert.** `accounts` (SKR03/04-ready: number, name, type), `journal_entries` (header, date, source ref, balanced), `postings` (account, debit/credit, amount — must sum to zero per entry). No posting logic in v1, just the tables + a `PostingSource` interface. This guarantees invoicing's later hook has a stable target and forces v1 to keep the data that postings will need (booking date, tax code, contra-account hints).
5. **Immutability + append-only discipline established in v1.** If invoices, audit, and (stub) postings are all append-only from the start, the ledger inherits a system where "correct by reversal" is already the norm.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 0–1k tenants | Single Postgres + RLS, one app process + a few workers, Redis for queue/cache. Monolith is correct. No sharding. |
| 1k–100k tenants | Add read replicas for reporting; scale workers horizontally (each pulls same queue); connection pooling (PgBouncer); consider moving heavy e-invoice validation to its own worker pool. RLS still fine. |
| 100k+ tenants | Consider extracting the e-invoice engine and bank-sync as independent services; partition largest tables by tenant; offer schema/DB-per-tenant for enterprise/residency; move immutable docs to dedicated object storage with lifecycle policies for the 10-year GoBD retention. |

### Scaling Priorities

1. **First bottleneck: worker throughput (PDF + e-invoice validation).** JVM-based Mustang/KoSIT validation is CPU-heavy. Fix by scaling the worker pool and isolating validation into its own queue so it can't starve e-mail/PDF jobs.
2. **Second bottleneck: reporting/analytics contention on the primary DB.** BWA/USt-VA/EÜR queries scan lots of rows. Fix with read replicas and, if needed, a reporting-oriented denormalization — never by weakening RLS.

## Anti-Patterns

### Anti-Pattern 1: Application-only tenant filtering (no RLS)

**What people do:** Rely on `WHERE tenant_id = ?` in every query, enforced only in code.
**Why it's wrong:** One forgotten filter = cross-tenant financial data leak — catastrophic for accounting software. Easy to miss in a new query or a raw report.
**Do this instead:** Enforce isolation in Postgres with RLS; treat the app filter as defense-in-depth, not the primary control.

### Anti-Pattern 2: Editing or deleting finalized invoices

**What people do:** Add an "edit invoice" button that mutates a sent invoice, or a "delete" that removes it.
**Why it's wrong:** Violates GoBD Unveränderbarkeit and breaks the audit trail; can produce number gaps.
**Do this instead:** Immutable after finalization; correct via Storno + new invoice under a new number. Block UPDATE/DELETE at the DB level for finalized rows.

### Anti-Pattern 3: Postgres SEQUENCE / auto-increment for invoice numbers

**What people do:** Use a DB sequence or `SERIAL` for the invoice number.
**Why it's wrong:** Sequences are non-transactional — a rolled-back finalize burns a number, creating a gap, violating lückenlose Nummerierung. Also not per-tenant.
**Do this instead:** A locked `number_sequences` row per (tenant, doc_type), advanced inside the finalize transaction.

### Anti-Pattern 4: Floating-point money

**What people do:** Store/compute amounts as float/double.
**Why it's wrong:** Rounding drift makes tax totals and (later) ledger postings not balance — un-auditable and legally wrong.
**Do this instead:** Integer minor units or fixed-precision decimal behind a `Money` type with explicit rounding.

### Anti-Pattern 5: Premature microservices for the financial core

**What people do:** Split invoices, open items, and ledger into separate services early.
**Why it's wrong:** These require shared transactions (finalize must atomically number + create receivable + audit); distributing them forces sagas/eventual consistency where strong consistency is required.
**Do this instead:** Modular monolith; extract only truly independent components (e-invoice validation, bank sync) later.

### Anti-Pattern 6: Client-side tier gating

**What people do:** Hide gated features in the PWA only.
**Why it's wrong:** Trivially bypassed; entitlement/quota checks must be authoritative.
**Do this instead:** Server-side entitlement guard on every gated action and quota.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| **Mustang / KoSIT validator** | JVM library; call from workers, often as a sidecar/microservice with a thin HTTP wrapper | Reference implementation of the German government; keep version current (tracks XRechnung releases, e.g. 3.0.2). Non-JVM stacks must shell out or run it as a service. |
| **E-mail (SMTP/API)** | Async via worker; templated (DE/EN) | Later: inbound e-mail intake for automatic receipt/e-invoice capture. |
| **Object storage** | S3-compatible or filesystem; write-once semantics for finalized docs | Backs the GoBD immutable archive (10-year retention, versioning/lock). DSGVO-friendly hosting. |
| **Bank API (later)** | Dedicated worker/service polling or webhook; provider TBD (research flag) | Multibanking + payment reconciliation; isolate as its own module/service. |
| **DATEV export (later)** | Batch export adapter from the ledger | Needs the ledger + tax categories to exist correctly. |
| **Stripe (later)** | Webhook → entitlements update | Billing state drives entitlements; the entitlements layer already exists in v1, so Stripe just feeds it. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| API tier ↔ Worker tier | Queue (enqueue after DB commit) | Jobs carry tenant_id; handlers re-set tenant context and are idempotent. |
| Sales ↔ Ledger (v2) | Domain events (`InvoiceFinalized`, `PaymentReceived`, `InvoiceCancelled`) | Decouples posting engine from invoice internals — the key seam to build in v1. |
| Sales ↔ Open Items | In-process, same transaction | Finalize creates receivable atomically. |
| All modules ↔ platform kernel | Direct in-process calls | Tenancy, auth, entitlements, audit, money are shared dependencies. |
| Modules ↔ Public API (later) | Thin adapter over module services | Public API reuses the same services the internal API uses; add API-key auth + rate limiting. |

## Suggested Build Order (dependency-driven)

Each layer depends on the one above it; this order lets every later component hook into stable seams.

1. **Platform kernel first:** tenancy (RLS) + auth + `Money` type + audit + entitlement guard. *Everything* depends on these, and retrofitting RLS or Money later is a rewrite. Define the (inert) ledger schema here too.
2. **Master data:** CRM (customers/suppliers) + Catalog (products/services). Pure CRUD, tenant-scoped; validates the RLS/tenancy plumbing end-to-end.
3. **Sales documents core:** the Angebot→Auftrag→Lieferschein→Rechnung chain with copy-forward, the immutability state machine, and gapless numbering. Emit domain events from finalize. This is the heart of v1.
4. **Worker tier + PDF rendering:** enqueue-after-commit pattern; render invoice PDFs from templates (DE/EN, Briefpapier).
5. **E-invoice engine:** generation (XRechnung UBL+CII, ZUGFeRD PDF/A-3) + inbound parsing/validation via Mustang/KoSIT, driven by workers.
6. **Open items + Dunning:** receivables derived from finalized invoices; scheduled dunning job.
7. **Feature-gating polish + i18n hardening + PWA install/offline.** (Entitlement guard exists from step 1; here it's wired to real S/M/L/XL matrices.)
8. **(Later milestones)** Ledger posting engine hooks into the step-3 domain events → tax reports → assets → multibanking → DATEV → portal → public API → Stripe. None require reworking v1 if steps 1 and 3 got Money, tax-per-line, immutability, and events right.

**Critical dependency notes:**
- Tenancy/RLS and the `Money` type gate everything — build them first, get them right.
- Numbering + immutability must land *with* the sales core, not bolted on after documents already exist (retrofitting immutability onto mutable records is a data-migration mess).
- The ledger schema + domain events are cheap insurance in v1; skipping them is the single most likely cause of a later rewrite.

## Sources

- [Approaches to tenancy in Postgres — PlanetScale](https://planetscale.com/blog/approaches-to-tenancy-in-postgres) (HIGH)
- [Shipping multi-tenant SaaS using Postgres Row-Level Security — Nile](https://www.thenile.dev/blog/multi-tenant-rls) (MEDIUM)
- [Row-level security recommendations — AWS Prescriptive Guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-managed-postgresql/rls.html) (HIGH)
- [Multi-Tenant SaaS: 4 Database Isolation Strategies Compared](https://syedarifiqbal.com/blog/multi-tenant-saas-isolation) (MEDIUM)
- [GoBD und Ausgangsrechnungen — Scopevisio](https://www.scopevisio.com/blog/steuern-und-recht/gobd-und-ausgangsrechnungen/) (MEDIUM)
- [Was bedeutet GoBD — sevDesk](https://sevdesk.de/ratgeber/buchhaltung-finanzen/buchhaltung/buchfuehrungspflicht/gobd/) (MEDIUM)
- [Stornorechnung erstellen 2026 GoBD-konform](https://kostenlose-erechnung.de/ratgeber/stornorechnung-vorlage-muster/) (MEDIUM)
- [Mustang Project — use/commandline docs](https://www.mustangproject.org/use/) (HIGH, official)
- [ZUGFeRD/mustangproject Release Notes (GitHub)](https://github.com/ZUGFeRD/mustangproject/blob/master/Release_Notes.md) (HIGH, official)
- [KoSIT Validator Anleitung — rechnex.de](https://rechnex.de/blog/kosit-validator-anleitung) (MEDIUM)
- [An Elegant DB Schema for Double-Entry Accounting — Journalize.io](https://blog.journalize.io/posts/an-elegant-db-schema-for-double-entry-accounting/) (MEDIUM)
- [Books — an immutable double-entry accounting database service (Square)](https://developer.squareup.com/blog/books-an-immutable-double-entry-accounting-database-service/) (HIGH)
- [Designing a real-time ledger system with double-entry logic — Finlego](https://finlego.com/blog/designing-a-real-time-ledger-system-with-double-entry-logic) (MEDIUM)
- [Best Background Job Queues for Node.js SaaS in 2026 — NodeStack](https://nodejs.tech/posts/best-background-job-queues-nodejs-saas/) (MEDIUM)
- [Background Job Processing in Node.js: BullMQ (2026)](https://dev.to/young_gao/background-job-processing-in-nodejs-bullmq-queues-and-worker-patterns-31d4) (MEDIUM)
- [Entitlements Over Feature Flags — StackBE](https://stackbe.io/blog/entitlements-over-feature-flags/) (MEDIUM)
- [The audit_logs table: an architectural anti-pattern — LogVault](https://www.logvault.app/blog/audit-logs-table-anti-pattern) (MEDIUM)
- [Event Sourcing Pattern — Microsoft Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing) (HIGH, official)

---
*Architecture research for: multi-tenant SaaS accounting & e-invoicing (German market)*
*Researched: 2026-07-09*
