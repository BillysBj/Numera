# Phase 10: Buchhaltungs-Fundament - Research

**Researched:** 2026-08-03
**Domain:** German double-entry bookkeeping (Doppik) — chart of accounts (SKR03/04), balanced append-only journal, auto-posting from immutable invoice/payment facts, Festschreibung/period-lock
**Confidence:** HIGH on codebase seams + immutability/RLS patterns (read from source this session); HIGH on SKR/Steuerschlüssel mapping (DATEV verified); MEDIUM on per-fiscal-year USt-VA Kennziffer specifics (that lands in the Reports/USt-VA phase, not here)

## Summary

The Ledger module is **not a greenfield** — `Numera.Modules.Ledger` already ships three inert entities (`Account`, `JournalEntry`, `Posting`) with tables created and RLS (ENABLE+FORCE+`tenant_isolation` policy) live since the InitialPlatform migration. What is missing is everything that makes it a real ledger: richer account metadata (SKR chart, Steuerschlüssel, Automatikkonto, Kennziffer), the tenant setup that picks SKR03/SKR04, the balanced-per-entry DB invariant, the append-only immutability trigger + REVOKE (the ledger tables today have RLS but **no** immutability enforcement, unlike `payment`/`sales_documents`), a `fiscal_periods` lock table, and the auto-posting engine that projects the already-immutable `SalesDocument`/`Payment` facts into journal entries.

The good news: every enforcement pattern this phase needs is **already proven in v1 and copy-ready**. The `payment`/`payment_allocation` migration gives a verbatim REVOKE + immutability-trigger template; the `sales_documents` migration gives a status-guarded whitelist trigger that is the exact model for Festschreibung/period-lock; the `TenantConnectionInterceptor` + reflective `ITenantEntity` discovery means new ledger tables get RLS + query filters for free. The domain-event seam (`IDomainEventPublisher`, `InvoiceFinalized`) is wired and already consumed by `EnqueuePdfOnFinalize` — the invoice→booking hook slots in the same way. The account-mapping seams (`CompanyProfile.RevenueAccount/DebtorAccount/CreditorAccount`, `BusinessPartner.DebtorAccount/CreditorAccount`, `Product.RevenueAccount`) already exist as nullable columns.

The one real gap on the seam side: **`PaymentService.RecordAsync`/`ReverseAsync` do NOT publish any domain event today** — so the payment→booking hook must add a publish (or post inline in the same transaction). Invoices publish; payments do not yet.

**Primary recommendation:** Extend `Numera.Modules.Ledger` in place. Add account metadata + a per-tenant `ledger_settings` (SKR variant, Besteuerungsart, Gewinnermittlungsart) + `fiscal_periods`. Seed SKR03/SKR04 from a checked-in dataset keyed to the Steuerschlüssel table. Enforce balance + immutability + period-lock as DB triggers copied from the `payment`/`sales_documents` templates. Auto-post synchronously **inside** the existing finalize/payment transactions (atomicity > the post-commit event seam) and reserve the domain event for non-critical side-effects.

## Locked Upstream Decisions (from milestone research — do NOT re-litigate)

From `.planning/research/{SUMMARY,STACK,ARCHITECTURE,PITFALLS}.md` (HIGH confidence):

- **No new bookkeeping library.** Double-entry is a domain/SQL problem — extend `Numera.Modules.Ledger`.
- **Seed SKR03/SKR04 yourself** (DATEV publishes only PDF/Excel, no CSV/API). Checked-in, versioned per fiscal year.
- **Booking is an automatic PROJECTION** of the already-immutable invoice/payment facts — reuse existing domain-event / `PaymentService` seams, **no double data entry**.
- **DB-enforced GoBD:** append-only journal, `REVOKE UPDATE/DELETE` from `numera_app`, immutability trigger, balanced-per-entry invariant, period lock. Corrections = Stornobuchung, never edit.
- **RLS ENABLE+FORCE + `tenant_isolation` policy on every new table**; money stays `decimal`; reuse NodaMoney + `RoundingPolicy`.
- **Out of scope this phase:** EÜR/GuV/BWA reports, USt-VA calculation/export, Kassenbuch, banking, Belege. Those are later phases. This phase is chart + journal + auto-posting + Festschreibung ONLY (ACCT-01..06, ACCT-09). ACCT-05 (incoming invoice/expense posting) is in-scope as the posting *rule*, but the Beleg capture pipeline that feeds it is a later phase — model the posting path, don't build OCR/intake.
- **Ist- vs Soll-Versteuerung** and **Gewinnermittlungsart (EÜR/Bilanz)** are per-tenant settings that must be *captured* here (B1) even though they are *consumed* by the later USt-VA/reports phase.

