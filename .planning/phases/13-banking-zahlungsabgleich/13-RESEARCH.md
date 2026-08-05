# Phase 13: Banking & Zahlungsabgleich - Research

**Researched:** 2026-08-05
**Domain:** PSD2/AIS bank connectivity (finAPI) + idempotent transaction sync + auto-reconciliation against v1 open items + booking via the Phase-10 payment seam + CSV/MT940/CAMT fallback
**Confidence:** HIGH on the in-repo booking/reconciliation seam and the Hangfire/RLS patterns (read from source); MEDIUM on finAPI operational specifics (verified against finAPI docs, but contract/tier is a vendor decision); MEDIUM on the .NET MT940/CAMT parser choice (multiple libs exist, none is a clear standard).

> **No CONTEXT.md exists** for this phase (no `/gsd:discuss-phase` run). The "User Constraints" section below is therefore populated from the **locked upstream milestone decisions** the orchestrator supplied, not from a discuss step. The genuine product/vendor decisions are flagged crisply in **Open Questions** for the orchestrator to lock with the user BEFORE planning.

---

## User Constraints (from locked upstream milestone decisions)

### Locked Decisions (do NOT re-derive or explore alternatives)
- **finAPI** (BaFin-licensed, Berlin-Group XS2A) is the PSD2/AIS aggregator. **RegShield / Web Form 2.0 → Numera needs NO own PSD2 licence.**
- **This phase is AIS only** (read transactions). SEPA credit transfer (finAPI **PIS**) = BANK-D1, **DEFERRED to v2.1**. No PIS, no `pain.001`, no outgoing money movement in Phase 13.
- **No official finAPI .NET SDK** → generate a typed client from finAPI's OpenAPI spec, or hand-write a thin typed `HttpClient`. (A community-generated C# client exists — see Standard Stack.)
- ⚠ **Do NOT** build on GoCardless/Nordigen (closed to new signups). Do NOT build own FinTS/HBCI or own PSD2 aggregation/screen-scraping.
- **Booking REUSES the v1 `PaymentService`** + the Phase-10 `PaymentPostingSource` (Bank↔Forderung). This phase feeds confirmed reconciliations INTO that path — **no new posting engine**.
- **NEVER auto-book a low-confidence match.** High-confidence → auto-suggest; low → human review queue (mirror the Belege/OCR "never auto-post" rule).
- Every new table gets RLS `ENABLE`+`FORCE`+tenant policy; sync jobs are Hangfire on the Worker calling `SetTenant`; money stays `decimal`.

