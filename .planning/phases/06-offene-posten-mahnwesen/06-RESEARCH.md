# Phase 6: Offene Posten & Mahnwesen - Research

**Researched:** 2026-07-27
**Domain:** Payment recording + allocation to open items, multi-level German dunning (Mahnwesen), GoBD-safe booked facts
**Confidence:** HIGH (architecture/patterns grounded in the actual codebase); MEDIUM-HIGH (German dunning law, cross-verified)

> **No CONTEXT.md exists** for this phase (no `/gsd:discuss-phase` was run). This research is grounded in `.planning/*` + the actual codebase. Several decisions below (payment immutability model, fees-outside-the-OP, manual-vs-scheduled dunning, Skonto handling) are flagged as **DECISION** points the planner or a discuss-phase pass should confirm.

## Summary

Phase 6 closes the money loop on top of a mature, opinionated Sales module. **Almost nothing new needs to be invented** — the phase is overwhelmingly a matter of *reusing* existing, proven patterns: the `ITenantEntity` + hand-written-RLS-in-migration entity pattern, the `mutate → IAuditWriter.RecordAsync → SaveChangesAsync` idiom, DB immutability triggers for GoBD-relevant rows, `Platform.Money`/`RoundingPolicy` for exact decimals, QuestPDF (`InvoiceDocument`) for notices, `MailKit`/`IEmailSender` + a Hangfire job mirroring `SendDocumentEmailJob` for delivery, and the read-only OP-Übersicht frontend already shipped in 03-10. The e-invoice engine (Phase 5) is irrelevant here.

The **payment model** (OPDN-02) is a new `payment` table (the booked receipt: amount, value date, method, reference) plus a `payment_allocation` join table (`payment_id`, `open_item_id`, `allocated_amount`) so one payment can settle several open items and one open item can be settled by several payments (Teilzahlungen). Recording a payment runs in ONE transaction: insert payment + allocations, reduce each `OpenItem.OpenAmount`, transition `OpenItem.Status` (Open → PartiallyPaid → Paid), reduce `SalesDocument.AmountDue` (a whitelisted column) and flip `SalesDocument.Status = Paid` when fully settled, and audit — all under RLS. GoBD makes a recorded payment a **booked fact**: recommend append-only payments (a DB immutability trigger blocking UPDATE/DELETE, mirroring `audit_events`), with corrections done via a reversal (negative) payment rather than an edit.

The **dunning model** (OPDN-03) is a per-tenant `dunning_level_config` table (level 0 = Zahlungserinnerung, 1..n = Mahnung; each with a days-past-due threshold, a Mahngebühr, an optional Verzugszins flag, and DE/EN template text) plus a `dunning_notice` history table recording each issued notice. `open_items` gains two denormalized columns (`current_dunning_level`, `last_dunned_on`) for OP-Übersicht prioritisation and double-dunning idempotency. A **user-initiated** dunning run (NOT a scheduled auto-job — see Pitfall 3) detects overdue OPs, picks the next eligible level, renders a `DunningNoticeDocument` PDF (new QuestPDF layout reusing the letterhead/label infrastructure), emails it via the existing job pattern, applies the configured fee + statutory interest **as Nebenforderungen recorded on the notice, NOT baked into the OP's open_amount**, and bumps the OP's dunning level.

**Primary recommendation:** Two waves. Wave 1 = payments (`payment` + `payment_allocation`, one migration, RLS + append-only trigger, `PaymentService`, `POST/GET /api/payments`, payment-entry UI). Wave 2 = dunning (`dunning_level_config` + `dunning_notice` + ALTER `open_items`, one migration, RLS, `DunningService` + `DunningNoticeDocument` + `SendDunningNoticeJob`, config + run endpoints, config settings UI + enriched OP-Übersicht). Reuse everything; hand-write RLS on every new table; keep money in `decimal(19,4)` via `RoundingPolicy`; test on real `postgres:18` + Mailpit with RLS isolation.

## Standard Stack

No new libraries. Everything Phase 6 needs is already in the solution and proven in Phases 3–5.

