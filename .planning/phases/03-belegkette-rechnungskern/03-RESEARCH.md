# Phase 3: Belegkette & Rechnungskern - Research

**Researched:** 2026-07-12
**Domain:** GoBD-immutable sales-document chain + invoice core (race-safe numbering, EN 16931 VAT categories, open items) on .NET 10 / EF Core 10 / Postgres 18 with FORCE RLS
**Confidence:** HIGH on data model, immutability, numbering, VAT, open items (verified against existing Phase 1/2 code + EN 16931 / §14 UStG / German B2G sources); MEDIUM on the exact EN 16931 exemption-reason code strings (VATEX list) and on domain-event ambition (a discretion call, flagged).

No CONTEXT.md exists for this phase (no `/gsd:discuss-phase` was run). The section below records the LOCKED foundation the planner must build ON, not revisit.

---

## Locked Foundation (do NOT revisit — inherited from Phase 1/2, verified in code)

These are settled platform decisions. Research builds on them; the planner must honor them verbatim.

- **Multi-tenancy:** shared schema, `tenant_id uuid NOT NULL`, Postgres **FORCE RLS** as the primary control (app role `numera_app`, **no BYPASSRLS**) + EF Core **named** query filters as defence-in-depth. **Every new tenant table** needs hand-written `ENABLE + FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation ... USING (...) WITH CHECK (...)` + a `tenant_id`-leading index, written in the migration. Reflective entity discovery does **not** create policies — the #1 silent-leak trap. One RLS integration test per new table = hard CI gate.
- **Money:** `decimal` only, `numeric(19,4)` (amounts) / `numeric(19,6)` (unit prices), never float. `Numera.Platform.Money.Money` (readonly record struct), `TaxCategory` enum (`S/AE/K/E/Z/G/O`), `RoundingPolicy` (`RoundTax` per category `AwayFromZero`, `DocumentVatTotal` = sum of rounded per-category amounts — never round the grand total). Reuse these — do NOT add a parallel money type.
- **PKs:** UUIDv7 via `Guid.CreateVersion7()`.
- **Migrations:** live in `Numera.Platform.Db/Migrations` with `Numera.Api` as `--startup-project`; module tables discovered reflectively via `ITenantEntity`; table/column names snake_cased by the DbContext's reflective snake-caser (owned-type-safe).
- **Audit:** reuse `IAuditWriter.RecordAsync` — it `Add`s an `AuditEvent` onto the **caller's** `NumeraDbContext` and is committed by the caller's single `SaveChangesAsync` (atomic). Call it INSIDE the finalize transaction, never as a separate op.
- **Stack:** .NET 10 / C# 14, EF Core 10 + Npgsql, minimal-API endpoints in `Numera.Api/Endpoints`, FluentValidation (`AddValidatorsFromAssemblyContaining<Program>`), Contracts/ + Validators/. Web: React 19 + Vite, shared `DataTable` (TanStack Table, `manualPagination`, `translationNs` prop), shadcn/ui primitives, cookie-BFF `lib/api/*.ts` clients, RHF + zod forms, i18next namespaces.
- **Ledger** (`Numera.Modules.Ledger`): `Account` / `JournalEntry` / `Posting` / `IPostingSource` schema is shipped **inert**. Phase 3 must be ledger-READY (emit events / keep the data postings will need) but must **NOT** build double-entry booking logic.
- **Explicitly deferred — design SEAMS only, do NOT build:** PDF rendering (Phase 4), XRechnung/ZUGFeRD XML generation (Phase 5), payment recording + Mahnwesen (Phase 6), Fremdwährung / Serienrechnungen / Abschlagsrechnungen (Phase 7). BUT the invoice data model must be EN-16931-shaped **now**.

---

## Summary

Phase 3 is the compliance heart of v1 and contains two irreversible "now or never" seams: **GoBD immutability** and **race-safe per-tenant numbering**. Both must be enforced in Postgres, not just app code, mirroring the append-only pattern already proven on `audit_events` (REVOKE + BEFORE trigger).

The recommended shape is **one polymorphic `sales_documents` table** (a `document_type` discriminator: Angebot / Auftragsbestätigung / Lieferschein / Rechnung / Storno / Gutschrift) with a shared `sales_document_lines` child, a persisted `sales_document_tax_breakdown` child (the EN 16931 BG-23 VAT breakdown, frozen at finalize), and a self-referencing `source_document_id` for the copy-forward chain plus `corrects_document_id` for Storno/Gutschrift → original links. Only Rechnung/Storno/Gutschrift are legally numbered + immutable; upstream documents are lighter (mutable, optional non-legal numbers, delivery notes may hide prices). A **status-guarded trigger** (not a blanket REVOKE, because drafts must stay editable) freezes the business columns of a document once `status != 'draft'`, allowing only a whitelisted set of lifecycle columns (status→sent/cancelled, storage refs, cancellation back-link) to change. Numbering is assigned **only at finalize**, inside the same transaction, via an atomic `INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING` on a per-`(tenant, doc_type, year)` counter row — never a Postgres `SEQUENCE`, never `MAX()+1`. German law requires **einmalig + nachvollziehbar** (unique + traceable), **not** lückenlos (gapless): do not build brittle gap-prevention.

Issuer master data (name, address, USt-IdNr/Steuernummer, Kleinunternehmer flag, bank) does **not yet exist** anywhere — the `tenants` table is intentionally lean. A new tenant-scoped `company_profile` table must be added, and both issuer and recipient data must be **snapshotted onto the document at finalize** (GoBD: a later master-data edit must not mutate an issued invoice — the same snapshot discipline the Phase-2 catalog picker was explicitly built for).

**Primary recommendation:** Build a single `SalesDocument` aggregate (header + lines + tax-breakdown), finalize it in one transaction that {snapshots issuer/recipient onto the doc, computes+persists the VAT breakdown via `RoundingPolicy`, assigns the number via an upsert-returning counter, flips status to finalized, creates the open item, records the audit event, raises an in-process `InvoiceFinalized` domain event}, and enforce immutability + numbering integrity with Postgres triggers/constraints tested via Testcontainers as `numera_app`.