## Standard Stack

### Core (all already in the solution — nothing new to install)

| Component | Where | Purpose | Why standard |
|-----------|-------|---------|--------------|
| `Numera.Modules.Ledger` | `src/modules/Numera.Modules.Ledger` | Home of `Account`/`JournalEntry`/`Posting` + the new engine | Already the designated seam; entities are reflectively discovered as `ITenantEntity` |
| `NumeraDbContext` | `src/platform/Numera.Platform.Db` | EF Core 10 context; reflective `ITenantEntity` registration, snake_case, named query filters (`Tenant`, `NotArchived`) | New ledger entities auto-register + get a `(tenant_id, id)` index + tenant query filter; table names hand-mapped in `ConfigureLedgerTableNames` |
| `TenantConnectionInterceptor` | `src/platform/Numera.Platform.Tenancy` | Sets `app.current_tenant` GUC per connection (parameterised), RESET on close | The RLS backbone; jobs re-establish via `ICurrentTenant.SetTenant` |
| `IDomainEventPublisher` / `IDomainEventHandler<T>` / `InProcessDomainEventPublisher` | `src/modules/Numera.Modules.Sales/Events` | Post-commit side-effect dispatch (NO MediatR) | Already consumed by `EnqueuePdfOnFinalize`/`EnqueueEInvoiceOnFinalize` |
| `RoundingPolicy` + `Money` + NodaMoney | `src/platform/Numera.Platform.Money` | EN-16931 per-category rounding, `decimal` money | Already used by the VAT breakdown; reuse verbatim |
| `IAuditWriter` / `AuditWriter` | `src/platform/Numera.Platform.Audit` | Append-only audit-log seam | Booking should write an audit event like `PaymentService` does |
| Hangfire (Api default queue) | `src/Numera.Api/Jobs` | Background jobs re-establishing tenant via `SetTenant` | Only if any posting work is deferred; posting itself should be synchronous |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Post synchronously inside finalize/payment tx | Post via the post-commit `InvoiceFinalized` handler (like PDF) | Post-commit is non-atomic: a crash after commit but before posting leaves invoice without a booking. Booking is a **legal projection**, so atomicity wins — post inside the tx. Keep the event for truly optional side-effects. |
| Signed-amount postings | Current `Amount>=0` + `PostingDirection` enum | Current model already chose non-negative amount + Debit/Credit direction. Keep it; the balance invariant is `Σ(debit amounts)=Σ(credit amounts)`. |
| Seed SKR as data rows | Compute accounts on the fly | GoBD/Steuerberater expect a real, auditable chart — seed rows. |

**Installation:** none. `dotnet ef migrations add LedgerEngine` after entity changes, built with the user-local .NET 10 SDK (see MEMORY: dotnet-10-sdk-path — do not use the PATH default .NET 8).

## Architecture Patterns

### What exists vs what this phase adds

```
Numera.Modules.Ledger/            EXISTS (inert)              PHASE 10 ADDS
  Account.cs          Id,TenantId,Number,Name,Type    →  + ChartVariant(SKR03/04), Steuerschluessel,
                                                          IsAutomatikkonto, UstvaKennziffer, IsActive,
                                                          ParentNumber?, (EÜR/GuV mapping seam)
  JournalEntry.cs     Id,TenantId,EntryDate,           →  + JournalNumber (gapless, at Festschreibung),
                      SourceRef,Description,Postings      PeriodId FK, PostingType (normal/storno),
                                                          ReversesEntryId?, SourceType (invoice/payment/expense),
                                                          IsPosted/FestgeschriebenAt
  Posting.cs          Id,TenantId,JournalEntryId,      →  + Steuerschluessel?, TaxRatePercent?, TaxCategory?
                      AccountId,Amount(19,4),Direction    (so USt-VA can be driven off account+key later)
  IPostingSource.cs   BuildPostings()                  →  implemented by the posting engine

  (NEW) LedgerSettings.cs         per-tenant: ChartVariant, Besteuerungsart(Ist/Soll),
                                  Gewinnermittlungsart(EÜR/Bilanz), FiscalYearStart
  (NEW) FiscalPeriod.cs           tenant, year, month/quarter, Status(Open/Locked), LockedAt
  (NEW) posting engine (InvoicePostingSource, PaymentPostingSource, ExpensePostingSource)
  (NEW) account-resolution service (SKR + VAT category + entity overrides → account + Steuerschluessel)
```