### Core (reuse — already referenced)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Npgsql / EF Core | 10 (net10.0) | `payment`, `payment_allocation`, `dunning_level_config`, `dunning_notice` entities + migration | Every Sales table already uses it; reflective `ITenantEntity` discovery + snake_case + tenant filter is automatic (`NumeraDbContext`) |
| Postgres | 18 | RLS + append-only/immutability triggers + `decimal(19,4)` | The isolation + GoBD substrate; `uuidv7()` PKs |
| `Numera.Platform.Money` (`Money`, `RoundingPolicy`, `TaxCategory`) | in-repo | Exact payment amounts, fee + interest rounding | PLAT-09; never `float`; EN-16931 rounding golden-tested |
| `Numera.Platform.Audit` (`IAuditWriter`, `IAuditEvent`) | in-repo | Audit every payment + every dunning issuance | PLAT-05; in-transaction, atomic with the change |
| Hangfire + Hangfire.PostgreSql | 1.8 (compat 180) | `SendDunningNoticeJob` (+ optional render job) on the Api default queue | Same tier as `SendDocumentEmailJob`/`RenderDocumentPdfJob` |
| QuestPDF | 2026.7.1 (Community) | `DunningNoticeDocument` PDF | Community license already set once in `Program.cs`; `InvoiceDocument`/`PdfLabels`/`SnapshotReader` are the template to mirror |
| MailKit / MimeKit (`IEmailSender`, `MailKitEmailSender`) | current | Send the dunning notice PDF by e-mail (Mailpit locally) | Exactly the `SendDocumentEmailJob` path |