---

## User Constraints

No CONTEXT.md — no user-locked decisions or deferred ideas beyond the platform foundation and phase-deferral list captured under **Locked Foundation** above. Everything else in this document is Claude's-discretion recommendation for the planner.

---

## Standard Stack (all already present — no new NuGet/npm needed)

### Core (reuse)
| Component | Where | Purpose in Phase 3 |
|-----------|-------|--------------------|
| `Money`, `TaxCategory`, `RoundingPolicy`, `TaxBucket` | `Numera.Platform.Money` | Line/document money + the ONLY VAT rounding authority. `RoundingPolicy.DocumentVatTotal(IEnumerable<TaxBucket>)` already implements per-category-round-then-sum. |
| `IAuditWriter` / `AuditWriter` | `Numera.Platform.Audit` | Atomic audit rows for `sales_document.finalized` / `.cancelled` / `.credited`, inside the finalize txn. |
| `NumeraDbContext` + `ITenantEntity` / `IArchivable` | `Numera.Platform.Db` | New entities implement `ITenantEntity` → auto tenant index + named tenant filter. Drafts may be `IArchivable` (deletable); finalized docs must NOT be archivable-away. |
| `IPostingSource` / ledger schema | `Numera.Modules.Ledger` | Seam only. Do not implement in Phase 3. |
| Catalog picker `lookupCatalogItems` / `CatalogLineItem` | `Numera.Modules.Catalog` + `web/src/lib/api/catalog.ts` | Invoice line editor consumes this and **snapshots** the returned net price/unit/tax onto the line. |
| `BusinessPartner` (`PaymentTermsNetDays`, `SkontoPercent`, `SkontoDays`, `VatId`, `BillingAddress`, `Language`, `DefaultTaxCategory`, `LeitwegId`) | `Numera.Modules.Crm` | Recipient data source; payment terms drive open-item due date + Skonto. |

### New module to create
`src/modules/Numera.Modules.Sales/` — the sales-document aggregate, numbering, and open items. Mirror the existing module layout (plain entity classes implementing `ITenantEntity`, `[Table]`/`[Index]` attributes, snake-cased by the context). Endpoints, Contracts, Validators go in `Numera.Api` next to the Catalog/Partner ones. Web feature under `web/src/features/documents/` (or `invoices/`).

### Installation
None. Everything is in-solution. (E-invoicing libs ZUGFeRD-csharp/KoSIT/QuestPDF are Phase 4/5 — do not add them now.)

---

## Architecture Patterns

### Pattern 1: ONE polymorphic `sales_documents` table (RECOMMENDED — firm)

**Decision:** A single entity `SalesDocument` with a `DocumentType` discriminator and a shared `SalesDocumentLine` child, NOT separate entities per type, NOT a base+per-type hierarchy.

**Why (evidence-driven):**
- The chain conversion (Angebot → Auftragsbestätigung → Lieferschein → Rechnung) is fundamentally **copy the header + lines forward and change the type/status**. One table makes this a row-copy; separate tables make it an N×N mapping problem.
- Lines are structurally identical across all types (description, qty, unit, net price, tax category/rate, line net). A delivery note simply renders without prices; the columns still exist.
- This is how the market models it: Lexware Office / sevDesk expose "Belege" of a `type`, freely convertible, with numbering + lock applying only to the legally-relevant types. The Phase-2 `BusinessPartner` decision comment already established the same "one record, role/type flags, not divergent entities" principle (`BusinessPartner.cs` lines 11-16).
- Immutability + numbering are **conditional on type AND status**, which a discriminator column expresses cleanly in the guard trigger (`WHERE document_type IN ('rechnung','storno','gutschrift') AND status <> 'draft'`).

**Entities:**