### Pattern 1: RLS on every new table (copy verbatim from Payments migration)

```sql
-- Source: src/platform/.../Migrations/20260727205739_Payments.cs (lines 57-65)
ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <t> FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON <t>
  USING (tenant_id = current_setting('app.current_tenant')::uuid)
  WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
```
The existing `accounts`/`journal_entries`/`postings` tables **already have this** (InitialPlatform lines 137-145). New tables (`ledger_settings`, `fiscal_periods`) must add it. Reflective discovery gives the EF query filter + `(tenant_id,id)` index automatically; **RLS SQL is always hand-written in the migration** (never auto-emitted).

### Pattern 2: Append-only immutability + REVOKE (copy verbatim from Payments migration)

```sql
-- Source: src/platform/.../Migrations/20260727205739_Payments.cs (lines 67-87)
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
    REVOKE UPDATE, DELETE ON journal_entries FROM numera_app;
    GRANT  INSERT, SELECT  ON journal_entries TO   numera_app;
    -- same for postings
  END IF;
END $$;

CREATE FUNCTION journal_entries_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'journal rows are append-only (GoBD); reverse, do not edit/delete'; END; $$;
CREATE TRIGGER journal_entries_immutable BEFORE UPDATE OR DELETE ON journal_entries
  FOR EACH ROW EXECUTE FUNCTION journal_entries_immutable();
-- same for postings
```
⚠ The current `accounts`/`journal_entries`/`postings` tables have **RLS but NOT this REVOKE/trigger** — the InitialPlatform migration created them inert. Phase 10 must add it. Note `accounts` is **mutable** (chart is editable master data), but `journal_entries`/`postings` are strictly append-only once posted.

### Pattern 3: Status-guarded whitelist trigger → the model for Festschreibung/period-lock

The `sales_documents` two-state trigger is the exact template: freely mutable while `status=0` (Draft), frozen after, with a whitelist of lifecycle columns and `IS DISTINCT FROM` comparisons (NULL-safe).

```sql
-- Source: src/platform/.../Migrations/20260712151258_SalesDocuments.cs (lines 259-290)
CREATE FUNCTION sales_document_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF (TG_OP = 'DELETE') THEN
    IF OLD.status <> 0 THEN RAISE EXCEPTION '...immutable (no delete)'; END IF;
    RETURN OLD;
  END IF;
  IF OLD.status = 0 THEN RETURN NEW; END IF;          -- draft: freely mutable
  IF NEW.document_number IS DISTINCT FROM OLD.document_number
     OR NEW.total_gross  IS DISTINCT FROM OLD.total_gross
     -- ...business columns frozen; only whitelisted lifecycle cols may change
  THEN RAISE EXCEPTION '...business fields frozen'; END IF;
  RETURN NEW;
END; $$;
```

**For the period lock (ACCT-09):** a `journal_entries` INSERT trigger looks up the `fiscal_period` for `NEW.entry_date`'s tenant+month and RAISEs if that period's `status = Locked` (mirrors the `sales_document_child_immutable` parent-status lookup at lines 301-306). This is what hard-blocks a booking into a festgeschriebene Periode.

### Pattern 4: Balanced-per-entry invariant (NEW — the one genuinely new trigger)

A `CONSTRAINT TRIGGER ... DEFERRABLE INITIALLY DEFERRED` on `postings` (or a statement trigger firing at commit) that, per affected `journal_entry_id`, asserts:
```sql
SELECT
  COALESCE(SUM(amount) FILTER (WHERE direction = 1), 0)   -- Debit/Soll
= COALESCE(SUM(amount) FILTER (WHERE direction = 2), 0)   -- Credit/Haben
FROM postings WHERE journal_entry_id = <entry>;
```
Deferred so multi-line inserts within one transaction are checked once at commit, not after each row. `PostingDirection.Debit = 1`, `Credit = 2` (from `Posting.cs`). Enforce the same invariant in the domain layer before SaveChanges (defence-in-depth), with golden-file tests for split VAT (19%+7% in one booking), reverse-charge, and multi-open-item payments — see Pitfall 1.

### Pattern 5: Auto-posting hook — invoices publish, payments do NOT

