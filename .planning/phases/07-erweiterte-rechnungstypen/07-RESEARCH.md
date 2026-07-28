# Phase 7: Erweiterte Rechnungstypen - Research

**Researched:** 2026-07-28
**Domain:** German invoicing edge-types on a .NET 10 / EF Core 10 / Postgres RLS modular monolith — foreign-currency VAT (EN 16931 BT-6/BT-111), tenant-safe scheduled recurring invoices (Hangfire + RLS), and down-payment/final-invoice VAT logic (§14 UStG, EN 16931 BT-113)
**Confidence:** HIGH on codebase integration + EN 16931 mechanics + German VAT rules; MEDIUM on the exact ZUGFeRD-csharp 18 BT-111 emission call and on the two scheduling architectures' operational tradeoffs

## User Constraints

**No CONTEXT.md exists for this phase** (`/gsd:discuss-phase` was not run). There are therefore no locked user decisions to honor verbatim. The **Open Questions / Decisions Needed** section at the end collects the choices a planner (or a discuss-phase pass) must resolve before or during planning. Everything else below is a prescriptive recommendation grounded in the existing code.

---

## Summary

All three features extend the *existing, working* finalize pipeline rather than replacing anything. The codebase is unusually disciplined: a single polymorphic `SalesDocument` table with a `DocumentType` discriminator, a single `FinalizeCoreAsync` transaction (race-safe numbering + frozen jsonb snapshots + BG-23 VAT breakdown + open-item), a single `VatCalculationService`, a single `EInvoiceMapper`, and a single `RoundingPolicy`. The winning strategy for every task in this phase is **"extend the one place, keep GoBD-freeze discipline, add a KoSIT golden scenario"** — never fork the pipeline.

The three requirements have very different centers of gravity. **INV-05 (foreign currency)** is a *data + mapping* change: `SalesDocument.Currency`/`OpenItem.Currency` already exist (BT-5), so the work is capturing a **frozen exchange rate**, computing/persisting **VAT-in-EUR** (EN 16931 BT-6 + BT-111, mandatory under BR-53), and threading it through PDF + XRechnung + a new KoSIT golden. **INV-07 (Abschlags-/Schlussrechnung)** is the *deepest finalize change*: new `DocumentType`s, a frozen prepayment-deduction child table, and correct VAT netting via EN 16931 **BT-113 (Vorauszahlung)** so already-taxed down-payments are not taxed twice. **INV-06 (recurring)** is a *scheduling-infrastructure* change whose only hard problem is the Phase-6 landmine: a multi-tenant cron cannot run under the RLS-forced `numera_app` role.

**Primary recommendation:** Resolve the recurring RLS-vs-cron tension by making Hangfire's own (non-RLS) storage the scheduler — register **one Hangfire recurring job per template** with `tenantId` baked into the job arguments, so every fire re-enters the proven `SetTenant(tenantId)` per-scope pattern and no cross-tenant enumeration (and no `BYPASSRLS` role) is ever needed. Build INV-07 first (largest finalize/VAT/e-invoice surface), then INV-05 (currency layered onto the same surface), then INV-06 (which simply *reuses* the now-complete finalize path).

---

## Architecture Patterns (existing — reuse verbatim)

### The finalize pipeline is the spine — study it before planning any task
`src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` → `FinalizeCoreAsync` (internal, lines ~858-927). One transaction:
1. Freeze issuer + recipient jsonb snapshots (`SerializeIssuer`/`SerializeRecipient`).
2. Compute BG-23 breakdown via `VatCalculationService.Calculate` → persist `SalesDocumentTaxBreakdown` rows **while status is still Draft** (child immutability trigger permits inserts only into a Draft parent).
3. Set frozen totals (`TotalNet/TotalTax/TotalGross/AmountDue`).
4. Assign race-safe number via `NumberingService.AssignAsync(docType, year)` (atomic `INSERT … ON CONFLICT … RETURNING` per `(tenant, doc_type, year)`).
5. Compute `DueDate`; create `OpenItem` **only for `DocumentType.Rechnung`**.
6. `SaveChanges` (writes children under Draft) → flip `Status = Finalized` → second `SaveChanges` (parent trigger allows because `OLD.status = 0`).
7. Caller commits, then publishes `InvoiceFinalized` (post-commit), which already fans out to `RenderDocumentPdfJob` + `GenerateEInvoiceJob`.