```
sales_documents (SalesDocument : ITenantEntity)
  id                     uuid  PK (UUIDv7)
  tenant_id              uuid  NOT NULL         -- RLS
  document_type          int   NOT NULL         -- Angebot/AB/Lieferschein/Rechnung/Storno/Gutschrift
  status                 int   NOT NULL          -- Draft/Finalized/Sent/Cancelled/Paid... (see Pattern 2)
  document_number        text  NULL             -- assigned ONLY at finalize; NULL while draft
  -- chain / provenance
  source_document_id     uuid  NULL FK->sales_documents.id   -- "converted from" (copy-forward predecessor)
  corrects_document_id   uuid  NULL FK->sales_documents.id   -- Storno/Gutschrift -> the original invoice
  cancelled_by_document_id uuid NULL FK->sales_documents.id  -- original -> its Storno (back-link)
  -- partner reference + FROZEN snapshot (GoBD)
  partner_id             uuid  NULL FK->partners.id          -- reference for navigation only
  recipient_snapshot     jsonb NULL             -- frozen buyer name/address/VatId at finalize
  issuer_snapshot        jsonb NULL             -- frozen company_profile at finalize
  -- dates
  document_date          date  NOT NULL         -- Rechnungsdatum / Ausstellungsdatum (BT-2)
  service_date           date  NULL             -- Leistungsdatum (BT-72) OR period start
  service_period_end     date  NULL             -- Leistungszeitraum end (BG-14)
  due_date               date  NULL             -- computed from partner terms at finalize (BT-9)
  -- money (all decimal / numeric(19,4)); persisted, authoritative, frozen at finalize
  currency               text  NOT NULL default 'EUR'  -- BT-5
  total_net              numeric(19,4) NOT NULL  -- BT-106 sum of line nets = BT-109 tax basis (single-currency v1)
  total_tax              numeric(19,4) NOT NULL  -- BT-110 = sum of breakdown tax amounts
  total_gross            numeric(19,4) NOT NULL  -- BT-112 grand total
  amount_due             numeric(19,4) NOT NULL  -- BT-115 (= gross in v1, no prepayments)
  -- flags captured at finalize
  is_kleinunternehmer    bool NOT NULL default false   -- snapshot of issuer §19 status
  reverse_charge         bool NOT NULL default false   -- any AE line present
  buyer_reference        text NULL              -- BT-10 Leitweg-ID (from partner, B2G seam)
  notes                  text NULL              -- BT-22 free note
  finalized_at           timestamptz NULL
  sent_at                timestamptz NULL
  archived_at            timestamptz NULL       -- IArchivable, but ONLY meaningful for drafts

sales_document_lines (SalesDocumentLine : ITenantEntity)
  id, tenant_id, document_id FK
  line_number            int                    -- BT-126 ordering
  catalog_item_id        uuid NULL              -- provenance only; data below is SNAPSHOT
  name                   text NOT NULL          -- BT-153 (snapshot of catalog name/free text)
  description            text NULL
  quantity               numeric(19,6) NOT NULL -- BT-129
  unit_code              text NOT NULL          -- BT-130 (UN/ECE Rec20, e.g. C62/HUR)
  net_unit_price         numeric(19,6) NOT NULL -- BT-146
  line_net_amount        numeric(19,4) NOT NULL -- BT-131 = qty*price (allowances Phase 7)
  tax_category           int  NOT NULL          -- BT-151 (TaxCategory enum)
  vat_rate_percent       numeric(5,2) NOT NULL  -- BT-152 (0 for AE/K/E)

sales_document_tax_breakdown (SalesDocumentTaxBreakdown : ITenantEntity)   -- EN 16931 BG-23, one row per (category,rate)
  id, tenant_id, document_id FK
  tax_category           int  NOT NULL          -- BT-118
  vat_rate_percent       numeric(5,2) NOT NULL  -- BT-119
  taxable_base           numeric(19,4) NOT NULL -- BT-116
  tax_amount             numeric(19,4) NOT NULL -- BT-117 = RoundingPolicy.RoundTax(base, rate)
  exemption_reason_code  text NULL              -- BT-121 (VATEX-EU-* / VATEX-EU-AE / -IC / -O etc.)
  exemption_reason_text  text NULL              -- BT-120 (the human Pflichttext)

number_sequences (NumberSequence : ITenantEntity)   -- see Pattern 3
document_number_formats (per tenant+type config)    -- see Pattern 3
open_items (OpenItem : ITenantEntity)               -- see Pattern 5
company_profile (CompanyProfile : ITenantEntity)    -- issuer master data, see Q4
```

Delivery notes (Lieferschein) fit because prices are columns, not requirements: a Lieferschein is created with the same lines, and the **renderer** (Phase 4) decides not to show price/tax. Do not model a price-less line type.

### Pattern 2: Draft→Finalized state machine + DB-enforced immutability (status-guarded trigger)

**State machine (per document):**

```
              (upstream types: Angebot/AB/Lieferschein)
 Draft ──edit──▶ Draft ──finalize──▶ Finalized ──send──▶ Sent
   │                                     │  (Rechnung)      │
   └──archive/delete (drafts only)       ├──cancel(Storno)──▶ Cancelled
                                         └──(payment, Phase6)▶ Paid
```

- **Draft:** fully mutable, no number, may be edited/deleted/archived freely.
- **Finalize:** one-way gate → assigns number, snapshots issuer/recipient + tax breakdown, freezes content, creates open item (for invoice-like types). GoBD Unveränderbarkeit begins here.
- After finalize, the ONLY permitted mutations are a whitelist of lifecycle columns: `status` (Finalized→Sent→Cancelled/Paid), `sent_at`, `cancelled_by_document_id`, `finalized_at` (set once), later Phase-4/5 storage refs, later Phase-6 payment status. Everything else is frozen.

**DB enforcement — status-guarded trigger, NOT blanket REVOKE.** The `audit_events` table uses `REVOKE UPDATE,DELETE` because it is *purely* append-only. Sales documents are different: drafts MUST be UPDATE/DELETE-able by `numera_app`. So the correct enforcement is a **`BEFORE UPDATE/DELETE` trigger that allows the mutation only while the row is a draft (or only for whitelisted columns once finalized)**. Concrete SQL (hand-written in the migration, same style as `AuditEvents.cs`):

```sql
-- Freeze finalized invoice-like documents: block DELETE, and block UPDATE of any
-- business column once status <> draft. Only lifecycle columns may still change.
CREATE FUNCTION sales_document_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF (TG_OP = 'DELETE') THEN
    IF OLD.status <> 0 THEN   -- 0 = Draft
      RAISE EXCEPTION 'sales_documents: finalized document % is immutable (no delete)', OLD.id;
    END IF;
    RETURN OLD;
  END IF;

  -- UPDATE: drafts are freely mutable.
  IF OLD.status = 0 THEN
    RETURN NEW;
  END IF;

  -- Finalized/onwards: every FROZEN column must be unchanged. Only the whitelist
  -- (status, sent_at, cancelled_by_document_id, finalized_at once-set, + future
  -- Phase-4/5/6 storage/payment columns) may differ.
  IF NEW.document_type   IS DISTINCT FROM OLD.document_type
     OR NEW.document_number IS DISTINCT FROM OLD.document_number
     OR NEW.tenant_id     IS DISTINCT FROM OLD.tenant_id
     OR NEW.partner_id    IS DISTINCT FROM OLD.partner_id
     OR NEW.recipient_snapshot IS DISTINCT FROM OLD.recipient_snapshot
     OR NEW.issuer_snapshot    IS DISTINCT FROM OLD.issuer_snapshot
     OR NEW.document_date IS DISTINCT FROM OLD.document_date
     OR NEW.service_date  IS DISTINCT FROM OLD.service_date
     OR NEW.total_net     IS DISTINCT FROM OLD.total_net
     OR NEW.total_tax     IS DISTINCT FROM OLD.total_tax
     OR NEW.total_gross   IS DISTINCT FROM OLD.total_gross
     OR NEW.currency      IS DISTINCT FROM OLD.currency
  THEN
    RAISE EXCEPTION 'sales_documents: finalized document % is immutable (business fields frozen)', OLD.id;
  END IF;
  RETURN NEW;
END; $$;

CREATE TRIGGER sales_document_immutable
  BEFORE UPDATE OR DELETE ON sales_documents
  FOR EACH ROW EXECUTE FUNCTION sales_document_immutable();
```