### Claude's Discretion (research options, recommend)
- finAPI **live vs sandbox vs port-stub** rollout (mirror Phase-12's stub-default OCR pattern).
- Reconciliation **confidence scoring inputs + auto-confirm threshold** (or whether to auto-confirm at all).
- Dedup key design (finAPI transaction id vs content fingerprint).
- MT940/CAMT/CSV parser choice.
- Payable/Verbindlichkeit direction scope (see the gap analysis — this is the single biggest open scope question).

### Deferred Ideas (OUT OF SCOPE this phase)
- BANK-D1 SEPA credit transfer (PIS) → v2.1
- BANK-D2 learned booking rules → v2.1
- BANK-D3 FX reconciliation → v2.1
- BANK-D4 liquidity/cashflow view → v2.1
- BANK-D5 SEPA direct debit → v3

---

## Summary

Phase 13 has two hard parts and a lot of reuse. The **hard parts** are (1) the finAPI AIS integration — OAuth client-credentials + per-tenant finAPI user provisioning + the Web Form 2.0 bank-connection import (SCA redirect) + the 90-day consent/re-auth lifecycle + idempotent incremental transaction fetch — and (2) the reconciliation scoring engine. Everything downstream of a *confirmed* match is **reuse**: the v1 `PaymentService.RecordAsync` already books Bank↔Forderung inline (via the Phase-10 `PaymentPostingSource`), splits by `PaymentAllocation`, reduces `OpenItem.OpenAmount`, flips status, and updates the `SalesDocument`. A confirmed bank-transaction→open-item match is literally a `RecordPaymentRequest{ Amount, ValueDate, Method, Reference, Allocations[] }` call — **zero new posting logic** for the receivable direction.

The **one genuine gap** is the **payable (Verbindlichkeit / Kreditor) direction.** The v1 payment stack is receivable-only: `OpenItem` is tied to a `SalesDocument` (customer invoice), `PaymentPostingSource` hard-codes Bank(Debit)↔Debtor(Credit), and Phase-12 receipts book Aufwand+Vorsteuer/Kreditor directly with **no open payable and no "supplier payment made" (Verbindlichkeit Soll / Bank Haben) posting**. So an *outgoing* bank transaction paying a supplier invoice has nowhere to reconcile TO. The planner must decide whether Phase 13 (a) adds the payable side (a payable open-item + a `SupplierPaymentPostingSource`), or (b) scopes reconciliation to the receivable (incoming-payment) direction only and treats outgoing tx as "categorize/book to expense or ignore" for now.

The whole thing mirrors patterns already proven in Phase 12: an **`IBankConnectionProvider` port** (like `IReceiptExtractor`) with a **stub/CSV-import default** so the pipeline can ship and be tested without a live finAPI contract; a **Hangfire sync job that `SetTenant` before touching the RLS DbContext** (exactly like `ExtractReceiptJob` / `PollBelegMailboxJob`); an **idempotent dedup key + processed-marker table** (exactly like `ProcessedBelegeMail`); and a **human review/confirm queue** (exactly like the Receipt `Captured→Extracted→Reviewed→Booked` lifecycle with a `confirm-book` endpoint).

**Primary recommendation:** Build behind an `IBankConnectionProvider` port with a **CSV/CAMT-import + stub default**, land the `bank_account`/`bank_transaction` model + idempotent sync + reconciliation scoring + Prüf-Queue + confirm→`PaymentService.RecordAsync` seam first, and wire finAPI Web Form 2.0 live behind the port as a distinct plan (or a follow-on) once the vendor contract/tier is locked. Decide the payable-direction scope up front — it drives the data model.

---

## Standard Stack

### Core

| Library / Asset | Version | Purpose | Why standard |
|---------|---------|---------|--------------|
| **finAPI Access (XS2A) + Web Form 2.0 + RegShield** | current SaaS (contract) | AIS: bank-connection import, account + transaction retrieval, PSD2 consent/SCA | BaFin-licensed, 100% Berlin-Group; RegShield = licence-as-a-service so Numera is not an AISP. Locked upstream. |
| **Generated finAPI C# client** OR thin typed `HttpClient` | from finAPI OpenAPI (docs.finapi.io) | Typed REST access from Worker/Api | finAPI ships no official .NET SDK. A **community-generated client exists**: `github.com/eciboadaptech/finapi-access` (eciboadaptech = finAPI/adaptech parent org) — usable as-is or as a generation reference. Wrap behind the `IBankConnectionProvider` port either way. |
| **Hangfire** (already in stack) | Postgres-backed | Periodic per-tenant transaction sync + consent-health checks | Same Worker tier + `[Queue("worker")]` + `SetTenant` pattern proven by `PollBelegMailboxJob`. |
| **v1 `PaymentService`** (in repo) | — | Books confirmed reconciliations (receivable direction) | `src/Numera.Api/Services/PaymentService.cs` — reuse `RecordAsync`/`ReverseAsync` verbatim; no new posting engine. |
| **NodaMoney / `decimal`** (already in stack) | — | Parse finAPI/CSV amounts to decimal at the boundary | Never `double` on a money path (PITFALLS #17). finAPI returns decimal-shaped amounts; parse explicitly. |

### Supporting (CSV/MT940/CAMT fallback — BANK-06)

| Library | Version | Purpose | When to use |
|---------|---------|---------|-------------|
| **Money.Unifi** (`chstorb/Money`) | current | ISO 20022 / **CAMT.053** (Bank-to-Customer Statement) typed parsing | Preferred for CAMT.053 — it is the modern format banks emit; ISO 20022 is the future (MT940 sunset ~2027-2028). |
| **SharpMt940Lib.Core** (`mjebrahimi`) or **Raptorious.Finance.Swift.Mt940** or **Livo.MT940Parser** | current | **MT940** SWIFT statement parsing (multi-bank quirks) | For the MT940 fallback. All three are small/unmaintained-ish; pick one, wrap it, and pin. MT940 has per-bank variance — expect to tolerate non-standard `:86:` fields. |
| **CsvHelper** (verify if already in stack) or hand-parse | current | CSV statement import | CSV has no standard schema — needs a per-bank/user column mapping UI or a documented expected layout. Cheapest to hand-parse a defined layout. |
| **System.Xml / XSD-validated read** | built-in | CAMT.053 as an alternative to Money.Unifi | CAMT.053 is XML; if the lib is unsatisfactory, read against the pain/camt XSD directly. |

**Recommendation:** All three fallback formats converge into the SAME `bank_transaction` ingest (see Architecture). Do **CAMT.053 first** (modern, structured, best data), **MT940 second** (still common from German banks), **CSV last** (messiest — gate behind a mapping step). Wrap each parser behind a `IBankStatementImporter` returning normalized `BankTransactionDraft`s so the reconciliation pipeline is format-agnostic.

**Installation (indicative — verify exact package IDs/versions at plan time):**
```bash
dotnet add Numera.Modules.Banking package <generated-finapi-client-or-none>
dotnet add Numera.Modules.Banking package SharpMt940Lib.Core        # MT940
# CAMT.053: reference chstorb/Money (Money.Unifi) or read XSD via System.Xml
# Hangfire, NodaMoney, MailKit already referenced — no new adds
```

### Alternatives Considered

| Instead of | Could use | Tradeoff |
|------------|-----------|----------|
| finAPI | Enable Banking / Tink / TrueLayer | Only if Numera goes pan-EU; finAPI is locked for DE + RegShield. Do NOT reopen. |
| finAPI | GoCardless/Nordigen | **Never — closed to new signups.** |
| Generated finAPI client | Thin hand-written `HttpClient` | Hand-written is fewer moving parts for the ~6 AIS endpoints actually needed (token, user, webform import, accounts, transactions, consent); generated client is more complete but adds a build/codegen step. **Recommend thin hand-written client** given the narrow AIS surface. |
| CAMT lib | Hand-parse XSD | Lib saves time but adds a dependency; XSD-read is dependency-free but more code. |

---

## Architecture Patterns

### The booking anchor — EXACT PaymentService reuse seam (BANK-04)

Verified from `src/Numera.Api/Services/PaymentService.cs` and `src/modules/Numera.Modules.Ledger/Posting/PaymentPostingSource.cs`:

**`PaymentService.RecordAsync(RecordPaymentRequest request, CancellationToken ct)` — this is the confirm seam.** In ONE DB transaction it:
1. Validates `Amount > 0`, ≥1 allocation, `Sum(allocations) == Amount`, method defined, each allocation ≤ `OpenItem.OpenAmount`, and each target `OpenItem.Status ∈ {Open, PartiallyPaid}`.
2. Inserts a `Payment` (`Amount`, `ValueDate` (DateOnly), `Method` (PaymentMethod enum), `Reference` (nullable string — put the Verwendungszweck / finAPI end-to-end id here), `RecordedAt`).
3. Inserts N `PaymentAllocation` rows (`PaymentId`, `OpenItemId`, `AllocatedAmount`).
4. For each grouped `OpenItemId`: `OpenItem.OpenAmount -= amount`; sets `Status = OpenAmount<=0 ? Paid : PartiallyPaid`; loads the `SalesDocument` and sets `AmountDue = OpenAmount`, `Status = Paid` when cleared.
5. Calls `PostRecordedPaymentAsync` → resolves the tenant `ChartVariant` from `LedgerSettings`, resolves each debtor's `BusinessPartner.DebtorAccount` override, builds a `PaymentPostingInput(BankAccount: null, ValueDate, allocations, IsReversal:false)` and posts via `PaymentPostingSource` → **Bank(Debit) ↔ Debtor/Forderung(Credit)**. Idempotency-guarded by `JournalEntry.SourceType==Payment && SourceRef==payment.Id`.
6. Writes a `payment.recorded` audit event; commits.

**`RecordPaymentRequest`** (contract) carries: `decimal? Amount`, `DateOnly ValueDate`, `PaymentMethod Method`, `string? Reference`, `IReadOnlyList<PaymentAllocationInput> Allocations` where `PaymentAllocationInput(Guid OpenItemId, decimal Amount)`.

**So a confirmed reconciliation is:**
```
RecordAsync(new RecordPaymentRequest {
    Amount = tx.Amount,                       // decimal, positive incoming
    ValueDate = tx.ValueDate,                 // DateOnly from bank booking/value date
    Method = PaymentMethod.BankTransfer,      // wire 0
    Reference = tx.EndToEndId ?? tx.Purpose,  // Verwendungszweck for the audit trail
    Allocations = [ new(openItemId, allocatedAmount), ... ]  // 1..N — split supported natively
})
```
**Split (BANK-05) is native**: N allocations across M open items is exactly what `PaymentAllocation` + `RecordAsync` grouping already do. **Correction/un-match (BANK-05)**: reuse `PaymentService.ReverseAsync(paymentId)` — it creates a negative `Payment` + negative allocations, restores `OpenItem.OpenAmount`, reopens the `SalesDocument`, and posts the Storno leg. The `bank_transaction` should store the resulting `PaymentId` so un-matching a transaction = `ReverseAsync(that PaymentId)` + clearing the tx's matched state.

### THE PAYABLE-DIRECTION GAP (must decide before planning)

Verified: **the payable side does not exist.**
- `OpenItem` (`src/modules/Numera.Modules.Sales/OpenItem.cs`) is a **receivable** created from a finalized `SalesDocument` (customer invoice). There is no payable open-item entity.
- `PaymentPostingSource` hard-codes `Bank` (Debit on record) ↔ `Debtor` (Credit). There is **no** Verbindlichkeit/Kreditor leg.
- Phase-12 `ReceiptEndpoints.ConfirmBookAsync` books supplier invoices via `ExpensePostingSource` (Aufwand + Vorsteuer / **Kreditor**, `LedgerSourceType.Expense`) — but creates **no open payable** and there is **no "supplier payment made" (Verbindlichkeit Soll / Bank Haben)** posting anywhere.
- `BusinessPartner` has `CreditorAccount` + `DebtorAccount` + `Iban`/`Bic` (good — enables counterparty matching both ways), and `IsSupplier`/`IsCustomer` role flags.

**Implication:** For **incoming** bank tx → customer-invoice receivable, everything reuses cleanly. For **outgoing** bank tx → supplier-invoice payable, Phase 13 would need NEW building blocks: a payable open-item (or generalize `OpenItem` with a direction), a `SupplierPaymentPostingSource` (Kreditor Debit ↔ Bank Credit), and a `SupplierPaymentService` sibling. This also potentially unlocks the **Phase-12-deferred EÜR Betriebsausgabe-on-payment** (under Ist/cash-basis, the expense hits EÜR when *paid*, i.e. at reconciliation, not at receipt booking). **Recommendation:** flag as OPEN QUESTION — likely scope the payable direction to a **thin add** (payable open-item + supplier-payment posting) OR defer it and ship receivable-only reconciliation in Phase 13. Do not silently assume it.

### Recommended module structure

```
src/modules/Numera.Modules.Banking/          # NEW module (mirror Modules.Sales/Belege layout)
├── BankAccount.cs               # tenant bank account (RLS), finapi ids, consent state
├── BankTransaction.cs           # normalized umsatz (RLS), dedupe key, match state, PaymentId link
├── BankConnection.cs            # per-tenant finAPI connection + consent lifecycle (RLS)
├── IBankConnectionProvider.cs   # PORT (like IReceiptExtractor) — Import/Sync/Consent
├── StubBankConnectionProvider.cs# default (no live finAPI) — enables CSV-only operation
├── Reconciliation/
│   ├── MatchCandidate.cs        # (openItem, score, reasons)
│   ├── ReconciliationScorer.cs  # amount + reference + counterparty → confidence
│   └── MatchStatus.cs           # Unmatched / Suggested / Review / Confirmed / Ignored
└── Import/
    ├── IBankStatementImporter.cs
    ├── Camt053Importer.cs · Mt940Importer.cs · CsvImporter.cs
    └── BankTransactionDraft.cs  # normalized pre-persist shape
src/Numera.Api/Services/FinApiBankConnectionProvider.cs   # live finAPI impl behind the port
src/Numera.Api/Jobs/SyncBankTransactionsJob.cs            # Hangfire, SetTenant, incremental
src/Numera.Api/Jobs/CheckBankConsentJob.cs                # consent-expiry health/notify
src/Numera.Api/Endpoints/BankAccountEndpoints.cs          # connect/list/consent
src/Numera.Api/Endpoints/BankTransactionEndpoints.cs      # list/queue/match/split/confirm/ignore
```

### Pattern 1: `IBankConnectionProvider` port with stub default (mirror Phase-12 OCR)
**What:** An interface for `ImportBankConnection` (returns a Web Form URL for SCA redirect), `SyncTransactions(sinceCursor)`, `GetConsentStatus`. `StubBankConnectionProvider` returns empty/no-op so the CSV/CAMT import path + reconciliation + booking all work and are testable with **no finAPI contract**. `FinApiBankConnectionProvider` is the live impl, DI-swapped by config — exactly how `StubReceiptExtractor` vs `AzureReceiptExtractor` are wired behind `IReceiptExtractor`.
**When:** Always — lets Phase 13 ship + be verified before/without the vendor onboarding completing.

### Pattern 2: Hangfire sync job with `SetTenant` (verified pattern)
**What:** Per `ExtractReceiptJob` / `PollBelegMailboxJob`: `using var scope = _scopeFactory.CreateScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);` BEFORE resolving `NumeraDbContext` — the `TenantConnectionInterceptor` then sets `app.current_tenant` for RLS inside the job. Mark `[Queue("worker")] [AutomaticRetry(Attempts=3)] [DisableConcurrentExecution(...)]`. The Worker (`src/Numera.Worker/Program.cs`) runs only the `"worker"` queue and registers recurring jobs via `IRecurringJobManager.AddOrUpdate(...)` with a cron.
**When:** For the periodic transaction sync (fan-out one job per connected tenant/account, staggered to respect finAPI rate limits — PITFALLS performance-trap "thundering herd") and the consent-health check.

### Pattern 3: Idempotent dedup + processed-marker (verified pattern)
**What:** `PollBelegMailboxJob` dedupes on `ProcessedBelegeMail.MessageId || ContentHash` before ingesting. Mirror it: each `BankTransaction` gets a stable **dedupe key**. Prefer finAPI's transaction id when present; where absent/unstable (CSV/MT940), compute a SHA-256 fingerprint over `(accountId, bookingDate, valueDate, amount, normalizedPurpose, counterpartyIban)`. **Upsert on (tenantId, bankAccountId, dedupeKey)** with a unique index. Track a **per-connection cursor** (last synced date/id) for incremental fetch. Re-running any window = zero duplicates (PITFALLS #7 + "Looks Done But Isn't" checklist). Note the finAPI **89-day** download window (below) — the cursor must handle overlapping re-fetches gracefully, which the dedupe key guarantees.

### Pattern 4: Review queue = the Receipt lifecycle, applied to matches (mirror Phase-12)
**What:** Receipts flow `Captured → Extracted → Reviewed → Booked` with a `confirm-book` endpoint that refuses to book unless `Status==Reviewed` and is idempotency-guarded on the `JournalEntry`. Mirror for transactions: `Unmatched → Suggested(high conf) / Review(low conf) → Confirmed(booked) / Ignored`. **Confirm** calls `PaymentService.RecordAsync`. **Never** transition to Confirmed without human action below the auto-confirm bar (locked rule). Store `matched OpenItemId(s) + allocation amounts + score + PaymentId` on the transaction.

### finAPI AIS flow (BANK-01) — verified specifics
- **Auth:** OAuth2 client-credentials (`client_id`/`client_secret`) for the app token; **per-tenant finAPI user** (finAPI "user" ≈ one Numera tenant) with its own user-scoped access token. Store finAPI user credentials/tokens **encrypted at rest, per tenant** — NEVER store the end-user's bank login (PSD2/DSGVO; finAPI holds the consent). 
- **Connect:** **Web Form 2.0 "Import a new Bank Connection" (recommended)** → finAPI returns a **web-form URL**; Numera redirects the user there; finAPI renders the bank pick + SCA/consent; on completion the connection (and its accounts) exist in finAPI. Only bank-connection-level import exists (no per-account import).
- **Fetch:** list accounts, then transactions per account with paging + a "since" filter for incremental sync. finAPI limits the **transaction download range to 89 days** and **RECURRING consent is valid ~90 days / up to 4 downloads** — so scheduled syncs must run comfortably inside that window.
- **Consent/re-auth (recurring UX obligation, NOT one-time):** when the last bank authorization is >90 days old the user must re-do SCA via an **"Update a Bank Connection" Web Form**. Track consent validity per connection; a `CheckBankConsentJob` proactively flags "Zustimmung läuft ab am …" and surfaces a one-click re-consent (connection-health widget). Silent staleness is the #1 banking UX failure (PITFALLS #9).
- **Webhooks/notifications:** finAPI can push notifications (new transactions / consent state) — optional; polling is sufficient for MVP. If used, the handler must `SetTenant` from the mapped connection and verify authenticity.
- **Sandbox ≠ prod:** different base URLs + mock test banks in sandbox; real-bank field variance + rate limits only appear in prod. Test a real bank in a staging tenant before GA.

### Reconciliation scoring (BANK-03)
Candidate-filter first (index by amount/date), then score — do NOT compare every tx against every open item (N×M perf trap). Scoring inputs, in signal-strength order:
1. **Reference match (strongest):** regex the `SalesDocument.DocumentNumber` / customer number out of the Verwendungszweck (`BankTransaction.Purpose`) and finAPI end-to-end id. Exact document-number hit = very high.
2. **Amount match:** exact `tx.Amount == OpenItem.OpenAmount` (highest); tolerant for Skonto/partial/rounding (lower — and partials must stay a *proposal*, never auto-cleared — PITFALLS #8).
3. **Counterparty match:** `tx.counterpartyIban == BusinessPartner.Iban`, or fuzzy name match → the open item's `PartnerId` (via `SalesDocument.PartnerId`).
Combine into a 0–1 **confidence**. **Tiering:** exact amount **AND** exact reference → high (auto-suggest, and *candidate* for auto-confirm if the user opts in — see Open Questions); otherwise → Prüf-Queue. Query candidate open items from `OpenItem WHERE Status ∈ {Open, PartiallyPaid}` (there is an index on `(TenantId, Status, DueDate)`), using `OpenAmount` as the target. Handle 1:N (Sammelüberweisung) and N:1 (Teilzahlung) via allocations.

### Anti-Patterns to Avoid
- **Auto-booking a low/partial-confidence match** — locked rule; corrupts the immutable ledger.
- **Keying dedup on `(date, amount, text)` alone** — legitimate identical charges collide (PITFALLS #7). Use provider id or a fuller fingerprint.
- **Running the sync job without `SetTenant`** — bypasses RLS (PITFALLS #16). Every job sets tenant first.
- **Storing bank amounts as `double`, or finAPI end-user bank credentials** — decimal only; finAPI holds credentials/consent.
- **N×M reconciliation scan** — candidate-filter by amount/date/doc-number index first.

---

## Don't Hand-Roll

| Problem | Don't build | Use instead | Why |
|---------|-------------|-------------|-----|
| PSD2 bank aggregation / SCA / consent | Own FinTS/HBCI or screen-scraper | finAPI AIS + Web Form 2.0 | Unlicensed + liability + per-bank breakage. Locked. |
| Bank↔Forderung booking | New posting engine | `PaymentService.RecordAsync` + `PaymentPostingSource` | Already books + splits + closes open items + audits, in one tx. |
| Payment reversal/un-match | Manual open-item edits | `PaymentService.ReverseAsync` | Append-only Storno + open-amount restore already implemented. |
| Split payment across invoices | Custom join logic | `PaymentAllocation` (N per payment) | Native many-to-many already modelled. |
| Per-tenant background isolation | Ad-hoc tenant plumbing | Hangfire `[Queue("worker")]` + `SetTenant` + `TenantConnectionInterceptor` | Proven RLS-safe pattern in 6+ jobs. |
| Idempotent ingest | Re-check-by-scanning | Dedupe key + unique index + processed-marker | `ProcessedBelegeMail` precedent. |
| MT940/CAMT parsing | Byte-level SWIFT/XML parser | SharpMt940Lib.Core / Money.Unifi | SWIFT `:86:` and camt namespaces are fiddly and bank-variant. |

**Key insight:** The value of Phase 13 is the finAPI feed + the scoring engine. Almost everything after "user confirms a match" is already built — the phase is mostly *plumbing a new source into an existing sink*.

---

## Common Pitfalls

### Pitfall 1: Non-idempotent import → duplicate Umsätze
**What:** Same tx imported twice (re-sync, overlapping 89-day windows, webhook retry) → double-matched open items, drifting bank balance.
**Avoid:** Dedupe key (finAPI id or fingerprint) + unique `(tenantId, bankAccountId, dedupeKey)` index + per-connection cursor. Re-run any window = 0 dupes.
**Warning signs:** Numera bank balance diverges from real balance; two rows with identical provider id.

### Pitfall 2: Consent silently expires → stale data
**What:** 90-day consent lapses, sync stops, reconciliation rots, no user prompt.
**Avoid:** Track consent validity per connection; `CheckBankConsentJob` + connection-health UI + one-click re-consent Web Form. Show "letzter erfolgreicher Abruf" / "Zustimmung läuft ab am".
**Warning signs:** Aging last-success timestamps; "meine Bank aktualisiert nicht" tickets.

### Pitfall 3: Sandbox works, prod breaks
**What:** Mock banks hide real-bank field variance, rate limits, consent quirks.
**Avoid:** Real bank in a staging tenant before GA; handle rate limits with staggered/queued syncs + backoff.

### Pitfall 4: Auto-clearing a partial/ambiguous match
**What:** €500 tx auto-matched to a €500 open item actually paid in two €250 tranches; or a fuzzy purpose-text match auto-clears the wrong invoice.
**Avoid:** Auto-suggest only on exact amount + reference; everything else is a *proposal*. Un-match must restore the open item exactly (`ReverseAsync`).

### Pitfall 5: New tables without RLS / job without tenant context
**What:** `bank_account`/`bank_transaction`/`bank_connection` leak cross-tenant, or the sync job runs unscoped.
**Avoid:** Every table gets `ENABLE`+`FORCE`+`tenant_isolation` policy (verified migration pattern below) and joins the Cross-Tenant test suite in the SAME phase; every job `SetTenant` first. Bank data is the worst-case breach — add targeted cross-tenant tests.

### Pitfall 6: `double` on the money path
**What:** finAPI/CSV amount parsed as float → cents drift → Soll≠Haben.
**Avoid:** Parse to `decimal` at the boundary; reuse v1 rounding.

---

## Code Examples

### RLS migration pattern (verified — from `20260804152146_Receipts.cs`)
```csharp
// Every new banking table:
migrationBuilder.Sql("ALTER TABLE bank_transaction ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE bank_transaction FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql("""
    CREATE POLICY tenant_isolation ON bank_transaction
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
    """);
// If append-only semantics wanted (e.g. raw imported umsatz): REVOKE UPDATE,DELETE FROM numera_app
// + a BEFORE UPDATE OR DELETE trigger raising an exception (see receipt_archive precedent).
```

### Hangfire sync job skeleton (verified pattern — from `ExtractReceiptJob` / `PollBelegMailboxJob`)
```csharp
[Queue("worker")]
[AutomaticRetry(Attempts = 3)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class SyncBankTransactionsJob
{
    public async Task RunAsync(Guid tenantId, Guid bankAccountId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId); // RLS first
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IBankConnectionProvider>();
        var cursor = /* load per-connection cursor */;
        await foreach (var draft in provider.SyncTransactionsAsync(bankAccountId, cursor, ct))
        {
            var key = draft.ProviderId ?? Fingerprint(draft);   // dedupe key
            if (await db.Set<BankTransaction>().AnyAsync(t => t.DedupeKey == key, ct)) continue;
            db.Add(BankTransaction.FromDraft(draft, key));       // decimal amounts
        }
        // advance cursor; SaveChanges; (reconciliation scoring can run here or in a follow-on job)
    }
}
```

### Confirm-match → book (the reuse seam)
```csharp
// In a BankTransactionEndpoints "confirm" handler, after human review:
var result = await paymentService.RecordAsync(new RecordPaymentRequest {
    Amount = tx.Amount,
    ValueDate = tx.ValueDate,
    Method = PaymentMethod.BankTransfer,
    Reference = tx.EndToEndId ?? tx.Purpose,
    Allocations = confirmedAllocations   // [(openItemId, amount), ...]  1..N
}, ct);
// then: tx.PaymentId = result.PaymentId; tx.MatchStatus = Confirmed;  (guard idempotently)
```

---

## State of the Art

| Old approach | Current approach | When changed | Impact |
|--------------|------------------|--------------|--------|
| FinTS/HBCI direct | PSD2 XS2A via licensed aggregator (finAPI) | PSD2 RTS 2019+ | No own licence; consent lifecycle is mandatory. |
| PSD2 90-day re-consent | EBA RTS amendment extended AIS renewal 90→180d (bank-dependent) | 2022 | Banks vary; finAPI still enforces ~90d recurring consent + 89-day download window — model per finAPI, don't assume 180. |
| MT940 SWIFT statements | ISO 20022 CAMT.053 | coexistence ends ~2027-2028 | Prefer CAMT.053 for the fallback; keep MT940 for banks still on it. |

**Deprecated/outdated:** GoCardless/Nordigen (closed to new signups); Web Form v1 (use **Web Form 2.0**).

---

## Open Questions

Crisp product/vendor decisions for the orchestrator to lock with the user BEFORE planning:

1. **finAPI live vs sandbox vs port-stub for THIS phase.**
   - Known: no live contract confirmed in-repo; Phase-12 shipped OCR stub-default and wired Azure behind `IReceiptExtractor`.
   - Unclear: is the finAPI contract/tier signed, or should Phase 13 ship the **`IBankConnectionProvider` port + CSV/CAMT import + stub default** and defer live finAPI to a follow-on plan?
   - Recommendation: **port + stub + fallback-import first**, live finAPI behind the port (separate plan or later task). Lock this — it changes the phase's dependency on vendor onboarding.

2. **Payable / Verbindlichkeit direction — in or out of Phase 13?** *(biggest scope driver)*
   - Known: the payable side does NOT exist (OpenItem is receivable-only; no supplier-payment posting; receipts book expense with no open payable).
   - Options: (a) add a payable open-item + `SupplierPaymentPostingSource` (Kreditor↔Bank) now (also unlocks Phase-12-deferred EÜR-Betriebsausgabe-on-payment); (b) ship **receivable-only** reconciliation in Phase 13, outgoing tx = categorize/ignore, defer payables.
   - Recommendation: default to (b) receivable-only unless the user needs supplier-payment reconciliation now; if (a), it's a bounded add.

3. **Auto-confirm threshold — auto-book at very high confidence, or ALWAYS human-confirm?**
   - Known: locked rule = never auto-book LOW confidence; high-confidence = auto-*suggest*.
   - Unclear: does the user want a very-high-confidence tier (exact amount + exact document number) to auto-*confirm* (book without a click), or must every match be human-confirmed like Belege?
   - Recommendation: **always human-confirm for launch** (safest, mirrors Belege); add opt-in auto-confirm later.

4. **PSD2 consent/SCA re-auth UX.**
   - Known: ~90-day recurring consent, Web Form "Update a Bank Connection" for re-auth.
   - Decisions: how proactively to warn (days before expiry), whether to email vs in-app only, and the connection-health widget scope.

5. **finAPI credential storage model.**
   - Known: per-tenant finAPI user + tokens, encrypted at rest, never store end-user bank login.
   - Decision: confirm the encryption-at-rest mechanism for the finAPI user secret/token (reuse an existing secret store? column encryption?) and the per-tenant finAPI-user provisioning trigger (on first connect vs at tenant creation).

6. **Fallback-import scope for BANK-06.** All of CSV + MT940 + CAMT.053, or a subset for launch? CSV needs a per-bank column-mapping UI (messiest). Recommendation: **CAMT.053 + MT940** at launch, CSV behind a defined-layout importer if time permits.

7. **SEPA/PIS confirmation.** Confirm PIS (outgoing transfer, BANK-D1) stays fully OUT of Phase 13 (locked as v2.1) — so no `pain.001`, no PIS endpoints, no outgoing-money UX. (Assumed yes; confirming avoids scope creep.)

---

## Sources

### Primary (HIGH confidence — read from repo)
- `src/Numera.Api/Services/PaymentService.cs` — `RecordAsync`/`ReverseAsync` signatures, allocation grouping, open-item + SalesDocument mutation, inline `PaymentPostingSource` call, `JournalEntry` idempotency guard, audit.
- `src/modules/Numera.Modules.Ledger/Posting/PaymentPostingSource.cs` + `ExpensePostingSource.cs` + `AccountResolver.cs` — Bank↔Debtor legs; expense Aufwand+Vorsteuer/Kreditor; no supplier-payment leg.
- `src/modules/Numera.Modules.Sales/OpenItem.cs`, `Payments/{Payment,PaymentAllocation,PaymentMethod}.cs` — receivable-only OpenItem; append-only Payment; N:M allocations; PaymentMethod wire ordinals.
- `src/modules/Numera.Modules.Crm/BusinessPartner.cs` — `Iban`/`Bic`/`DebtorAccount`/`CreditorAccount`/role flags (counterparty-match inputs).
- `src/Numera.Api/Jobs/{ExtractReceiptJob,PollBelegMailboxJob}.cs`, `src/Numera.Worker/Program.cs` — `SetTenant`-before-DbContext pattern, `[Queue("worker")]`, `ProcessedBelegeMail` dedupe, recurring-job registration.
- `src/Numera.Api/Endpoints/ReceiptEndpoints.cs` (ConfirmBookAsync) + `PaymentEndpoints.cs` — review→confirm-book lifecycle + idempotency guard; payment record/reverse/list endpoints.
- `src/platform/.../Migrations/20260804152146_Receipts.cs` — verbatim RLS ENABLE/FORCE/policy + append-only REVOKE + immutability trigger pattern.
- `.planning/research/{SUMMARY,STACK,FEATURES,PITFALLS}.md` — locked stack + reconciliation pitfalls (idempotency, consent, N×M, RLS, money precision).

### Secondary (MEDIUM — finAPI/parser docs, verified via web)
- finAPI documentation & XS2A support: [Import a new Bank Connection — Web Form 2.0](https://documentation.finapi.io/access/import-a-new-bank-connection-with-web-form-2-0-rec), [Update a Bank Connection — Web Form 2.0](https://documentation.finapi.io/access/update-a-bank-connection-for-web-form-2-0-customer), [Access transactions older than 90 days](https://xs2a-support.finapi.io/hc/en-us/articles/360017364060-13-How-to-access-transactions-older-than-90-days-from-current-date-), [finAPI PSD2 Web Form](https://finapi.zendesk.com/hc/en-us/articles/360002596391-finAPI-PSD2-Web-Form) — RECURRING consent ~90d / 4 downloads, 89-day download range, SCA re-auth.
- Community finAPI C# client: [github.com/eciboadaptech/finapi-access](https://github.com/eciboadaptech/finapi-access) (BankConnectionsApi docs).
- .NET parsers: [SharpMt940Lib.Core](https://www.nuget.org/packages/SharpMt940Lib.Core), [Raptorious.Finance.Swift.Mt940](https://www.nuget.org/packages/Raptorious.Finance.Swift.Mt940), [Livo.MT940Parser](https://www.nuget.org/packages/Livo.MT940Parser), [chstorb/Money (ISO 20022 / CAMT.053)](https://github.com/chstorb/Money); [MT940 vs CAMT.053 format guide](https://invoicedataextraction.com/blog/mt940-camt053-bank-statement-format-guide).

## Metadata

**Confidence breakdown:**
- Booking/reuse seam (PaymentService/OpenItem/PaymentAllocation/reversal): **HIGH** — read verbatim from source.
- Data model + Hangfire/RLS/dedupe patterns: **HIGH** — direct precedent in Phase-12 code.
- Payable-direction gap: **HIGH** — verified absent in code.
- finAPI AIS/Web Form 2.0/consent specifics: **MEDIUM** — finAPI docs; contract tier + exact webhook/notification behaviour is a vendor decision to confirm at onboarding.
- MT940/CAMT/CSV parser choice: **MEDIUM** — several viable libs, none dominant; validate against real German bank exports.

**Research date:** 2026-08-05
**Valid until:** ~2026-09-05 (finAPI API + parser packages are moderately stable; re-verify finAPI consent-window + package versions at plan time).