**Every new invoice type in this phase must flow through this exact method.** The gates keyed on `== DocumentType.Rechnung` (open-item creation here; e-invoice pre-finalize dry-run at lines ~480-497) are the two places new invoice types must be added.

### The tenant-safe background-job pattern (LOCKED, copy it)
`GenerateEInvoiceJob` / `SendDunningNoticeJob`: constructor takes `IServiceScopeFactory`; `RunAsync(Guid tenantId, …)` does `using var scope = _scopeFactory.CreateScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);` then resolves a fresh `NumeraDbContext`. The `TenantConnectionInterceptor` pushes `app.current_tenant` on connection-open so **RLS applies inside the job exactly as in a request**. Jobs run on the **Api default Hangfire queue**, never the Worker queue (the Worker host lacks the Sales/ZUGFeRD references — LOCKED pitfall). `[AutomaticRetry(Attempts = 3)]`. Enqueue **after** the DB transaction commits.

### GoBD freeze discipline (LOCKED)
`EInvoiceMapper` + `SnapshotReader` + `InvoicePdfModel` read **only** frozen data (jsonb snapshots + persisted lines/breakdown/totals), never live `CompanyProfile`/`BusinessPartner`. Every new legally-relevant value in this phase (exchange rate, EUR VAT total, each deducted down-payment's net/VAT/number) **must be persisted at finalize and never recomputed at render time.**

### Persistence conventions (LOCKED)
- Every tenant table is an `ITenantEntity` (reflectively discovered in `NumeraDbContext`); RLS policies are **hand-written in the migration** (`ENABLE` + `FORCE` + `tenant_isolation` policy on `tenant_id = current_setting('app.current_tenant')::uuid`), never auto-generated. Master copy: `src/platform/Numera.Platform.Db/Sql/rls_policies.sql`.
- Money columns `numeric(19,4)`; unit price/qty `numeric(19,6)`; rate `numeric(5,2)`.
- Client-set UUIDv7 PKs (`Guid.CreateVersion7()`); persist with `db.Add(...)`, **never** via a nav-collection `.Add` on a tracked parent (documented past bug).
- Migrations live in `src/platform/Numera.Platform.Db/Migrations/`.

---

## Feature 1 — INV-05: Foreign currency (EUR VAT ausweis)

### What already exists
- `SalesDocument.Currency` (BT-5, default `"EUR"`) and `OpenItem.Currency` are present. But `POST /api/documents` **hardcodes `Currency = "EUR"`** (line ~242); `convert`/`storno`/`credit-note` copy `source.Currency`. So the plumbing exists but no path lets a user *choose* a currency, and nothing computes VAT-in-EUR.
- `Money` (NodaMoney 2.7.0-validated ISO-4217) forbids cross-currency `+`/`-` (throws) — relevant because Phase-6 payments allocate against `OpenItem` amounts.

### The legal requirement (verified)
German law (§16 Abs. 6 + §14 UStG / UStAE): an invoice may be issued in a foreign currency, but the **VAT amount (Steuerbetrag) must additionally be expressed in EUR**. EN 16931 encodes this exactly:
- **BT-5** = document currency (the foreign currency). One currency throughout the invoice **except**…
- **BT-6** = VAT accounting currency (here `EUR`), used only when it differs from BT-5.
- **BT-111** = Invoice total VAT amount in accounting currency (EUR). **BR-53: if BT-6 is present, BT-111 shall be provided.** BT-111 is *not* used in the invoice total arithmetic — it is a parallel VAT statement.
- **BT-110** = VAT total in document currency (unchanged, per-category rounded as today).

Confidence: HIGH (Peppol/EN 16931 rule docs + ZUGFeRD-csharp XML doc comments both confirm BT-6/BT-111/BR-53 semantics).

### Recommended model changes
Add to `sales_documents` (all frozen at finalize):
| Column | Type | Purpose |
|--------|------|---------|
| `exchange_rate` | `numeric(19,6)` | Units of document currency per 1 EUR (or EUR per unit — **pick one convention and document it**). NULL/1 for EUR invoices. |
| `exchange_rate_date` | `date` | Rate reference date (GoBD provenance). |
| `total_tax_eur` | `numeric(19,4)` | BT-111 — the document VAT total converted to EUR at the frozen rate, rounded to 2 dp with `RoundingPolicy`. |

Keep `Currency` as BT-5. `VatAccountingCurrency` is implicitly fixed `EUR` (a constant, not a column). Do **not** convert per-line or per-breakdown to EUR — BT-111 is a single document-level total; convert `doc.TotalTax` once.

### Exchange-rate sourcing — recommendation
**User-entered, stored, frozen.** The rate is captured on the draft (defaulted/suggested in the UI, optionally pre-filled from a rate source as a convenience), persisted, and frozen at finalize. Rationale, aligned with the platform ideal: deterministic, reproducible, GoBD-immutable, and independent of any third-party API's availability at render time (mirrors the "never recompute at render" LOCKED rule). Do **not** call a live FX API inside finalize or render. If a suggestion source is wanted, a manual "fetch ECB reference rate" button that merely *pre-fills* the editable field is acceptable — the persisted value is authoritative.

### VAT-in-EUR computation
1. Compute the BG-23 breakdown and `TotalTax` in the document currency **exactly as today** (`VatCalculationService` unchanged — per-category `RoundingPolicy.RoundTax`).
2. `total_tax_eur = RoundingPolicy`-consistent round of `TotalTax × exchangeRate` (or `/ rate`, per chosen convention) to 2 dp, half-away-from-zero. Persist at finalize.

### PDF + XRechnung/ZUGFeRD
- **PDF** (`InvoicePdfModel` already carries `Currency`): add the frozen `exchange_rate` and `total_tax_eur` to the model + `SnapshotReader`, and render a "USt in EUR: …" line + the rate note. Formatting culture stays de-DE.
- **XRechnung/ZUGFeRD** (`EInvoiceMapper.ToDescriptor`): document currency already flows via `ParseCurrency(model.Currency)`. Add BT-6/BT-111: **ZUGFeRD-csharp 18 exposes `InvoiceDescriptor.TaxCurrency` (BT-6) and the writer emits the accounting-currency VAT total (BT-111).** `EInvoiceMapper` is the single change locus. **MEDIUM-confidence gap:** confirm during planning the exact call that supplies the BT-111 *value* (the `TaxCurrency` property sets the code; verify whether the value is taken from a per-tax field or a document total, by generating XML and running the KoSIT harness). ZUGFeRD-csharp 18 `SetTotals` already has the 9-arg overload (incl. `totalPrepaidAmount`, `roundingAmount`) — see INV-07.
- **KoSIT**: add a foreign-currency scenario to `tests/Numera.IntegrationTests/KoSitConformanceTests.cs` (both UBL + CII). Also extend the **pre-finalize dry-run** so a non-conformant currency setup is caught before a number is burned.

### Currency pitfalls to gate
- **Minor-unit mismatch:** `RoundingPolicy` rounds to 2 dp. Currencies with 0 (JPY) or 3 (BHD, KWD) minor units break that assumption. **Recommend restricting v1 to 2-minor-unit currencies** (validate against NodaMoney's minor-unit metadata at draft time) and flag the rest as out of scope — or explicitly decide to support them.
- **Payments (Phase 6):** an open item in a foreign currency must be paid/allocated in that same currency (`Money` throws on mismatch). Verify the payment path tolerates non-EUR or gate it.

---

## Feature 2 — INV-06: Recurring invoices (Serienrechnungen)

### The #1 problem to resolve: RLS vs. a multi-tenant scheduler
Confirmed from code, not assumption:
- `scripts/db-roles.sql`: `numera_app` (runtime) is `NOSUPERUSER NOBYPASSRLS`; `numera_migrator` (owns tables) is **also** `NOBYPASSRLS`, and every tenant table is `FORCE ROW LEVEL SECURITY`, so **even the owner is filtered**.
- The `tenants` table is **self-scoped**: policy `USING (id = current_setting('app.current_tenant')::uuid)`. With no tenant GUC set, a `SELECT * FROM tenants` returns **zero rows** (and the cast can error). There is **no role in the system that can enumerate all tenants**.
- Phase 6 hit this exact wall: the Mahnlauf was made **manual/user-initiated** because a scheduled multi-tenant cron would run with no tenant context and see nothing under RLS. INV-06 *requires* automatic generation, so this must be resolved deliberately.

### Recommended design (PRIMARY): per-template Hangfire recurring job
Make **Hangfire's own storage the scheduler**. Hangfire tables live in a separate schema owned by the privileged `Hangfire` connection (`Username=numera` in dev) and are **not RLS-subject** — this is already true and load-bearing in the app.

- When a user creates/activates a recurring template, register a recurring job whose **arguments carry `tenantId`**:
  ```csharp
  RecurringJob.AddOrUpdate<GenerateRecurringInvoiceJob>(
      recurringJobId: $"recurring-invoice:{tenantId}:{templateId}",
      methodCall: j => j.RunAsync(tenantId, templateId, CancellationToken.None),
      cronExpression: cron);           // Cron.Monthly(...) etc., derived from the template cadence
  ```
- Each fire runs the **LOCKED per-scope tenant pattern**: `CreateScope()` → `SetTenant(tenantId)` → RLS applies → build the draft → call the **same `FinalizeCoreAsync`** → post-commit event fans out to PDF + e-invoice jobs already.
- **Pause** = `RecurringJob.RemoveIfExists(id)`; **resume** = re-add; **end date / after-N** = the job checks the template's end condition and removes its own recurring entry when exhausted. **Next-run** is derivable from the cron/template state for the UI.

**Why this resolves the tension:** there is **no cross-tenant enumeration anywhere** and **no new DB role** — the tenant identity travels inside the job payload, and every DB touch is single-tenant under RLS, exactly like the four existing jobs. It reuses infrastructure that already works.

### Alternative design (documented, not recommended for v1): central dispatcher + `BYPASSRLS` scheduler role
One global `RecurringJob` (daily) acts as a dispatcher that reads due `(tenant_id, template_id)` pairs across all tenants and enqueues a per-tenant `GenerateRecurringInvoiceJob` for each. Because nothing can read across tenants today, this **requires a new minimal-privilege DB role with `BYPASSRLS`** (e.g. `numera_scheduler`, `LOGIN`, granted `SELECT` only on the recurring-templates dispatch view) used **solely** for the enumeration query; all tenant work still happens in per-tenant `SetTenant` scopes. Note: a `SECURITY DEFINER` function does **not** suffice, because its owner (`numera_migrator`) is `NOBYPASSRLS` under `FORCE` RLS.
Tradeoff: centralizes control/observability and scales to very many templates without many Hangfire recurring entries, but **reintroduces a cross-tenant surface** — the precise thing Phase 6 avoided. Prefer PRIMARY unless template counts become extreme.

### Idempotency + catch-up (mandatory regardless of design)
Cron fires are not a safe generation trigger by themselves: `AutomaticRetry` + enqueue-after-commit means a job **can run twice**, and a server outage can miss occurrences. Drive generation from **template state**, not from the cron pulse:
- Template carries `next_run_on` (date) + cadence + `last_generated_period_end`.
- On fire, the job **loops**: while `next_run_on <= today`, generate the invoice for that period, advance `next_run_on` by one cadence. This yields **catch-up** after downtime.
- Guard every generated invoice with a **UNIQUE `(tenant_id, recurring_template_id, period_key)`** index (client-set; `period_key` = e.g. `2026-07`). A duplicate fire hits the constraint → clean skip. This makes generation **idempotent**.

### Recurring-template schema (new `ITenantEntity`, RLS-forced)
`recurring_invoice_templates`: `partner_id`, `currency`, cadence (interval unit + count, or a cron string), `anchor_date`/`start_on`, end condition (never / until date / after N), `next_run_on`, `last_generated_period_end`, `status` (Active/Paused/Ended), `auto_finalize` flag (see decision below), plus the **line template** (either child rows `recurring_invoice_template_lines` mirroring `SalesDocumentLine`, or a jsonb blob — child rows are more consistent with the codebase). Per-occurrence **service period** (BT-73/74) should be computed from the cadence and written to `ServiceDate`/`ServicePeriodEnd` on each generated invoice.

### Generation flow (reuse everything)
The job builds a `SalesDocument` Draft from the template (same shape as `POST /api/documents` + `convert`), then either:
- **auto-finalizes** it by calling the **same `FinalizeCoreAsync`** inside a transaction (race-safe numbering, snapshots, open item, KoSIT dry-run, post-commit PDF/e-invoice/optional auto-send) — this literally satisfies "automatisch nach Zeitplan erzeugt"; or
- leaves it as a **Draft** for user review (no number, no immutability, no OP).
See decision below.

---

## Feature 3 — INV-07: Abschlags- & Schlussrechnung (down-payment / final invoices)

### The German model (verified)
- **Abschlagsrechnung** (progress/partial invoice) is a *full* §14 invoice for a partial amount. Its VAT is due to the tax office when the Abschlag is **paid** (Ist-/Soll-Versteuerung), independent of the Schlussrechnung.
- **Schlussrechnung** (final invoice) states the **full contract value** and then **deducts the already-invoiced Abschläge**. Per UStAE/§14 Abs. 5, each Abschlag must be **listed individually** (number, date, net, VAT) and deducted so the already-charged VAT is **not charged again**. The Abschläge are deducted at their **gross** amount from the total to pay; their VAT was already declared, so the Schlussrechnung must not re-tax them.
Confidence: HIGH (multiple German tax sources agree on the itemized-deduction method and gross deduction).

### EN 16931 / XRechnung representation (verified)
- **BT-113 (Vorauszahlung / Paid amount)**: total prepaid, in BT-5 currency. **BR-CO-16: BT-115 (amount due) = BT-112 (gross) − BT-113 + BT-114 (rounding).** BT-113 reduces the amount due **without** altering the VAT breakdown (BT-110 stays the full VAT). This is exactly the itemized method: the Schlussrechnung declares the full net + full VAT for the project, and the already-paid gross (incl. its VAT) is subtracted via BT-113 → residual to pay. ZUGFeRD-csharp 18: `InvoiceDescriptor.TotalPrepaidAmount` (BT-113) + `SetTotals(…, totalPrepaidAmount, roundingAmount)` 9-arg overload (both verified in the package XML docs). BT-3 stays **380** (commercial invoice) for both Abschlag and Schluss — the German distinction is in the document title/text, not the type code.

### Recommended model changes
**New `DocumentType` values** (they are legally distinct, numbered, immutable, e-invoiced invoices):
```
DocumentType.Abschlagsrechnung   // partial/progress invoice
DocumentType.Schlussrechnung     // final invoice
```
- Add `NumberingService.DefaultPrefix` cases (e.g. `AR-`, `SR-`) — each type gets its **own series** automatically (the numbering counter keys on `doc_type`).
- **Widen the two finalize gates** currently keyed on `== DocumentType.Rechnung`:
  1. Open-item creation (`FinalizeCoreAsync`) → also for Abschlag/Schluss.
  2. Pre-finalize KoSIT dry-run + `EnqueueEInvoiceOnFinalize` → also for Abschlag/Schluss.
  `EInvoiceMapper` maps all of them to BT-3 = 380, so the mapper's `InvoiceType.Invoice` stays correct.

**Link + freeze model (GoBD):** the Schlussrechnung must *list* each deducted Abschlag, so those values must be **frozen snapshots**, not live lookups. Add a child table (like `sales_document_tax_breakdown`):
```
sales_document_prepayment (ITenantEntity, RLS-forced)
  document_id          -> the Schlussrechnung
  abschlag_document_id -> the deducted Abschlagsrechnung (provenance)
  abschlag_number      -> frozen (snapshot)
  abschlag_date        -> frozen
  net_amount           -> numeric(19,4) frozen
  vat_amount           -> numeric(19,4) frozen
  gross_amount         -> numeric(19,4) frozen
```
Optionally add a nullable `project_id`/`vorgang_id` grouping column on `sales_documents` so the UI can gather the candidate Abschläge (Angebot → Abschläge → Schluss) — but the legally authoritative record is the frozen child rows.

### VAT treatment — the double-taxation trap
- The Schlussrechnung's **BG-23 breakdown = VAT on the FULL project value** (compute as today).
- Deduct the sum of Abschlag **gross** via **BT-113** → `AmountDue` (BT-115) = full gross − Σ Abschlag gross. The VAT breakdown stays full (the already-paid VAT was declared on the Abschläge, not re-declared here).
- **`OpenItem` must use the RESIDUAL, not the full gross.** `BuildOpenItem` currently sets `OriginalAmount = OpenAmount = doc.TotalGross`. For a Schlussrechnung it must use `AmountDue` (= gross − prepaid). **This is the single easiest place to create a wrong receivable** — plan an explicit task + test.
- **`SetTotals`** in `EInvoiceMapper` must pass `duePayableAmount = AmountDue` and `totalPrepaidAmount = Σ Abschlag gross`, so BR-CO-16 holds and KoSIT accepts.
- **KoSIT golden**: add a Schlussrechnung-with-prepayment scenario (UBL + CII) to `KoSitConformanceTests`.

### Abschlagsrechnung lifecycle
A normal immutable invoice: finalizes, gets its own number, creates an OpenItem for its amount, is paid via Phase-6 payments, is Storno-able. The Schlussrechnung generation gathers the Abschläge belonging to the same project/partner and freezes their deduction rows.

---

## Cross-Cutting: ordering, parallelism, gates, pitfalls

### Recommended build order
1. **INV-07 first** — it makes the largest changes to the shared surface (new `DocumentType`s, numbering series, the two finalize gates, a new frozen child table, `EInvoiceMapper.SetTotals`, `OpenItem` residual logic, KoSIT golden). Landing it first means INV-05 and INV-06 build on a settled finalize path.
2. **INV-05 second** — currency layers onto the *same* finalize/VAT/mapper surface (exchange-rate freeze, BT-6/BT-111, PDF EUR line, KoSIT golden). Doing it after INV-07 avoids two simultaneous rewrites of `FinalizeCoreAsync`/`EInvoiceMapper`.
3. **INV-06 last** — the scheduling/template infra is orthogonal, but generation *reuses* the now-complete finalize path (so a generated Abschlag/foreign-currency invoice "just works").

### What can parallelize
- Recurring **template CRUD + schema + scheduling infra** (backend) and its **frontend** can be built in parallel with INV-05/07 — only the *generation integration* must wait for finalize to settle.
- All three **frontends** (`web/src/features/documents/*` + new pages) are largely independent of each other.

### Entitlement / Tarif gates
`Capability` enum (`MultiUser/DataExport/Dunning/EInvoicing/ApiAccess`) has **no** capability for advanced invoice types. If any of these are premium, add `Capability` value(s) (e.g. `RecurringInvoices`, `ForeignCurrency`), wire them in `PlanCapabilityMap` (single source of truth), and gate the endpoints via `IEntitlementService.HasCapabilityAsync` (pattern in `MeEndpoints`). Roadmap Phase 9 polishes tarif-gate UX, but the **server-side seam should be added here** if these are gated. **Decision needed** (see below).

### Top pitfalls
1. **RLS-vs-cron for recurring** (the landmine) — never run a multi-tenant cron under `numera_app`; use per-template Hangfire jobs with `tenantId` in the payload (PRIMARY design).
2. **Double taxation in the Schlussrechnung** — keep the VAT breakdown full, deduct Abschläge as gross via BT-113, and set `OpenItem` to the **residual** `AmountDue` (not `TotalGross`).
3. **GoBD freeze** — exchange rate, EUR VAT total, and each Abschlag deduction (net/VAT/number/date) must be **persisted at finalize and read from the snapshot at render**, mirroring the `EInvoiceMapper`/`SnapshotReader` LOCKED rule.
4. **Numbering + finalize gates** — every new `DocumentType` needs a `DefaultPrefix` case and inclusion in the `== Rechnung` gates (open item + e-invoice dry-run/enqueue); otherwise an Abschlag finalizes with no receivable and no e-invoice.
5. **Idempotency / catch-up** — a unique `(tenant, template, period_key)` index is mandatory; `AutomaticRetry` + enqueue-after-commit means jobs can and will run twice.
6. **KoSIT regressions** — foreign currency (BR-53) and prepayment (BR-CO-16) each need a new golden conformance scenario in `KoSitConformanceTests` (UBL + CII); the pre-finalize dry-run must also cover them so a bad invoice never burns a number.
7. **Currency minor units** — `RoundingPolicy` assumes 2 dp; restrict v1 to 2-minor-unit currencies or handle 0/3-unit currencies explicitly.
8. **Persistence footguns** — client-set UUIDv7 PKs, `db.Add` (not nav-collection add), hand-written RLS in the migration for every new table (`ENABLE`+`FORCE`+policy), `numeric(19,4)` money columns.
9. **Cross-currency payments** — Phase-6 payment allocation uses `Money` (throws on currency mismatch); verify the OP/payment path for non-EUR open items.

---

## Don't Hand-Roll

| Problem | Don't build | Use instead | Why |
|---------|-------------|-------------|-----|
| Recurring scheduling across tenants | A custom polling loop / `Timer` / cross-tenant query | Hangfire recurring jobs (already in `Program.cs`), one per template with `tenantId` in args | Persistent, retrying, survives restarts, and sidesteps RLS entirely |
| Down-payment VAT netting | A bespoke "subtract VAT" formula | EN 16931 **BT-113** (`InvoiceDescriptor.TotalPrepaidAmount`) + full VAT breakdown | Standard, KoSIT-valid (BR-CO-16), avoids double taxation |
| Foreign-currency VAT statement | Custom EUR fields in XML | EN 16931 **BT-6/BT-111** via `InvoiceDescriptor.TaxCurrency` | Mandated by BR-53; the writer emits conformant XML |
| Document number for new types | A new counter | `NumberingService` (add a `DefaultPrefix` case) | Race-safe atomic counter already keyed on `doc_type` |
| VAT math for new types | New rounding code | `VatCalculationService` + `RoundingPolicy` (unchanged) | Single legally-load-bearing authority; per-category half-away-from-zero |
| FX rate lookup at render | Live FX API call in finalize/render | User-entered rate **frozen** on the document | Deterministic + GoBD-immutable + no render-time dependency |

**Key insight:** every hard problem in this phase already has a blessed home in the codebase — the win is *routing into it*, not building next to it.

## Code Examples (from the actual repo — patterns to mirror)

### Tenant-safe job entry (mirror for `GenerateRecurringInvoiceJob`)
```csharp
// Source: src/Numera.Api/Jobs/GenerateEInvoiceJob.cs
using var scope = _scopeFactory.CreateScope();
scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId); // RLS applies inside the job
var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
// … build draft, then call the SAME FinalizeCoreAsync inside a transaction …
```

### Reuse the finalize core (do NOT reimplement)
```csharp
// Source: src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs  (FinalizeCoreAsync)
await using var tx = await db.Database.BeginTransactionAsync(ct);
await FinalizeCoreAsync(doc, profile!, partner, db, numbering, audit, tenantId, "sales_document.recurring_generated", ct);
await tx.CommitAsync(ct);
await publisher.PublishAsync(new InvoiceFinalized(...), ct); // fans out to PDF + e-invoice jobs
```

### e-invoice totals with prepayment + tax currency (extend the mapper)
```csharp
// Source: src/modules/Numera.Modules.Sales/EInvoice/EInvoiceMapper.cs (ToDescriptor)
// desc.TaxCurrency = CurrencyCodes.EUR;                 // BT-6 (foreign-currency invoices)
// desc.SetTotals(lineTotal, taxBasis, taxTotal, grandTotal, totalAllowance:null,
//                totalCharge:null, roundingAmount:null, totalPrepaidAmount: prepaidGross, duePayableAmount: amountDue);
// (9-arg SetTotals + TaxCurrency/TotalPrepaidAmount confirmed in ZUGFeRD-csharp 18 package XML docs)
```

## Open Questions / Decisions Needed

1. **Recurring: auto-finalize vs. Draft-for-review?**
   - Known: success criterion says invoices are generated "automatisch nach Zeitplan". Auto-finalize burns a legal number, freezes immutably, creates an OP, and sends the e-invoice — fully automatic. Draft-for-review keeps a human in the loop but isn't "automatic".
   - Recommendation: **auto-finalize** (reusing `FinalizeCoreAsync`), with a per-template `auto_finalize` toggle and an optional `auto_send`. Default: auto-finalize, manual send. **Needs user confirmation.**

2. **Scheduling architecture: per-template Hangfire jobs (PRIMARY) vs. central dispatcher + `BYPASSRLS` role?**
   - Recommendation: PRIMARY (no cross-tenant surface, no new role). Confirm before planning INV-06 tasks.

3. **Exchange-rate convention + source:** units-of-foreign-per-EUR vs. EUR-per-unit; and whether to offer an ECB "suggest rate" pre-fill. Recommendation: user-entered + frozen; document the multiplication convention explicitly in the plan.

4. **Currency scope:** restrict v1 to 2-minor-unit currencies (recommended) or support 0/3-minor-unit currencies too?

5. **Tarif gating:** are foreign currency / recurring / down-payment invoices premium (add `Capability` values + `PlanCapabilityMap` wiring + endpoint gates now), or ungated in v1 with polish deferred to Phase 9?

6. **Abschlag/Schluss as new `DocumentType`s (recommended) vs. flags on `Rechnung`?** New types are cleaner for numbering + legal identity but touch the two finalize gates. Confirm the modeling choice.

7. **MEDIUM gap — BT-111 emission:** confirm during INV-05 planning exactly how ZUGFeRD-csharp 18 emits the BT-111 *value* (vs. just the BT-6 code) by generating XML and validating with the KoSIT sidecar.

## Sources

### Primary (HIGH confidence)
- Repo code (read directly): `SalesDocumentEndpoints.cs` (finalize/storno/credit-note), `FinalizeCoreAsync`, `NumberingService.cs`, `VatCalculationService.cs`, `RoundingPolicy.cs`, `Money.cs`, `TaxCategory.cs`, `SalesDocument.cs`, `SalesDocumentLine.cs`, `SalesDocumentTaxBreakdown.cs`, `SalesEnums.cs`, `OpenItem.cs`, `EInvoiceMapper.cs`, `InvoicePdfModel.cs`, `SnapshotReader.cs`, `TenantConnectionInterceptor.cs`, `NumeraDbContext.cs`, `EntitlementService.cs`, `PlanCapabilityMap.cs`, `Capability.cs`, `Tenant.cs`, `Program.cs`, `GenerateEInvoiceJob.cs`, `SendDunningNoticeJob.cs`, `DunningEndpoints.cs`, migration `20260710021521_InitialPlatform.cs` (RLS), `scripts/db-roles.sql`, `.planning/ROADMAP.md`.
- ZUGFeRD-csharp 18.0.0 package XML docs (`s2industries.ZUGFeRD.xml`): confirmed `InvoiceDescriptor.TaxCurrency` (BT-6/BT-111 semantics), `TotalPrepaidAmount` (BT-113), `RoundingAmount` (BT-114), 9-arg `SetTotals`.
- Peppol/EN 16931 rule docs — BR-53 (BT-6 ⇒ BT-111), BT-5/BT-6/BT-110/BT-111 definitions: https://docs.peppol.eu/poac/eu/pint-eu/trn-invoice/rule/BR-53/ , https://docs.peppol.eu/poacc/billing/3.0/syntax/ubl-invoice/cbc-TaxCurrencyCode/

### Secondary (MEDIUM confidence — multiple German sources agree)
- Down-payment / final-invoice VAT (gross deduction, itemized Abschläge, VAT already declared): https://sevdesk.de/lexikon/schlussrechnung/ , https://www.meinbuero.de/ratgeber/buchhaltung/abschlagsrechnungen-die-auswirkungen-auf-vorsteuer-und-umsatzsteuer/ , https://www.clean-invoice.de/blog/abschlagsrechnung-handwerk
- BT-113 mechanics + BR-CO-16 (BT-115 = BT-112 − BT-113 + BT-114): https://www.invoice-converter.com/en/resources/xrechnung/bt-113-paid-amount , https://www.invoice-converter.com/en/resources/xrechnung/bt-115-amount-due-for-payment

## Metadata

**Confidence breakdown:**
- Codebase integration points: HIGH — read the actual finalize/numbering/mapper/RLS/job code.
- EN 16931 currency (BT-6/BT-111/BR-53) + prepayment (BT-113/BR-CO-16): HIGH — rule docs + library XML docs agree.
- German Abschlags-/Schlussrechnung VAT method: HIGH — multiple independent German tax sources agree on itemized gross deduction.
- Recurring scheduling design: HIGH on the RLS constraint (verified in roles + migration); MEDIUM on which of the two designs is operationally best at scale.
- Exact ZUGFeRD-csharp 18 BT-111 value call: MEDIUM — property confirmed; emission path to be verified against the KoSIT sidecar during planning.

**Research date:** 2026-07-28
**Valid until:** ~2026-08-27 (stable; re-verify if ZUGFeRD-csharp, XRechnung version, or the KoSIT validator is bumped)