Lines + tax-breakdown children must be frozen too. A line has no own status, so its trigger looks up the parent:

```sql
CREATE FUNCTION sales_document_line_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE parent_status int;
BEGIN
  SELECT status INTO parent_status FROM sales_documents
    WHERE id = COALESCE(NEW.document_id, OLD.document_id);
  IF parent_status IS DISTINCT FROM 0 THEN   -- parent not Draft
    RAISE EXCEPTION 'sales_document_lines: parent document is finalized/immutable';
  END IF;
  RETURN COALESCE(NEW, OLD);
END; $$;

CREATE TRIGGER sales_document_line_immutable
  BEFORE INSERT OR UPDATE OR DELETE ON sales_document_lines
  FOR EACH ROW EXECUTE FUNCTION sales_document_line_immutable();
-- (apply the identical guard to sales_document_tax_breakdown)
```

Note: this line trigger blocks INSERT of lines into a finalized doc too (a common attack). The tax-breakdown rows are inserted DURING finalize while status is still Draft inside the transaction — order the finalize so breakdown rows are written **before** the status flip, or exempt the breakdown insert by writing it in the same statement batch pre-flip. Simplest: within the finalize txn, insert breakdown + lines-finalization while status is still 0, then update status to 1 last. The planner must sequence the finalize unit-of-work accordingly (see Pattern 4).

App-layer guard too (defence-in-depth): the finalize/update endpoints reject mutations when `status != Draft` with a 409 before hitting the DB, so users get a clean error rather than a raw trigger exception.

### Pattern 3: Race-safe per-tenant numbering assigned at finalize (RECOMMENDED — firm)

**Counter storage** — a tenant-scoped counter row per series:

```
number_sequences (ITenantEntity)
  id, tenant_id
  doc_type      int   NOT NULL     -- Rechnung / Storno / Gutschrift (only numbered types)
  year          int   NOT NULL     -- for annual reset series; 0 = never-reset series
  next_value    bigint NOT NULL     -- next number to assign
  UNIQUE (tenant_id, doc_type, year)
```

Plus a per-tenant **format config** (configurable format is an INV-02 requirement):

```
document_number_formats (ITenantEntity)
  tenant_id, doc_type
  prefix        text   -- e.g. "RE-"
  include_year  bool   -- reset annually + embed {YYYY}
  padding       int    -- zero-pad width, e.g. 5 -> 00001
  -- rendered: {prefix}{YYYY?-}{seq:0{padding}}  => "RE-2026-00001"
```

**Atomic assignment inside the finalize transaction** — use an upsert-returning, which is a single statement that both creates the counter on first use and increments-and-returns atomically, correct under concurrency and under RLS `WITH CHECK` (the row carries `tenant_id = app.current_tenant`):

```sql
INSERT INTO number_sequences (id, tenant_id, doc_type, year, next_value)
VALUES (@id, current_setting('app.current_tenant')::uuid, @docType, @year, 2)
ON CONFLICT (tenant_id, doc_type, year)
DO UPDATE SET next_value = number_sequences.next_value + 1
RETURNING next_value - 1 AS assigned;   -- assigned = the value this txn owns
```

In EF Core 10 run this via `db.Database.SqlQuery<long>($"...")` (interpolated → parameterized) **inside** the finalize transaction (`await db.Database.BeginTransactionAsync`), then format the number with the tenant's `document_number_formats` row and set `document.document_number`. Because it is one statement under the row lock ON CONFLICT takes, two concurrent finalizations serialize on that row per tenant and cannot collide or gap-from-race. (Alternative: `FromSql("... FOR UPDATE")` load + increment + save — more round-trips; prefer the upsert-returning.)

**Hard rules (from PITFALLS.md, verified):**
- **Never a Postgres `SEQUENCE`/`SERIAL`** — non-transactional, burns numbers on rollback, not per-tenant.
- **Never `MAX(number)+1`** — read-modify-write race → duplicates under concurrency.
- **Assign only at finalize**, never at draft creation (drafts get deleted → would look like gaps / or worse force reuse).
- **Gapless myth:** German law requires **einmalig + nachvollziehbar** (unique + traceable), NOT lückenlos (gapless). `fortlaufend` ≠ no gaps. A rolled-back finalize that skips a number is legal if documented; **reusing** a number is illegal. So: do NOT build gap-back-fill or "block finalize until previous succeeds" logic. Do add a `UNIQUE (tenant_id, doc_type, document_number)` constraint so reuse is impossible.
- **Separate series per doc type** (Rechnung / Storno / Gutschrift each own counter). Whether Storno/Gutschrift share the invoice series or have their own is a legitimate config choice — recommend **separate series** (clearer audit trail; many German tools do this) but keep it a `doc_type` dimension so it is a one-line change.
- Upstream types (Angebot/AB/Lieferschein) MAY reuse the same mechanism for convenience numbering but are **not legally bound** — fine to number them at creation, or not at all in v1.
- RLS interaction: `number_sequences` is a normal `ITenantEntity` with the standard tenant policy; the upsert runs under the request's `app.current_tenant`, so the counter is inherently per-tenant and isolated. Add the `(tenant_id, doc_type, year)` unique index (which is also the tenant-leading access index).

### Pattern 4: Finalize = one transaction, side-effects enqueued after commit

The finalize command (the single most important transaction in v1) does, in order, inside one `BeginTransactionAsync`:

1. Re-load the draft **tracked**; assert `status == Draft` (app-layer guard → 409 otherwise).
2. Validate completeness (§14 fields present — see Q4; at least one line; recipient has address; if any AE/K line, recipient `VatId` present; etc.).
3. Snapshot `issuer_snapshot` (from `company_profile`) and `recipient_snapshot` (from `partner` + billing address) onto the document as jsonb.
4. Compute money: line nets, group into `TaxBucket`s by (category, rate), `RoundingPolicy.RoundTax` per bucket → persist `sales_document_tax_breakdown` rows; `total_tax = RoundingPolicy.DocumentVatTotal(buckets)`; `total_net = Σ line_net`; `total_gross = total_net + total_tax`; `amount_due = total_gross`.
5. Assign `document_number` via the upsert-returning counter (Pattern 3) + format config.
6. Compute `due_date` from partner payment terms; create the `open_items` row (Pattern 5).
7. Flip `status = Finalized`, set `finalized_at` **last** (so the line/breakdown inserts in step 4 happen while status is still Draft and pass the child trigger).
8. `audit.RecordAsync("sales_document.finalized", ...)`.
9. Single `SaveChangesAsync` + commit.
10. **After commit**, enqueue side-effects (PDF render Phase 4, e-invoice Phase 5, email) and dispatch the `InvoiceFinalized` domain event to in-process handlers. In v1 there is no worker tier yet — the event/enqueue is a seam; the open-item + audit are done inline in the txn, not via the event.

This matches ARCHITECTURE.md Pattern 4 (Command → Domain txn → enqueue) and the existing Catalog endpoint's mutate→audit→single-SaveChanges idiom.

### Pattern 5: Open items created atomically at finalize (OPDN-01)

```
open_items (OpenItem : ITenantEntity)
  id, tenant_id
  document_id       uuid FK->sales_documents.id   -- the finalized invoice
  partner_id        uuid FK
  document_number   text            -- denormalized for the OP list
  currency          text
  original_amount   numeric(19,4)   -- gross at issue
  open_amount       numeric(19,4)   -- reduced by payments in Phase 6; = original at issue
  status            int             -- Open / PartiallyPaid / Paid / Cancelled
  issued_on         date
  due_date          date            -- = document_date + partner.PaymentTermsNetDays (default e.g. 14 if null)
  skonto_percent    numeric(5,2) NULL   -- snapshot of partner.SkontoPercent
  skonto_days       int NULL
  skonto_due_date   date NULL       -- document_date + skonto_days
  skonto_amount     numeric(19,4) NULL  -- RoundingPolicy-consistent discount preview
```

- Created in the finalize txn (in-process, same transaction — ARCHITECTURE.md "Sales ↔ Open Items: same transaction").
- **Phase-6 seam:** payments/dunning are deferred. Design so a future `payments` table reduces `open_amount` and flips `status`; do NOT build payment recording now. The `open_amount` + `status` columns are the seam.
- **Storno/Gutschrift:** when an invoice is cancelled, close/negate its open item (set `status = Cancelled` or create an offsetting negative open item). Recommend: on Storno, set the original invoice's open item `status = Cancelled` and `open_amount = 0`; the Storno document itself (negative gross) does not create a positive receivable. Keep this rule explicit in the plan.
- OP-Übersicht = a list endpoint + `DataTable` page over `open_items` with due-date + status columns.

### Pattern 6: Storno + Gutschrift as typed correction documents (INV-03) — VERIFIED

German B2G guidance (e-rechnung-bund.de) confirms the EN 16931 encoding:

| Concept | `DocumentType` | EN 16931 BT-3 code | Modeling |
|---------|---------------|--------------------|----------|
| Rechnung (commercial invoice) | Rechnung | **380** | positive amounts |
| **Stornorechnung** (cancellation of a specific invoice) | Storno | **384** (corrected invoice) | mirrors the original with **negative** amounts; references original via `corrects_document_id` → EN 16931 PRECEDING INVOICE REFERENCE (BG-3: BT-25 number, BT-26 date) |
| **Gutschrift / Rechnungskorrektur** (commercial credit note) | Gutschrift | **381** (credit note) | positive amounts on a credit note; "no extra minus sign on the total" per the FAQ; references original |

- Each correction gets **its own number** from its own series (Pattern 3), references the original (`corrects_document_id`), and is itself finalized + immutable.
- The **original invoice stays immutable**; only its whitelisted lifecycle columns change: `status → Cancelled` and `cancelled_by_document_id → storno.id` (both in the trigger whitelist). Correcting = new document, never in-place edit.
- German terminology trap to note for the planner: "Gutschrift" is overloaded — the **kaufmännische Gutschrift** (commercial credit note) = code **381**, versus the VAT-law **Gutschrift** = self-billed invoice = code **389** (a *buyer* issuing the invoice). v1 handles only the commercial credit note (381). Do not conflate.
- Storno amount sign: model the Storno as a full mirror (negative line nets, negative totals, negative open-item effect). This keeps the correction self-describing and ledger-ready.

### Anti-Patterns to avoid (all confirmed in PITFALLS.md)
- Editing/deleting a finalized document (GoBD violation) — blocked by the trigger AND the app guard.
- Postgres `SEQUENCE`/`MAX()+1` for numbers.
- Building gapless-guarantee logic (chases a non-requirement, risks illegal number reuse).
- A single `vat_rate` column instead of per-line `TaxCategory + rate + document breakdown`.
- Kleinunternehmer invoices that show a VAT line.
- Referencing the live catalog item on an issued line instead of snapshotting (a later catalog price edit would mutate an issued invoice — GoBD).
- Rounding the grand total instead of summing per-category rounded tax.
- Forgetting the RLS policy on any of the new tables (silent cross-tenant leak).

---

## Don't Hand-Roll

| Problem | Don't build | Use instead |
|---------|-------------|-------------|
| VAT rounding / document VAT total | ad-hoc `Math.Round(gross)` | `RoundingPolicy.RoundTax` + `RoundingPolicy.DocumentVatTotal(buckets)` (already correct per BR-S-08 / BR-CO-14/17) |
| Money type | a new struct | `Numera.Platform.Money.Money` + raw `decimal` for intermediates |
| Audit rows | a bespoke history table | `IAuditWriter.RecordAsync` inside the finalize txn |
| Tenant isolation | app-only `WHERE tenant_id` | FORCE RLS migration SQL (copy the `Crm.cs` loop) + `ITenantEntity` (auto filter + index) |
| Immutability | app-only status checks | status-guarded Postgres trigger (Pattern 2) + app guard as UX |
| Numbering concurrency | `SELECT MAX+1` / lock objects | upsert-returning counter row (Pattern 3) |
| Duplicate-number errors → 500 | try/catch guesswork | `UNIQUE (tenant, doc_type, number)` → catch `PostgresErrorCodes.UniqueViolation` → 409 (mirror `CatalogEndpoints.IsDuplicateNumber`) |
| Catalog line data | live FK read at render | snapshot onto the line at creation (the `CatalogLineItem` picker exists precisely for this) |