**Invoice → booking (ACCT-03):** `SalesDocumentEndpoints` finalize runs `FinalizeCoreAsync` inside a transaction and, **after commit**, calls `publisher.PublishAsync(new InvoiceFinalized(...))`. `InvoiceFinalized` carries `TenantId, DocumentId, DocumentNumber, TotalNet, TotalTax, TotalGross, DocumentDate, DocumentType`. Two options:
- **(recommended) Post inside `FinalizeCoreAsync`** (add a call after the breakdown is written, before/with the status flip) so the booking is atomic with the invoice. The VAT breakdown rows (`SalesDocumentTaxBreakdown`: `TaxCategory`, `VatRatePercent`, `TaxableBase`, `TaxAmount`) are already computed there — perfect posting input.
- (alt) Register `PostInvoiceOnFinalize : IDomainEventHandler<InvoiceFinalized>` like `EnqueuePdfOnFinalize`. Simpler wiring but non-atomic (see Alternatives). If chosen, the booking must be idempotent (dedupe on `source_type+source_ref`).

Also handle `InvoiceCancelled(TenantId, DocumentId, StornoDocumentId)` and `CreditNoteIssued(...)` → Stornobuchung (Generalumkehr).

**Payment → booking (ACCT-04):** `PaymentService.RecordAsync`/`ReverseAsync` (`src/Numera.Api/Services/PaymentService.cs`) already run in a transaction and update `OpenItem`/`SalesDocument`, but **publish no event**. Add the booking **inside** that same transaction (it already has `_db`, `_currentTenant`, `_audit`) OR add a `PaymentRecorded`/`PaymentReversed` event to the Sales events + publisher. Inline is cleaner here because the open-item mutation and the Bank↔Forderung booking must be atomic. The `PaymentAllocation` rows (payment↔open_item, `AllocatedAmount`) give the per-open-item split needed to close the Forderung exactly.

### Recommended project structure

```
src/modules/Numera.Modules.Ledger/
├── Account.cs                 # extend
├── JournalEntry.cs            # extend
├── Posting.cs                 # extend
├── IPostingSource.cs          # implement
├── LedgerSettings.cs          # NEW (per-tenant chart/tax settings)
├── FiscalPeriod.cs            # NEW (period lock)
├── Steuerschluessel.cs        # NEW enum/table of DATEV BU keys
├── Seed/
│   ├── skr03.accounts.json    # NEW checked-in seed
│   └── skr04.accounts.json    # NEW checked-in seed
├── Posting/
│   ├── InvoicePostingSource.cs
│   ├── PaymentPostingSource.cs
│   ├── ExpensePostingSource.cs      # ACCT-05 rule (fed later by Belege phase)
│   └── AccountResolver.cs           # SKR + TaxCategory + entity overrides → account+key
src/Numera.Api/
├── Events/PostInvoiceOnFinalize.cs  # only if post-commit path chosen
├── Endpoints/LedgerEndpoints.cs     # journal + Kontoauszug reads, setup (SKR choice), period lock
```

### Anti-Patterns to Avoid
- **Booking as a flat 2-account row.** Model header + N lines from day 1 (already the shape) — split VAT and multi-open-item payments need it.
- **Festschreibung as a bare boolean with no period.** Ship the `fiscal_periods` lock (ACCT-09) in this phase; retrofitting corrupts filed USt-VA later.
- **Editing/deleting a posted entry.** Only Stornobuchung. The REVOKE + trigger makes this impossible at the DB even if app code tries.
- **Deriving VAT at report time.** Store `Steuerschluessel` + `TaxCategory` on the posting so USt-VA is driven off account+key (single source of truth Steuerberater audit).
- **Manual tax key on an Automatikkonto.** Reject a Steuerschlüssel on an account that already implies one (double-VAT bug) — see Pitfall 4.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tenant RLS context | Manual `SET` in each query | `TenantConnectionInterceptor` + `ICurrentTenant.SetTenant` in jobs | Pool-safe, parameterised, already proven |
| Immutability enforcement | App-only guards | DB REVOKE + trigger (Pattern 2) | GoBD needs DB-level; app guards are bypassable |
| New-table isolation | Manual query filters | Reflective `ITenantEntity` discovery in `NumeraDbContext` | Auto query filter + index; only the RLS SQL is manual |
| Per-category VAT rounding | Custom rounding | `RoundingPolicy.RoundTax` (already used by breakdown) | Golden-file-tested EN-16931 rounding |
| Money type | `double`/custom | `decimal` `numeric(19,4)` + NodaMoney | v1 standard; float in a ledger = Soll≠Haben |
| Gapless journal number | DB SEQUENCE | The race-safe numbering pattern used by `NumberingService` | `Account.cs` doc explicitly warns: never lean on DB sequences for legal numbers |
| SKR chart | Scrape DATEV PDF at runtime | Checked-in seed JSON keyed to the Steuerschlüssel-Tabelle | No DATEV API; versioned seed is auditable |
| Event dispatch | MediatR | `InProcessDomainEventPublisher` | STACK decision: no commercial MediatR |