### Supporting (frontend — reuse the 02-06 UI stack)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| React 19 + react-router-dom | in-repo | payment-entry, dunning-config, enriched OP-Übersicht | mirror `features/openItems`, `features/inbound` |
| TanStack Query + Table | in-repo | server-paged lists, mutations | `OpenItemsListPage.tsx` is the pattern |
| React Hook Form + zod | in-repo | payment form, dunning-config form | mirror `CompanyProfileSettingsPage` (RHF + zodResolver upsert) |
| i18next (DE authoritative) | in-repo | new `payments`/`dunning` namespaces | add to `web/src/i18n/index.ts` + `locales/{de,en}/` (additive, like `inbound.json`) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `payment` + `payment_allocation` (join) | `payment.open_item_id` direct FK (one payment ↔ one OP) | Simpler, but cannot model one bank transfer paying several invoices — a very common real case and the natural seam for v2 bank reconciliation (BANK-02). **Recommend the allocation table.** |
| Append-only payments + reversal | Editable/deletable payment rows | Editable is simpler UX but violates GoBD "booked fact" (a recorded payment is a Buchung). **Recommend append-only + reversal** (mirrors `audit_events`); confirm in discuss-phase. |
| User-initiated Mahnlauf | Scheduled Hangfire recurring dunning job | A recurring job has no per-request tenant → would need to enumerate tenants and set `ICurrentTenant` per tenant, breaking the RLS-per-request model. Dunning is also a business decision, not automatic. **Recommend manual/user-initiated for v1;** defer auto-scheduling. |
| Fees/interest as Nebenforderungen on the notice | Inflate `open_item.open_amount` with the Mahngebühr | Inflating the OP corrupts the invoice receivable (the OP must equal the finalized invoice's amount; the invoice is immutable). **Recommend keeping fees off the OP**, tracked on `dunning_notice`, shown as "Gesamt zu zahlen" on the letter. |

**Installation:** none — no new NuGet or npm packages.

## Architecture Patterns

### Recommended structure (mirror existing folders)
```
src/modules/Numera.Modules.Sales/
├── Payments/
│   ├── Payment.cs                 # ITenantEntity: amount, value date, method, reference, reversal link
│   ├── PaymentAllocation.cs       # ITenantEntity: payment_id, open_item_id, allocated_amount
│   └── PaymentMethod.cs           # enum: BankTransfer, Cash, Card, SEPA, Other (append-only ordinals)
├── Dunning/
│   ├── DunningLevelConfig.cs      # ITenantEntity: level, name, days_after_due, fee, interest flag, template
│   ├── DunningNotice.cs           # ITenantEntity: history of each issued Mahnung/Erinnerung
│   ├── DunningDefaults.cs         # app-level German default ladder (used when tenant has no config rows)
│   └── DunningService.cs          # candidate detection + level selection + fee/interest calc (pure-ish)
├── Pdf/
│   ├── DunningNoticeDocument.cs   # NEW QuestPDF layout (mirror InvoiceDocument), DE/EN via PdfLabels
│   └── DunningNoticeModel.cs      # flat model (mirror InvoicePdfModel)
src/Numera.Api/
├── Services/PaymentService.cs     # record → allocate → reduce OP → transition → audit (one tx)
├── Jobs/SendDunningNoticeJob.cs   # mirror SendDocumentEmailJob (scope + SetTenant + AutomaticRetry(3))
├── Endpoints/PaymentEndpoints.cs  # POST/GET /api/payments
├── Endpoints/DunningEndpoints.cs  # config CRUD + dunning run/issue
├── Contracts/PaymentContracts.cs, DunningContracts.cs
├── Validators/PaymentValidators.cs, DunningValidators.cs
src/platform/Numera.Platform.Db/Migrations/
├── <ts>_Payments.cs               # wave 1: payment + payment_allocation + RLS + append-only trigger
├── <ts>_Dunning.cs                # wave 2: dunning_level_config + dunning_notice + ALTER open_items + RLS
web/src/
├── features/payments/…            # payment entry (against an OP; partial)
├── features/dunning/…             # config settings + dunning-run action
├── features/openItems/…           # ENRICH: dunning level, days overdue, "mahnfähig" action
├── lib/api/payments.ts, dunning.ts
```

### Pattern 1: New tenant table = self-describing entity + hand-written RLS in the migration
**What:** Every new table (`payment`, `payment_allocation`, `dunning_level_config`, `dunning_notice`) is an `ITenantEntity` with `[Table]`/`[Index]` attributes; the reflective discovery in `NumeraDbContext.RegisterModuleTenantEntities` maps it, adds the tenant-leading index and the query filter — but **NEVER emits the RLS policy** (the #1 silent cross-tenant-leak trap). RLS is hand-written in the migration.
**When to use:** every new table, no exceptions.
**Example:**
```csharp
// Source: src/platform/.../Migrations/20260726200550_DocumentEInvoice.cs (verbatim pattern)
migrationBuilder.Sql("ALTER TABLE payment ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE payment FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql(
    "CREATE POLICY tenant_isolation ON payment " +
    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
// ...and the same 3 lines for payment_allocation, dunning_level_config, dunning_notice.
```
Client-set UUIDv7 PKs (`Guid.CreateVersion7()`), inserted via `db.Add(...)` **never** a navigation-collection add (the file-wide convention — a nav-added child with a client PK is marked Modified → 0-row UPDATE → `DbUpdateConcurrencyException`; this exact bug bit finalize in 03-11).

### Pattern 2: Record-a-payment transaction (mutate → audit → SaveChanges, one tx)
**What:** Recording a payment mutates several rows atomically and audits.
**When to use:** `PaymentService.RecordAsync`.
**Example (shape, grounded in `FinalizeCoreAsync` + `SendDocumentEmailJob`):**
```csharp
// Source: mirrors src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs FinalizeCoreAsync
await using var tx = await db.Database.BeginTransactionAsync(ct);
var payment = new Payment { TenantId = tenantId, /* amount, valueDate, method, reference */ };
db.Add(payment);
foreach (var alloc in allocations) // allocated_amount <= open_item.open_amount (validated)
{
    db.Add(new PaymentAllocation { TenantId = tenantId, PaymentId = payment.Id,
                                   OpenItemId = alloc.OpenItemId, AllocatedAmount = alloc.Amount });
    var op = await db.Set<OpenItem>().FirstAsync(o => o.Id == alloc.OpenItemId, ct);
    op.OpenAmount -= alloc.Amount;                                   // decimal(19,4)
    op.Status = op.OpenAmount <= 0m ? OpenItemStatus.Paid
              : OpenItemStatus.PartiallyPaid;                        // enum already exists
    var doc = await db.Set<SalesDocument>().FirstAsync(d => d.Id == op.DocumentId, ct);
    doc.AmountDue = op.OpenAmount;                                   // WHITELISTED column (trigger allows)
    if (op.Status == OpenItemStatus.Paid) doc.Status = DocumentStatus.Paid; // Paid=4 exists; status not frozen
}
await audit.RecordAsync(new SalesDocumentAuditEvent("payment.recorded", payment.Id, null, snapshot), ct);
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```
**Key:** `SalesDocument.AmountDue` and `Status` are BOTH mutable on a finalized document — the immutability trigger's whitelist explicitly permits `amount_due` and does not freeze `status`. `open_items` has **no** immutability trigger, so `open_amount`/`status` are freely mutable. Verified in `20260712151258_SalesDocuments.cs`.

### Pattern 3: Append-only booked fact (GoBD) via a blanket immutability trigger
**What:** A recorded payment is a Buchung; block UPDATE/DELETE at the DB (corrections = reversal payment). Simpler than the sales_documents status-guarded trigger because a payment has no draft state.
**Example:**
```sql
-- Mirror the audit_events append-only posture (see 20260710023110_AuditEvents.cs), applied to payment/payment_allocation:
CREATE FUNCTION payment_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'payment rows are append-only (GoBD); reverse, do not edit/delete'; END; $$;
CREATE TRIGGER payment_immutable BEFORE UPDATE OR DELETE ON payment
  FOR EACH ROW EXECUTE FUNCTION payment_immutable();
```
**DECISION:** confirm append-only vs. editable-until-X. Append-only is the GoBD-correct default and matches the repo's muscle memory; a reversal payment carries a negative amount + `reverses_payment_id` and its allocations restore `open_amount` + recompute status.

### Pattern 4: Dunning notice = new QuestPDF layout + reused delivery
**What:** A Mahnung is NOT an invoice — different content (reference to the original invoice number/date, overdue amount, Mahngebühr, Verzugszinsen, a NEW payment deadline, level-specific wording, "letzte Mahnung" escalation). So build a `DunningNoticeDocument : IDocument` mirroring `InvoiceDocument` (letterhead + logo live-read + `PdfLabels` culture-driven DE/EN), fed by a flat `DunningNoticeModel`. Store the rendered bytes on the `dunning_notice` row (or a `document_render`-style blob) and email via a `SendDunningNoticeJob` that mirrors `SendDocumentEmailJob` verbatim (own scope, `ICurrentTenant.SetTenant`, `[AutomaticRetry(3)]`, Api default queue).
**When to use:** issuing any Zahlungserinnerung/Mahnung.

### Pattern 5: Dunning level selection + idempotency
**What:** For each overdue OP (`due_date < today AND status IN (Open, PartiallyPaid)`), the next level = `current_dunning_level + 1`, issued only when `days_overdue >= config[nextLevel].days_after_due` and `last_dunned_on` is not today (and, recommended, respecting a per-level minimum gap). A partial unique index `(tenant_id, open_item_id, level)` on `dunning_notice` makes re-issuing the same level impossible.
**When to use:** `DunningService.SelectCandidates` + the run endpoint.

### Anti-Patterns to Avoid
- **Inflating the OP with fees.** The open item must equal the (immutable) invoice's receivable; Mahngebühren + Verzugszinsen are separate claims tracked on `dunning_notice`, summed into the letter's "Gesamt zu zahlen" only.
- **A scheduled multi-tenant Hangfire dunning job in v1.** No per-request tenant → RLS breaks. Use a user-initiated run.
- **Reading live master data into the notice legal content.** Follow the SnapshotReader discipline: the invoice reference/amounts come from the OP + the document's frozen snapshot, not a re-read of the partner. (The logo is the one permitted live read — presentation only.)
- **Nav-collection `.Add` for client-PK children.** Always `db.Add(...)` (the 03-11 bug).
- **A second unnamed `HasQueryFilter`.** Named filters only (`TenantFilter`) — an unnamed one silently overwrites tenancy.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Exact money math on amounts/fees/interest | ad-hoc `decimal` rounding | `Platform.Money` + `RoundingPolicy` (`decimal(19,4)`) | PLAT-09, golden-tested EN-16931 rounding; the OP already uses it |
| Cross-tenant isolation on new tables | app-level `WHERE tenant_id` | hand-written RLS policy in the migration (+ reflective query filter) | RLS is the primary control; app filter is defence-in-depth only |
| Immutability of booked facts | app-layer "don't update" checks | DB immutability trigger | GoBD requires DB-enforced, not app-enforced |
| PDF generation | HTML→PDF / a new lib | QuestPDF `IDocument` mirroring `InvoiceDocument` | Community license already wired; consistent letterhead/DE-EN |
| Email delivery + retry | raw SMTP / inline send | `IEmailSender` + Hangfire job mirroring `SendDocumentEmailJob` | proven enqueue-after-commit + tenant re-establishment + retry |
| Audit trail | custom log table | `IAuditWriter.RecordAsync` in the same tx | atomic, tenant/actor stamped, immutable `audit_events` |
| Background execution | `Task.Run` | Hangfire on the Api default queue | durable, retried, tenant-scoped per the LOCKED Pitfall-2 rule |

**Key insight:** Phase 6 is an *integration* phase — its risk is not novel algorithms but wiring new rows correctly into the existing RLS/audit/immutability/money invariants. Every "how do I…" already has a verbatim answer in Phases 3–5.

## Common Pitfalls

### Pitfall 1: `open_items` lacks the fields payments + dunning need
**What goes wrong:** The current `OpenItem` (`src/modules/Numera.Modules.Sales/OpenItem.cs`) has `OpenAmount` + `Status` (the payment seam) and Skonto snapshot fields, but **no dunning fields** and **no paid-amount/last-payment fields**. `partner_id` is nullable.
**Gaps the plans MUST close:**
- Add `current_dunning_level int NOT NULL DEFAULT 0` and `last_dunned_on date NULL` to `open_items` (ALTER in the wave-2 migration — no new RLS needed, the policy already covers the table).
- `paid_amount` is derivable (`original_amount - open_amount`); no column needed.
- Dunning recipient: prefer `partner_id` → fall back to the document's frozen `recipient_snapshot` e-mail/address (the `ResolveRecipientEmail` helper in `SalesDocumentEndpoints` shows how to read the jsonb snapshot). Do NOT require a live partner.
**How to avoid:** treat the OpenItem shape gap as an explicit task; keep the ALTER minimal + denormalized only for list/idempotency.

### Pitfall 2: Breaking the immutable invoice while recording money movement
**What goes wrong:** Finalized `sales_documents` are DB-frozen; touching a frozen business column raises. Payments must touch ONLY `amount_due` (whitelisted) and `status` (not in the frozen set). Fees must never touch the invoice at all.
**Why it happens:** instinct to "update the invoice total".
**How to avoid:** payments write `amount_due` + `status` only; fees live on `dunning_notice`; verify against the trigger body in `20260712151258_SalesDocuments.cs` (whitelist: `status, sent_at, finalized_at, cancelled_by_document_id, due_date, amount_due`).

### Pitfall 3: Multi-tenant scheduled dunning breaks RLS
**What goes wrong:** A Hangfire `RecurringJob` runs with no HTTP request → `ICurrentTenant.TenantId` is null → the tenant query filter yields zero rows (or a crash), and there's no clean way to iterate every tenant under least-privilege RLS.
**How to avoid:** make the dunning run **user-initiated** (an endpoint that computes candidates for the *current* tenant and issues, enqueuing per-notice send jobs that each `SetTenant`). Defer scheduled auto-dunning. This also matches the requirement's "Nutzer kann … erzeugen und versenden".

### Pitfall 4: Culture leakage in the notice PDF/amounts
**What goes wrong:** Money/dates formatted under the worker's ambient culture instead of the label set's `CultureInfo` → "300.00 €" in a German letter.
**How to avoid:** copy `InvoiceDocument`/`PdfLabels` exactly — every format call uses `_labels.Culture` (de-DE default). Verbatim frozen German texts stay German even under EN labels.

### Pitfall 5: Enum wire-drift on the frontend
**What goes wrong:** The Api has no `JsonStringEnumConverter`; C# enums cross the wire as **numbers**. The TS mirrors (`OpenItemStatus`, new `PaymentMethod`, dunning level) must match ordinals exactly, or badges/labels silently mismap.
**How to avoid:** append-only enum ordinals in C#; mirror them as `as const` numeric maps (see `lib/api/openItems.ts`, `lib/api/inbound.ts`). Add a comment noting the drift risk.

### Pitfall 6: Double-dunning / over-allocation
**What goes wrong:** Issuing the same Mahnstufe twice, or allocating more than an OP's open amount.
**How to avoid:** unique `(tenant_id, open_item_id, level)` on `dunning_notice` + skip when `last_dunned_on = today`; validate `sum(allocations per OP) <= open_amount` before persisting (FluentValidation + a service-level re-check inside the tx).

## Code Examples

### Reading the frozen recipient from an OP's document (dunning address)
```csharp
// Source: src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs (ResolveRecipientEmail)
using var json = JsonDocument.Parse(recipientSnapshot);
var root = json.RootElement;
if (root.TryGetProperty("email", out var e) || root.TryGetProperty("Email", out e)) { /* BT-43 */ }
// The recipient name/address live in the same jsonb (Name, LegalForm, BillingAddress{...}).
```

### The dunning-notice send job (mirror verbatim)
```csharp
// Source: src/Numera.Api/Jobs/SendDocumentEmailJob.cs — copy the scope + SetTenant + retry skeleton
[AutomaticRetry(Attempts = 3)]
public sealed class SendDunningNoticeJob {
  public async Task RunAsync(Guid tenantId, Guid dunningNoticeId, string language, CancellationToken ct = default) {
    using var scope = _scopeFactory.CreateScope();
    scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId); // RLS applies inside the job
    // load dunning_notice (RLS), render-if-absent DunningNoticeDocument, send via IEmailSender,
    // advance status + set SentAt, rethrow on failure for retry.
  }
}
```

### German statutory dunning numbers (for the fee/interest calc)
```text
Verzug (§286 BGB): B2B automatically 30 days after due date + receipt of invoice (§286 III), or after a Mahnung.
Verzugszinsen (§288 BGB): B2C = Basiszinssatz + 5 pp; B2B (no consumer) = Basiszinssatz + 9 pp.
Basiszinssatz = 1.27 % (Bundesbank, unchanged 1 Jan 2026) → B2B default rate 10.27 % p.a.
Verzugspauschale (§288 V): 40 € flat, B2B only, on top of interest.
Mahngebühren: no statutory amount — must be "angemessen" (actual cost of the reminder); common practice ≈ 2.50–10 € per level. KEEP CONFIGURABLE. There is NO legal duty to send a Zahlungserinnerung before a Mahnung.
```
Interest per notice ≈ `round(open_amount × rate% × days_overdue / 365, 2)` via `RoundingPolicy`. Store the rate used on the notice (rates change semi-annually) so the letter is reproducible.

### Sensible German default dunning ladder (used when a tenant has no config rows)
```text
Level 0  Zahlungserinnerung   +7 days after due    fee 0.00 €     no interest
Level 1  1. Mahnung           +14 days             fee 5.00 €     interest optional
Level 2  2. Mahnung           +28 days             fee 10.00 €    interest on
Level 3  3./letzte Mahnung    +42 days             fee 15.00 €    interest on + escalation text
```
All values are practice-based defaults, fully overridable per tenant (OPDN-03 requires configurable Stufen/Fristen/Gebühren).

## State of the Art

| Old Approach | Current Approach | When | Impact |
|--------------|------------------|------|--------|
| Gapless (lückenlos) legal numbering fears | einmalig + nachvollziehbar only | Phase 3 (locked) | Dunning notices need NO gapless legal number; reference the invoice number + Mahnstufe (a human reference like `MAHN-…` is optional nice-to-have, not required). Don't reuse `NumberingService` unless a reference number is explicitly wanted. |
| Basiszinssatz 0.62 % (2021 era) | **1.27 %** (unchanged since 1 Jul 2025, confirmed 1 Jan 2026) | 2026 | Persist the rate used on each notice; it is revised each Jan 1 / Jul 1. |

**Deprecated/outdated:** none relevant. QuestPDF `DocumentSettings.PdfA` is deprecated (use `ConformanceLevel`) — irrelevant here (notices are plain PDFs, not PDF/A).

## Open Questions

1. **Payment mutability model (GoBD).**
   - Know: a recorded payment is a booked fact; the repo enforces GoBD at the DB.
   - Unclear: append-only-with-reversal vs. editable-until-something.
   - Recommendation: **append-only + reversal** (blanket immutability trigger like `audit_events`); confirm in discuss-phase. Low cost either way in schema.

2. **Payment ↔ allocation shape.**
   - Know: Teilzahlungen = one OP by several payments (needs partial amounts).
   - Unclear: whether one payment must span multiple OPs in v1.
   - Recommendation: ship the `payment_allocation` join anyway (clean, future-proof for BANK-02); if scope-pressed, a single-allocation UX still uses the same schema.

3. **Skonto settlement.**
   - Know: `OpenItem` already snapshots `SkontoPercent/Days/DueDate/Amount`.
   - Unclear: whether a payment inside the Skonto window may settle the OP for `open_amount - skonto_amount` and write off the difference.
   - Recommendation: support a "mit Skonto" option on payment recording (write off `skonto_amount`, mark Paid) when `value_date <= skonto_due_date`; treat as **Claude's discretion / nice-to-have** — the core requirement is partial payments, not Skonto.

4. **Fees/interest ledger.**
   - Know: fees are Nebenforderungen, not part of the invoice OP.
   - Unclear: whether the tenant later wants fees as their own trackable receivables.
   - Recommendation: v1 record fee + interest on `dunning_notice` (shown on the letter), do NOT create OP rows for them; revisit when accounting (v2 BOOK-*) lands.

5. **Notice storage.**
   - Recommendation: store the rendered notice bytes either inline on `dunning_notice` (bytea) or in a `document_render`-style row; inline is simpler and matches `inbound_document`/`document_render` precedent. Planner's call.

## Sources

### Primary (HIGH confidence — the actual codebase)
- `src/modules/Numera.Modules.Sales/OpenItem.cs`, `SalesEnums.cs`, `SalesDocument.cs` — OP shape, `OpenItemStatus` (Open/PartiallyPaid/Paid/Cancelled), `DocumentStatus.Paid=4`
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` — `FinalizeCoreAsync`, `BuildOpenItem`, `ResolveRecipientEmail`, mutate→audit→SaveChanges, whitelisted `amount_due`/`status`, Storno closing the OP
- `src/platform/Numera.Platform.Db/Migrations/20260712151258_SalesDocuments.cs` — `open_items` schema, RLS pattern, the immutability trigger + whitelist
- `src/platform/.../Migrations/20260726200550_DocumentEInvoice.cs`, `20260710023110_AuditEvents.cs` — RLS + append-only trigger templates
- `src/Numera.Api/Jobs/SendDocumentEmailJob.cs`, `Services/DocumentPdfService.cs`, `Events/EnqueuePdfOnFinalize.cs`, `Program.cs` — Hangfire job + tenant re-establishment + DI + domain-event handler registration (multiple handlers per event supported)
- `src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs`, `Pdf/PdfLabels.cs`, `Pdf/SnapshotReader.cs` — QuestPDF DE/EN layout template
- `src/platform/Numera.Platform.Db/NumeraDbContext.cs`, `Numera.Platform.Audit/AuditWriter.cs` — reflective `ITenantEntity` discovery, named filters, in-tx audit
- `src/modules/Numera.Modules.Crm/BusinessPartner.cs`, `Numera.Modules.Sales/CompanyProfile.cs` — payment terms (`PaymentTermsNetDays`, `SkontoPercent/Days`), IBAN/BIC dunning seams
- `web/src/features/openItems/OpenItemsListPage.tsx`, `lib/api/openItems.ts`, `lib/api/inbound.ts`, `i18n/index.ts`, `App.tsx` — frontend conventions (server-paged table, numeric enum wire, DE-authoritative i18n, route/nav wiring)

### Secondary (MEDIUM-HIGH — German dunning law, cross-verified)
- Deutsche Bundesbank — Basiszinssatz 1,27 % zum 1. Januar 2026 (unverändert): https://www.bundesbank.de/de/presse/pressenotizen/bekanntgabe-des-basiszinssatzes-zum-1-januar-2026-basiszinssatz-bleibt-unveraendert-bei-1-27--973974
- § 288 BGB (Verzugszinsen + 40 € Pauschale): https://www.gesetze-im-internet.de/bgb/__288.html
- IT-Recht-Kanzlei — Zahlungsverzug im Geschäftsverkehr (Stand 2026): https://www.it-recht-kanzlei.de/zahlungsverzug-unternehmer.html
- IHK Region Stuttgart — Verzugszinsen berechnen: https://www.ihk.de/stuttgart/fuer-unternehmen/recht-und-steuern/wirtschaftsrecht/insolvenz-und-zwangsvollstreckung/verzugszins-676936

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — everything already exists and is proven in Phases 3–5; no new deps.
- Architecture/patterns: HIGH — every pattern is a verbatim mirror of shipped code with file citations.
- German dunning law: MEDIUM-HIGH — statutes + current Basiszinssatz cross-verified against Bundesbank + gesetze-im-internet; Mahngebühr amounts are practice-based (kept configurable).
- Open design decisions: flagged (payment mutability, allocation span, Skonto, fee ledger) — should be confirmed in discuss-phase or by the planner.

**Research date:** 2026-07-27
**Valid until:** ~2026-08-27 (stable; re-check Basiszinssatz after 2026-07-01 revision — value confirmed unchanged through Jan 2026, next revision Jul 2026 already relevant → verify the live rate at implementation time)