---

## Common Pitfalls (phase-specific)

### Pitfall 1: Child-row triggers firing during finalize
**What goes wrong:** the line/tax-breakdown immutability trigger rejects the inserts that finalize itself performs.
**Avoid:** insert lines/breakdown while parent `status` is still `Draft`, flip status **last** in the same transaction (Pattern 4 step 7). The child trigger keys off parent status.

### Pitfall 2: Issuer/recipient not snapshotted → later master-data edit mutates an issued invoice
**Why it happens:** natural instinct is to render from the live `partner`/`company_profile`. GoBD forbids this — the invoice must reflect data *as issued*.
**Avoid:** freeze `issuer_snapshot` + `recipient_snapshot` (jsonb) at finalize; render from the snapshot, not the live rows.

### Pitfall 3: Kleinunternehmer still shows VAT
**Why:** the §19 flag lives on the issuer (`company_profile`), not the line. If finalize doesn't read it, lines compute 19%.
**Avoid:** if `company_profile.is_kleinunternehmer`, force all lines to category `E`, rate 0, zero tax, emit the mandatory §19 note, and set `is_kleinunternehmer` on the doc. Golden-file test this.

### Pitfall 4: Reverse-charge / intra-EU without recipient VAT-ID or note
**Why:** AE (§13b) and K (innergemeinschaftlich) require the recipient's `VatId` and a mandatory Pflichttext; the happy path skips both.
**Avoid:** finalize validation blocks an AE/K document whose recipient has no `VatId`; the tax-breakdown row carries the exemption code (BT-121) + text (BT-120). See VAT note table below.

### Pitfall 5: Missing RLS policy on a new table
**Why:** reflective discovery creates the table + tenant index + query filter but NOT the RLS policy.
**Avoid:** hand-write `ENABLE + FORCE + CREATE POLICY tenant_isolation` for **every** new table (`sales_documents`, `sales_document_lines`, `sales_document_tax_breakdown`, `number_sequences`, `document_number_formats`, `open_items`, `company_profile`) in the migration, exactly like the `Crm.cs` foreach loop. One RLS test per table.

### Pitfall 6: `document_number` unique constraint vs the gapless myth
**Why:** teams either allow duplicates (illegal) or over-engineer gaplessness.
**Avoid:** `UNIQUE (tenant_id, doc_type, document_number) WHERE document_number IS NOT NULL` (partial — drafts have NULL numbers). No gap logic.

---

## §14 UStG Mandatory Fields — checklist + data source (Q4)

Verified against §14 UStG (gesetze-im-internet.de) + IHK/Handelskammer checklists (2026). Each field mapped to where it comes from:

| §14 field | Source | Status |
|-----------|--------|--------|
| Full name + address of **issuer** (Leistender) | `company_profile` | **MISSING — must add** |
| Full name + address of **recipient** (Leistungsempfänger) | `BusinessPartner` + `BillingAddress` | exists (snapshot at finalize) |
| Issuer **USt-IdNr OR Steuernummer** (exactly one required) | `company_profile` | **MISSING — must add** |
| **Invoice number** (fortlaufend, einmalig) | numbering (Pattern 3) | build here |
| **Ausstellungsdatum** (BT-2) | `document_date` | build here |
| **Menge + Art** of goods / scope of service | lines (`quantity`, `name`, `unit_code`) | build here |
| **Leistungsdatum / Leistungszeitraum** (Zeitpunkt der Lieferung/Leistung) | `service_date` / `service_period_end` | build here (may equal document_date with a note) |
| **Net amount per tax rate** (nach Steuersätzen aufgeschlüsselt) | tax breakdown (BG-23) | build here |
| **Tax rate + tax amount** per rate (or exemption note) | tax breakdown | build here |
| **Gross / amount due** | `total_gross` / `amount_due` | build here |
| Any pre-agreed **Entgeltminderung** (discounts) | lines/allowances | Phase 7 (allowances) — seam only |
| **Kleinunternehmer §19** note (when applicable) | `company_profile.is_kleinunternehmer` | **MISSING — must add** |
| Reverse-charge / intra-EU **Pflichttext** | tax breakdown exemption text | build here |

**New `company_profile` table (issuer master data) — recommended (firm):**

```
company_profile (CompanyProfile : ITenantEntity)   -- 1:1 with tenant
  id, tenant_id (UNIQUE)
  legal_name            text NOT NULL      -- BT-27 Seller name
  address (owned Address, reuse Crm.Address value object OR a local copy)  -- BG-5
  vat_id                text NULL          -- BT-31 USt-IdNr
  tax_number            text NULL          -- Steuernummer (national)  [exactly one of vat_id/tax_number required by validation]
  is_kleinunternehmer   bool NOT NULL default false   -- §19 → suppress VAT
  -- default invoice settings
  default_payment_terms_net_days int NULL
  default_tax_category  int NULL
  -- bank (BG-16 / payment means seam; Phase 4/6)
  iban, bic, bank_name  text NULL
  -- registry / contact (nice-to-have on the printed invoice, Phase 4)
  register_court, register_number, managing_director, contact_email, contact_phone  text NULL
  logo_ref              text NULL          -- Phase 4 seam
```