**Key insight:** The entire GoBD enforcement surface (RLS, append-only, whitelist trigger) already exists in three shipped migrations. This phase is 80% *applying proven patterns to new tables* and 20% genuinely new work (balance trigger, period lock, SKR seed, posting rules).

## SKR03 / SKR04 Seed Data (MVP)

Seed a **minimal but correct** chart per variant. Store on each `Account`: `Number`, `Name`, `Type` (existing `AccountType`), `ChartVariant`, `IsAutomatikkonto`, implicit `Steuerschluessel`, `UstvaKennziffer` (seam for later), `IsActive`. Source of truth for the mapping: [DATEV Steuerschlüssel-Tabelle SKR03/SKR04 (Dok. 0907054)](https://help-center.apps.datev.de/documents/0907054) and [SKR03/04 Kontenrahmen (Dok. 1038737)](https://help-center.apps.datev.de/documents/1038737). Do not copy DATEV's PDF text verbatim (copyright) — curate the numbers/names.

### Minimal account set (verified numbers)

| Purpose | SKR03 | SKR04 | Type | Automatik? | Steuerschl. | USt-VA Kz (seam) |
|---------|-------|-------|------|-----------|-------------|------------------|
| Erlöse 19% USt | **8400** | **4400** | Revenue | yes (→USt) | 3 | 81 |
| Erlöse 7% USt | **8300** (or 8401) | **4300** (or 4401) | Revenue | yes | 2 | 86 |
| Erlöse steuerfrei / §19 | 8200 | 4200 | Revenue | no | 0/1 | — |
| Forderungen aus L+L (Debitoren) | **1400** | **1200** | Asset | no | — | — |
| Verbindlichkeiten aus L+L (Kreditoren) | **1600** | **3300** | Liability | no | — | — |
| Bank | **1200** | **1800** | Asset | no | — | — |
| Kasse | **1000** | **1600** | Asset | no | — | — |
| Umsatzsteuer 19% | **1776** | **3806** | Liability | — | — | 81-basis |
| Umsatzsteuer 7% | **1771** | **3801** | Liability | — | — | 86-basis |
| Abziehbare Vorsteuer 19% | **1576** | **1406** | Asset | — | 9 | 66 |
| Abziehbare Vorsteuer 7% | **1571** | **1401** | Asset | — | 8 | 66 |
| Wareneingang/Aufwand 19% VSt | 3400 | 5400 | Expense | yes (→VSt) | 9 | 66 |
| Sonstige betriebl. Aufwendungen | 4980 | 6300 | Expense | no | 9 | 66 |

Automatikkonto mapping verified: SKR03 8400→USt 1776, 8300/8401→USt 1771; SKR04 4400→USt 3806, 4400... 7% →3801.

### DATEV Steuerschlüssel (BU-Schlüssel) — verified

| Key | Meaning | Rate |
|-----|---------|------|
| 0 | kein Steuerschlüssel / steuerfrei ohne VSt-Abzug | — |
| 1 | steuerfrei (mit Vorsteuerabzug) | 0% |
| 2 | Umsatzsteuer 7% | 7% |
| 3 | Umsatzsteuer 19% | 19% |
| 8 | Vorsteuer 7% | 7% |
| 9 | Vorsteuer 19% | 19% |
| 20 | Reverse-charge, no tax | — |
| 22 / 23 | Reverse-charge USt 7% / 19% (§13b) | 7/19% |
| 28 / 29 | Reverse-charge VSt 7% / 19% | 7/19% |
| 40 | Aufhebung der Automatik am Automatikkonto | — |
| 42 | innergemeinschaftlicher Erwerb (reverse charge) | — |
| 19 / 48 | Skonto 19% Erlös / 19% Aufwand (auto tax-correct) | — |

Model `Steuerschluessel` as first-class so it can be attached to a posting and drive the USt-VA later. Reject applying a manual key on an Automatikkonto (Pitfall 4).

### EN-16931 TaxCategory → Steuerschlüssel / account mapping

The invoice already carries `TaxCategory` (BT-118) + `VatRatePercent` per breakdown row (`Numera.Platform.Money.TaxCategory`: `S, AE, K, E, Z, G, O`). Map:

| TaxCategory | Meaning | Revenue account | Steuerschl. | Notes |
|-------------|---------|-----------------|-------------|-------|
| S (19%) | Standard | 8400/4400 | 3 | USt to 1776/3806 |
| S (7%) | Ermäßigt | 8300/4300 | 2 | USt to 1771/3801 |
| AE | §13b reverse-charge | steuerfrei rev. acct | 20/22/23 | no output USt; recipient owes |
| K | innergem. Lieferung | 8125/4125 (i.g.) | 1 | Kz 41 seam (later) |
| E / Z / G / O | exempt / zero / export / out-of-scope | steuerfrei rev. acct | 0/1 | no VAT line |
| §19 Kleinunternehmer | `SalesDocument.IsKleinunternehmer=true` | 8200/4200 | 0 | never post USt |

`SalesDocument.ReverseCharge` and `IsKleinunternehmer` flags are already frozen at finalize — use them, don't re-derive.

## Auto-Posting Rules (the concrete Buchungssätze)

Amounts from the frozen `SalesDocument`/`SalesDocumentTaxBreakdown`. Accounts resolved via `AccountResolver` in this precedence: **Product/Partner override → CompanyProfile default → SKR standard account for the TaxCategory**. (Override columns exist: `Product.RevenueAccount`, `BusinessPartner.DebtorAccount/CreditorAccount`, `CompanyProfile.RevenueAccount/DebtorAccount/CreditorAccount`.)

**ACCT-03 Outgoing invoice (Rechnung), 19% example (SKR03):**
```
Soll  1400 Forderungen aLuL      brutto (TotalGross)
  Haben 8400 Erlöse 19%          netto  (TaxableBase, Steuerschl. 3)
  Haben 1776 Umsatzsteuer 19%    steuer (TaxAmount)
```
One Haben-pair per breakdown row (split VAT → multiple revenue+USt legs). Storno/Gutschrift → same accounts, reversed (Generalumkehr).

**ACCT-04 Payment received (close Forderung):**
```
Soll  1200 Bank                  allocated amount
  Haben 1400 Forderungen aLuL    allocated amount
```
One booking per `Payment`, split by `PaymentAllocation` per `OpenItem`. Reversal payment (`Amount<0`, `ReversesPaymentId`) → reversing booking. Skonto/rounding differences (later) post to a discount account; MVP can require exact allocation (PaymentService already validates `Σallocations = amount`).

**ACCT-05 Incoming invoice / expense (Aufwand + Vorsteuer), 19% (SKR03):**
```
Soll  4980/3400 Aufwand          netto  (Steuerschl. 9)
Soll  1576 Abziehbare Vorsteuer  steuer
  Haben 1600 Verbindlichkeiten aLuL   brutto
```
Feeds EÜR/USt-VA Vorsteuer (Kz 66) later. NOTE: the Beleg/expense capture pipeline is a later phase — model the posting rule + account resolution here so it's ready, but the data source (supplier invoice / OCR) is out of scope for Phase 10. If no expense entity exists yet, the planner may scope ACCT-05 to the posting engine + tests only, with the entry point stubbed for the Belege phase.

## Read Views (ACCT-06)

Both are RLS-scoped reads (tenant filter + RLS already apply to `journal_entries`/`postings`).

**Buchungsjournal:** list `journal_entries` with aggregated posting summary, ordered by `journal_number` (or `entry_date, id`), keyset-paginated on the UUIDv7 `id` (time-ordered PK — no OFFSET). Filter by period/date range.

**Kontoauszug (per account):** `postings` for one `account_id`, ordered by entry date, with a **running balance** (`SUM(signed amount) OVER (ORDER BY entry_date, id)` where signed = `+amount` for the account's natural side, `-` for the other). Show opening balance from prior locked periods. Keyset-paginate. Consider a lightweight indexed read model / SQL view; do not recompute all-history on every view (Performance Trap — pre-aggregate per period once reports arrive).

Add `(tenant_id, account_id, entry_date)` and `(tenant_id, entry_date)` indexes for these paths (reflective discovery only adds `(tenant_id, id)`).

## Common Pitfalls

### Pitfall 1: Unbalanced postings (Soll≠Haben)
**What goes wrong:** a booking persists with debit≠credit, or split-line rounding drifts a cent; every downstream report is silently wrong.
**How to avoid:** deferred DB constraint trigger (Pattern 4) + domain invariant + golden-file tests (19%, 7%, mixed, §13b, multi-open-item payment). Compute tax with `RoundingPolicy` per category; place any remainder on a defined line — never round each line independently.
**Warning signs:** a Verrechnungskonto slowly accumulates cents.

### Pitfall 2: Editing/deleting a posted entry (GoBD Unveränderbarkeit)
**How to avoid:** REVOKE UPDATE/DELETE + immutability trigger on `journal_entries`/`postings` (Pattern 2); corrections only as Stornobuchung referencing the original via `ReversesEntryId`; gapless journal number assigned at Festschreibung (race-safe numbering, not a SEQUENCE).

### Pitfall 3: Period lock too weak/late
**How to avoid:** `fiscal_periods` with `Locked` status + an INSERT trigger on `journal_entries` that rejects a booking whose `entry_date` falls in a locked period (Pattern 3). Reject or redirect to the next open period with an explicit user decision.

### Pitfall 4: Wrong SKR account / misused Steuerschlüssel on Automatikkonten
**How to avoid:** seed `IsAutomatikkonto` + implicit key; reject a manual key on an Automatikkonto; drive USt-VA off account+key. Ship BOTH SKR03 and SKR04 (per-tenant choice fixed at setup). SKR03 (Prozessgliederung, 8400) vs SKR04 (Abschlussgliederung, 4400) are *different numbering schemes* — the seed must be per-variant.

### Pitfall 5: Ist- vs Soll-Versteuerung period timing
**How to avoid:** capture `Besteuerungsart` on `LedgerSettings` NOW even though consumed later. Under Ist-Versteuerung the *payment* date drives the USt-VA period (couples payments into VAT); under Soll the invoice date does. The posting still happens on both events — the setting only changes which date the later USt-VA phase reads.

### Pitfall 6: New tables shipped without RLS / jobs without tenant context
**How to avoid:** `ledger_settings` + `fiscal_periods` get RLS ENABLE+FORCE+policy in the migration; any Hangfire job calls `SetTenant`; add cross-tenant tests (tenant A cannot read B's journal) — v1 has a Cross-Tenant suite; grow it.

### Pitfall 7: Money precision
**How to avoid:** `decimal` end-to-end, `numeric(19,4)`; no float anywhere on the posting path. `Posting.Amount` is already `numeric(19,4)`.

## Code Examples

### Reflective entity registration (how a new ledger entity is picked up)
```csharp
// Source: src/platform/Numera.Platform.Db/NumeraDbContext.cs (RegisterModuleTenantEntities)
// Any non-abstract ITenantEntity in a loaded Numera.* assembly is auto-registered,
// gets a (tenant_id, id) index + the named "Tenant" query filter. New ledger tables
// must be added to ConfigureLedgerTableNames for their snake_case table name:
var ledgerTables = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["Account"] = "accounts",
    ["JournalEntry"] = "journal_entries",
    ["Posting"] = "postings",
    // add: ["LedgerSettings"] = "ledger_settings", ["FiscalPeriod"] = "fiscal_periods",
};
```

### Posting inside the payment transaction (the seam to extend)
```csharp
// Source: src/Numera.Api/Services/PaymentService.cs (RecordAsync) — inside the existing tx,
// after open-item mutation, before SaveChanges. Add the Bank<->Forderung booking here so it
// is atomic with the open-item close. PaymentAllocation gives the per-open-item split.
await using var tx = await _db.Database.BeginTransactionAsync(ct);
// ... existing: create Payment, PaymentAllocations, reduce OpenItem.OpenAmount ...
// NEW: _db.Add(new JournalEntry { ... postings: Soll Bank / Haben Forderungen ... });
await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

## State of the Art

| Old (inert v1) | Phase 10 | Impact |
|----------------|----------|--------|
| `Account` = Number/Name/Type only | + SKR variant, Steuerschlüssel, Automatikkonto, Kennziffer | Real chart, DATEV-literate |
| `journal_entries`/`postings` RLS only | + REVOKE + immutability + balance + period-lock triggers | GoBD-fest |
| No tenant tax setup | `LedgerSettings` (SKR/Ist-Soll/Gewinnermittlung) | Drives posting + later USt-VA |
| Invoices publish event, payments don't | Payment posting inline (or new event) | ACCT-04 hookable |

## Open Questions

1. **Post inline vs post-commit handler?** — Recommendation: inline (atomic). Planner should confirm and, if post-commit, mandate idempotent dedupe on `source_type+source_ref`.
2. **Where do `LedgerSettings` live** — new table vs columns on `tenants`/`company_profile`? Recommendation: new `ledger_settings` (one-per-tenant, RLS) to keep the platform `tenants` table lean (it deliberately holds no business data).
3. **Journal number scheme** — per fiscal year, per tenant, gapless. Reuse `NumberingService` pattern; confirm format (e.g. `2026-000001`).
4. **ACCT-05 scope** — is there any expense/supplier-invoice entity to post *from* in this phase, or only the posting engine + tests (data source deferred to Belege)? Recommendation: engine + tests only; stub the entry point.
5. **7% revenue account** — 8300 vs 8401 (SKR03) / 4300 vs 4301 (SKR04): both exist in DATEV; pick 8300/4300 (classic) for the seed and document the choice.
6. **EÜR vs Bilanz depth** — capture `Gewinnermittlungsart` now; full Bilanz is out of scope (later phase).

## Sources

### Primary (HIGH confidence — read from the repo this session)
- `src/modules/Numera.Modules.Ledger/{Account,JournalEntry,Posting,IPostingSource}.cs` — existing inert schema
- `src/platform/.../Migrations/20260710021521_InitialPlatform.cs` — ledger tables + RLS (no immutability yet)
- `src/platform/.../Migrations/20260727205739_Payments.cs` — REVOKE + immutability trigger template
- `src/platform/.../Migrations/20260712151258_SalesDocuments.cs` — status-guarded whitelist trigger (Festschreibung model)
- `src/modules/Numera.Modules.Sales/{SalesDocument,OpenItem,SalesDocumentTaxBreakdown,CompanyProfile,SalesEnums}.cs`; `Payments/{Payment,PaymentAllocation}.cs`; `Events/{SalesDomainEvents,IDomainEventPublisher}.cs`
- `src/Numera.Api/Services/PaymentService.cs`; `src/Numera.Api/Events/EnqueuePdfOnFinalize.cs`; `src/Numera.Api/Jobs/RenderDocumentPdfJob.cs`; `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` (finalize flow)
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs`; `Numera.Platform.Tenancy/TenantConnectionInterceptor.cs`; `Numera.Platform.Money/TaxCategory.cs`
- Model snapshot confirms `revenue_account`/`debtor_account`/`creditor_account` seam columns on CompanyProfile/Partner/Product
- `.planning/research/{SUMMARY,STACK,ARCHITECTURE,PITFALLS}.md` — locked milestone decisions

### Secondary (HIGH — verified this session)
- [DATEV Steuerschlüssel-Tabelle SKR03/SKR04 (Dok. 0907054)](https://help-center.apps.datev.de/documents/0907054)
- [DATEV SKR03/SKR04 Kontenrahmen/Kontenfunktionen (Dok. 1038737)](https://help-center.apps.datev.de/documents/1038737)
- [epago: DATEV-Buchungsschlüssel / BU-Schlüssel + Automatikkonten](https://epago.de/lexikon/steuern/datev-buchungsschluessel-guide/) — verified BU keys 1/2/3/8/9, §13b 20-29, 40/42, Automatik 8400→1776 / 4400→3806
- [DATEV-Kontenrahmen 2026](https://www.datev.de/web/de/berufsgruppenuebergreifend/service-und-support/wichtige-informationen-zum-jahreswechsel/jahreswechsel-rechnungswesen/anpassungen-in-den-programmen/DATEV-Kontenrahmen)

### Tertiary (MEDIUM — confirm in the later USt-VA/Reports phase)
- USt-VA Kennziffern per fiscal year (81/86/66/83 etc.) — mapping seam only in Phase 10; exact XSD/Kennziffer set is the Reports phase's job

## Metadata

**Confidence breakdown:**
- Codebase seams / entities / events: HIGH — read from source this session
- Immutability/RLS/trigger patterns: HIGH — copied from three shipped migrations
- SKR03/SKR04 accounts + Steuerschlüssel: HIGH — DATEV-verified
- Auto-posting Buchungssätze: HIGH — standard German bookkeeping, grounded in existing VAT breakdown data
- USt-VA Kennziffer specifics: MEDIUM — seam only; deferred to Reports phase

**Research date:** 2026-08-03
**Valid until:** ~2026-09-03 for SKR/DATEV facts (fiscal-year stable); codebase facts valid until the Ledger module changes