Why a new table, not columns on `tenants`: `tenants` is deliberately lean and is the RLS *self-scoped* special table (no `tenant_id`); issuer master data will keep growing (multiple bank accounts, logo, Briefpapier). A tenant-scoped `company_profile` (standard RLS) is cleaner and keeps `tenants` as the pure isolation anchor. Note the `Address` value object lives in `Numera.Modules.Crm`; either reference it or define a local owned `Address` in Sales to avoid a module dependency (recommend a small local owned type — modules shouldn't depend on each other).

---

## VAT Category Modeling + mandatory notes (Q5) — EN 16931 BG-23 verified

Per line: `TaxCategory` (BT-151) + `vat_rate_percent` (BT-152). Per document: a persisted `sales_document_tax_breakdown` (BG-23), one row per distinct (category, rate), each carrying BT-116 base, BT-117 tax, BT-118 category, BT-119 rate, BT-120 exemption text, BT-121 exemption code. Compute via a **`VatCalculationService`** that takes the lines, buckets them into `TaxBucket`s, and calls `RoundingPolicy` — do not compute VAT inline in the endpoint.

Scenario → category → rate → mandatory note (Pflichttext). Note texts are legally load-bearing; store BOTH a code (BT-121) and the rendered text (BT-120):

| Scenario | Category (BT-118) | Rate | Mandatory note (BT-120, DE) | Exemption code (BT-121) |
|----------|-------------------|------|------------------------------|--------------------------|
| Standard 19% | `S` | 19 | — | — |
| Reduced 7% | `S` | 7 | — | — |
| Zero-rated | `Z` | 0 | — | — |
| §13b Reverse charge (incl. Bauleistungen) | `AE` | 0 | "Steuerschuldnerschaft des Leistungsempfängers (§13b UStG)" | `VATEX-EU-AE` |
| Innergemeinschaftliche Lieferung | `K` | 0 | "Steuerfreie innergemeinschaftliche Lieferung (§4 Nr. 1b i.V.m. §6a UStG)" — requires recipient USt-IdNr | `VATEX-EU-IC` |
| Kleinunternehmer §19 | `E` | 0 | "Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG" | `VATEX-EU-D` (verify) |
| Ausfuhr / export | `G` | 0 | "Steuerfreie Ausfuhrlieferung (§4 Nr.1a i.V.m. §6 UStG)" | `VATEX-EU-G` |
| Nicht steuerbar | `O` | — | — | (category O has no rate) |

- **Kleinunternehmer** is document-wide (issuer property), not per-line: it forces the whole document to `E`/0 and suppresses VAT entirely.
- **AE/K validation gate at finalize:** if any line is `AE` or `K`, the recipient must have a `VatId` (block finalize otherwise).
- Confidence: category codes + note requirements HIGH; the exact `VATEX-EU-*` code strings MEDIUM — verify against the EN 16931 VATEX code list during Phase 5 (XML generation) but store the column now so it's a data fill, not a schema change. The human note text is the legally required part on the v1 (Phase-4) PDF; the code is the Phase-5 XML concern.

---

## Ledger-readiness (Q8) — recommendation

**Minimum that avoids a Phase-3.5 rewrite, without building bookkeeping:**
- Define typed domain event records: `InvoiceFinalized(documentId, tenantId, breakdown, gross, ...)`, `InvoiceCancelled`, `CreditNoteIssued`.
- Add a tiny in-process publisher seam (a `IDomainEventHandler<T>` list resolved from DI, invoked **after** the finalize `SaveChanges` succeeds — or a captured list dispatched post-commit). Do NOT add MediatR (it went commercial per STACK.md).
- In v1 the open-item creation + audit are done **inline in the txn** (they must be atomic), so they do NOT go through the event. The event exists so the future ledger `IPostingSource` implementation and the Phase-5 e-invoice job can subscribe without touching invoice internals — this is the seam ARCHITECTURE.md flags as "the single most likely cause of a later rewrite" if skipped.
- **Do NOT** write inert `Posting`/`JournalEntry` rows in Phase 3. Keep the ledger tables empty; just ensure the data postings will need (booking date = `document_date`, per-rate tax breakdown, `partner.DebtorAccount` / `catalog.RevenueAccount` hints) is captured — it already is.

This is a discretion call (MEDIUM). If the planner wants the absolute floor: at minimum raise an `InvoiceFinalized` object even if the only v1 subscriber is a no-op/log — the cost is trivial and the seam is then real. Recommended: implement the small publisher.

---

## API + Frontend (Q9)

**Endpoints (minimal-API, `Numera.Api/Endpoints/SalesDocumentEndpoints.cs`, `.RequireAuthorization()`, mutate→audit→single-SaveChanges idiom):**

```
GET    /api/documents                      list (type/status filter, paged, DataTable manualPagination)
GET    /api/documents/{id}                 detail (header + lines + breakdown + chain links)
POST   /api/documents                      create draft (type, partner, lines)
PUT    /api/documents/{id}                 update draft  (409 if not Draft)
DELETE /api/documents/{id}                 delete draft  (409 if not Draft; trigger also blocks)
POST   /api/documents/{id}/convert         copy-forward → new draft of target type (Angebot→AB→LS→Rechnung)
POST   /api/documents/{id}/finalize        THE transaction (assign number, freeze, open item, audit, event)
POST   /api/documents/{id}/storno          create + finalize a Storno (384) referencing original; cancel original
POST   /api/documents/{id}/credit-note     create a Gutschrift (381) draft referencing original
GET    /api/open-items                     OP-Übersicht (due date, status, paged)
GET    /api/company-profile  / PUT         issuer master data (§14 issuer fields)
```

- Contracts in `Numera.Api/Contracts` (record DTOs, camelCased on the wire; note enums serialize as **numbers** — the web client mirrors ordinals, as `catalog.ts` documents).
- Validators in `Numera.Api/Validators` (FluentValidation, auto-scanned): draft validity, finalize completeness (§14), AE/K requires recipient VatId, at least one line, positive quantities.
- **Line editor** consumes `lookupCatalogItems(q)` (existing picker) and **snapshots** `name/unitCode/netPrice/taxCategory/vatRatePercent` onto the line — the line is thereafter independent of the catalog. Free-text (non-catalog) lines allowed (`catalog_item_id` NULL).
- **Web** (`web/src/features/documents/`): RHF + zod document editor with a `useFieldArray` dynamic line list; compute **live totals client-side for preview only** (using the same per-category-round-then-sum logic) — the **server recomputes authoritatively** at save/finalize and its numbers win. List pages reuse the shared `DataTable` (`translationNs="documents"`). New i18n namespaces: `documents.json` (+ maybe `openItems.json`) under `web/src/i18n/locales/{de,en}/`.

---

## Testing (Q10) — mirror Phase-1 approaches

| Test | What it proves | How |
|------|----------------|-----|
| **Immutability integration** | finalized doc UPDATE/DELETE blocked at DB level, as `numera_app` | Testcontainers Postgres 18, connect as `numera_app` (no BYPASSRLS), finalize a doc, assert UPDATE of a frozen column and DELETE both raise; assert draft UPDATE succeeds; assert line INSERT into finalized parent raises. Mirror the `audit_events` append-only test. |
| **Concurrent-finalize numbering** | no duplicate numbers under parallel finalize | Spin N parallel finalize calls for one tenant (Testcontainers, mirror the 01-08 concurrency test), assert N distinct sequential numbers, zero duplicates, `UNIQUE` constraint never trips. |
| **VAT golden-file** | mixed 19/7/0, §13b (AE), intra-EU (K), Kleinunternehmer (E) produce correct base/tax/breakdown + notes | Extend the Phase-1 Money golden files: feed known line sets → assert per-category base/tax (per-category rounding) + document totals + note text + exemption code. |
| **RLS per new table** | tenant A cannot see/write tenant B rows | One test per new table (hard CI gate), copying the existing RLS test pattern. |
| **Copy-forward** | convert preserves lines + sets `source_document_id` + status | Unit/integration on the convert endpoint. |
| **Storno** | Storno gets its own number, references original, negates amounts, flips original→Cancelled + back-link | Integration. |
| **Open item on finalize** | finalize creates an open item with correct due date from partner terms | Integration. |

---

## Open Questions

1. **Storno/Gutschrift series sharing.** Whether Storno/Gutschrift share the Rechnung number series or have separate series is a legal-preference choice. Recommendation: **separate series** (cleaner audit trail) — but modeled as a `doc_type` dimension so it's a config flip. Confirm with the product owner if a Steuerberater has a preference. LOW risk either way.
2. **Exact VATEX-EU exemption codes (BT-121).** The category codes (AE/K/E/...) and the German note texts (BT-120) are HIGH confidence; the precise `VATEX-EU-*` strings are MEDIUM. Store the column now; finalize the exact codes in Phase 5 (XML generation) against the official VATEX code list — this is a data fill, not a schema change.
3. **Company profile: extend `tenants` vs new table.** Recommended a new `company_profile` table (rationale above). If the planner prefers columns on `tenants`, RLS self-scoping still works, but the table will bloat and `tenants` loses its "pure isolation anchor" clarity. Recommendation stands: new table.
4. **Domain-event ambition (Q8).** Full in-process publisher vs bare `InvoiceFinalized` object. Recommended the small publisher; the floor is a raised event object. Either satisfies ledger-readiness; the rewrite risk is only if NO event/seam exists at all.
5. **Number reset boundary.** Annual reset (`{YYYY}` + year in the counter key) is the common German default and is modeled above. If a tenant wants a never-reset continuous series, use `year = 0`. Both supported by the `document_number_formats.include_year` flag.

---

## Sources

### Primary (HIGH)
- Existing code (authoritative for all platform patterns): `src/platform/Numera.Platform.Money/{Money,TaxCategory,RoundingPolicy}.cs`, `src/platform/Numera.Platform.Db/NumeraDbContext.cs`, `.../Migrations/{AuditEvents,Crm}.cs` (RLS + append-only trigger patterns), `src/platform/Numera.Platform.Audit/AuditWriter.cs`, `src/modules/Numera.Modules.{Crm/BusinessPartner,Catalog/CatalogItem,Ledger/*}.cs`, `src/Numera.Api/Endpoints/CatalogEndpoints.cs`, `web/src/lib/api/catalog.ts`.
- `.planning/research/{ARCHITECTURE,PITFALLS,STACK}.md` (written for this phase).
- §14 UStG — gesetze-im-internet.de/ustg_1980/__14.html; IHK/Handelskammer Hamburg 2026 Pflichtangaben checklists.
- EN 16931 invoice type codes (BT-3 / UNTDID 1001): e-rechnung-bund.de FAQ "How should I indicate credits and invoice corrections?" (380 invoice / 381 credit note / 384 corrected / 389 self-billed; BG-3 preceding invoice reference), Vertex XRechnung invoice-type docs, invoice-converter.com BT-3.

### Secondary (MEDIUM)
- EF Core 10 raw SQL / pessimistic locking: dotnet/efcore #26042, npgsql/efcore.pg #2049, milanjovanovic.tech pessimistic-locking (`FromSql ... FOR UPDATE`, `SqlQuery`, transactions not automatic). Used to confirm the upsert-returning / FOR-UPDATE options; the recommended upsert-returning is a single-statement pattern independent of EF locking APIs.
- EN 16931 BG-23 / BT-116..121 field semantics, VATEX exemption codes — invoicenavigator / ConnectingEurope EN16931 model (BT-121 exact strings flagged MEDIUM, to re-verify Phase 5).

## Metadata
**Confidence breakdown:**
- Document model / immutability / numbering: HIGH — grounded in existing code patterns (audit trigger, RLS loop, catalog snapshot seam) + verified law.
- VAT categories / §14 fields / type codes: HIGH on structure and requirements; MEDIUM on exact VATEX code strings (Phase-5 concern).
- Ledger-readiness / domain events: MEDIUM — a discretion call; recommendation given with a stated floor.

**Research date:** 2026-07-12
**Valid until:** ~30 days (stable domain; EN 16931 / §14 UStG are slow-moving; re-verify XRechnung version specifics at Phase 5).
