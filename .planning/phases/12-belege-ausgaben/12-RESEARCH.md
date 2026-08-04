# Phase 12: Belege & Ausgaben - Research

**Researched:** 2026-08-04
**Domain:** Receipt capture + OCR extraction + GoBD WORM archive + expense posting + supplier match/dedup + per-tenant email intake — added to the existing .NET 10 + Postgres-RLS modular monolith
**Confidence:** HIGH on every codebase seam (read from source this session — posting engine, inbound e-invoice pipeline, WORM immutability template, MailKit, Hangfire tenant-context); HIGH on the two-tier architecture (locked upstream); MEDIUM on the exact Azure Document Intelligence field/confidence shape and EU pricing (verified against Microsoft Learn + NuGet, but the live provider decision + accuracy on German receipts is a genuine business spike). The cloud-OCR provider (live vs. stub) and mailbox infrastructure are the two real OPEN QUESTIONS.

## Summary

Phase 12 is **not** a new accounting engine — it is the **runtime entry point** that feeds an expense-posting rule that **already exists and is tested** from Phase 10. `ExpensePostingSource` + `ExpensePostingInput` are in the Ledger module today, exercised only by unit tests (`LedgerPostingEngineTests`); this phase wires the human-review **confirm → book** flow that builds an `ExpensePostingInput` and calls `PostingEngine.PostAsync` inside a transaction, exactly mirroring the invoice hook (`SalesDocumentEndpoints.PostInvoiceAsync`) and the payment hook (`PaymentService`). Booking an expense also unlocks the Phase-11-deferred USt-VA **Vorsteuer Kz 66** and real **EÜR Betriebsausgaben** — those report readers already look for the Vorsteuer accounts (`1576/1571/1406/1401`), they were simply never populated because no expense entry point existed.

The domain splits cleanly into **two ingest tiers** (locked upstream). Tier A: an incoming **XRechnung/ZUGFeRD** e-invoice is parsed by the **existing Phase-5 `InboundParser`** (`InvoiceDescriptor.Load` via ZUGFeRD-csharp + PdfPig embedded-XML extraction) — **zero OCR**, high accuracy, structured `BreakdownRows` (TaxCategory, VatRatePercent, TaxableBase, TaxAmount) that map directly onto `ExpensePostingInput`. The `inbound_document` table + `InboundEInvoiceService` + `SupplierMatcher` **already do 90% of Tier A** — this phase adds the booking-proposal + confirm-and-book layer on top of it. Tier B: a photographed/scanned/PDF receipt goes through a new **`IReceiptExtractor` port** (cloud adapter = **Azure AI Document Intelligence `prebuilt-invoice`/`prebuilt-receipt`**, EU region + DPA; self-hosted PaddleOCR/docTR escape hatch) that returns the same structured fields **with per-field confidence** for human review. **Never auto-book** — OCR/e-invoice both *propose*; a human confirms.

The **GoBD WORM archive** reuses the **proven append-only pattern verbatim**: store original bytes as Postgres `bytea` (exactly like `customer_files` and `inbound_document.OriginalBytes`), enforce immutability with the `customer_files` migration template (ENABLE+FORCE RLS + `tenant_isolation` policy + `REVOKE UPDATE,DELETE FROM numera_app` + a `BEFORE UPDATE OR DELETE` RAISE-EXCEPTION trigger), index the metadata, link to the resulting `JournalEntry`. **Email intake** is net-new: MailKit is in the solution but only used **outbound** (SMTP) today — inbound needs an `ImapClient` polling a per-tenant catch-all address on a **Hangfire recurring job** that `SetTenant`s per message (the established job pattern).

**Primary recommendation:** Build a single `receipt` (Beleg) aggregate with a state machine `Captured → Extracted → Reviewed → Booked` (+ `Duplicate`/`Rejected`), whose four capture sources (camera, upload, email, e-invoice) all converge on the same review-and-book flow. Reuse `bytea` WORM storage (`customer_files` template) — do NOT reach for object storage in this phase. Put OCR behind `IReceiptExtractor` and **stub the adapter by default**, flagging the live Azure wiring + a mailbox domain as the two things the user must green-light. Feed the existing `ExpensePostingSource` unchanged for the 19%/7% Vorsteuer case; the only Ledger gap is 0%/no-Vorsteuer expenses (Kleinunternehmer suppliers, insurance, §13b purchases) which currently throw — flag that as a bounded extension.

## Standard Stack

### Core (already in the solution — reuse, do not re-install)

| Component | Where (verified this session) | Role in Phase 12 |
|-----------|-------------------------------|------------------|
| `ExpensePostingSource` + `ExpensePostingInput` | `src/modules/Numera.Modules.Ledger/Posting/ExpensePostingSource.cs` | **The BELEG-05 booking rule — already built + tested.** This phase supplies its runtime caller. |
| `PostingEngine.PostAsync(source, header, ct)` | `src/modules/Numera.Modules.Ledger/Posting/PostingEngine.cs` | Enforces Soll=Haben, persists on the caller's tx. Caller owns `BeginTransaction`. |
| `AccountResolver` (`ResolveExpense`/`ResolveInputTax`/`ResolveStandard`) | `.../Posting/AccountResolver.cs` | Maps rate→Aufwandskonto/Vorsteuer/Kreditor; enforces Automatikkonto tax-key conflicts. |
| `InboundParser.Parse(bytes, contentType, fileName)` | `src/modules/Numera.Modules.Sales/EInvoice/Inbound/InboundParser.cs` | **BELEG-03 — Tier A zero-OCR structured read.** Returns `InboundReadModel` (Seller VatId/Name, InvoiceNumber, InvoiceDate, TotalNet/Tax/Gross, `BreakdownRows`). |
| `InboundDocument` (+ `inbound_document` table) | `.../Inbound/InboundDocument.cs` | Existing immutable-original + jsonb-read-model + supplier-match row. Beleg model should **relate to / reuse** this, not duplicate it. |
| `InboundEInvoiceService.IngestAsync` | `src/Numera.Api/Services/InboundEInvoiceService.cs` | Existing parse→KoSIT-validate→match→store flow. Extend to also create a Beleg + booking proposal. |
| `SupplierMatcher.MatchSellerAsync` | `.../Inbound/SupplierMatcher.cs` | **BELEG-06 — exact VAT-id then exact-name match, prefers `IsSupplier`.** Reuse as-is; extend for fuzzy/IBAN + dedup. |
| `BusinessPartner` (CRM) | `src/modules/Numera.Modules.Crm/BusinessPartner.cs` | `IsSupplier`, `CreditorAccount`, `DebtorAccount`, `VatId`, `Iban`, `Email`, `Name` — the supplier master for match + Kreditor account override. |
| `CustomerFile` + `customer_files` migration | `src/modules/Numera.Modules.Crm/CustomerFile.cs` + `src/platform/Numera.Platform.Db/Migrations/20260728203515_CustomerFiles.cs` | **The WORM template for BELEG-04** — `bytea` original, RLS, REVOKE UPDATE/DELETE, immutability trigger. Copy it. |
| MailKit / MimeKit 4.x | `src/Numera.Api/Services/MailKitEmailSender.cs` (SMTP only today) | **BELEG-07** — add `ImapClient` inbound polling; the dependency is already referenced. |
| Hangfire (Worker + Api) + `ICurrentTenant.SetTenant` | `src/Numera.Worker/Program.cs`, `src/Numera.Api/Jobs/*.cs` | OCR extraction + mailbox polling as background jobs; every job opens a DI scope and `SetTenant(tenantId)` so RLS applies. |
| `LedgerSettings.ChartVariant` | `src/modules/Numera.Modules.Ledger/LedgerSettings.cs` | Resolve SKR03/04 per tenant for the posting (mirror `TryResolveChartVariant`). |
| `NumeraDbContext` self-describing entities (`ITenantEntity`, `[Table]`, `[Index]`) | `src/platform/Numera.Platform.Db` | New tables self-register; policies are HAND-WRITTEN in the migration (reflection never emits RLS). |

### Net-new for this phase

| Component | Version / source | Purpose | Notes |
|-----------|------------------|---------|-------|
| **Azure.AI.DocumentIntelligence** | **1.0.0 GA** (NuGet), API `2024-11-30`, model `prebuilt-invoice` / `prebuilt-receipt` | Cloud OCR adapter behind `IReceiptExtractor` | Targets a swappable port. **EU region** (Germany West Central / West Europe / Sweden Central) + **DPA**. 27 languages incl. German. Per-field confidence returned. **OPEN QUESTION: provider + live-vs-stub.** |
| **`IReceiptExtractor` port** | net-new abstraction | `Task<ReceiptExtractionResult> ExtractAsync(byte[] bytes, string contentType, CancellationToken)` → structured fields + per-field confidence | Provider-swappable. Ship a **stub/null adapter default** + the Azure adapter; self-hosted PaddleOCR/docTR as documented escape hatch. |
| **`receipt` (Beleg) aggregate + table** | net-new `ITenantEntity` | The Beleg state machine + extracted fields + confidence + links (archive original, supplier, JournalEntry) | RLS + cross-tenant test (Definition of Done). |
| **`receipt_archive` WORM store** | net-new (or reuse `inbound_document.OriginalBytes` / a `bytea` column on `receipt`) | GoBD 10-year immutable original | `bytea` + `customer_files` immutability template. Decide: separate archive table vs. bytea on the Beleg row. |
| **`IReceiptExtractor` → Azure adapter** | net-new | Concrete Azure DI call | Auth: key or Managed Identity. Async (poller) → run in Hangfire. |
| **IMAP intake job** | MailKit `ImapClient` + Hangfire `RecurringJob` | Poll per-tenant catch-all, extract attachments → Beleg | Idempotent on `Message-Id`; `SetTenant` per routed message. |

### Alternatives Considered

| Instead of | Could use | Tradeoff |
|------------|-----------|----------|
| Azure DI (cloud, EU) | Self-hosted PaddleOCR / docTR (Docker sidecar) | Only if a customer/DPA forbids any cloud OCR. Higher eng + accuracy cost. Same `IReceiptExtractor` port — keep as escape hatch, not default. |
| Azure DI | Klippa / Rossum / Taggun (EU receipt specialists) | Viable EU-compliant swaps behind the port; Rossum strongest pure-invoice AI, Taggun lightest receipt-only. Provider is a business/DPA decision → OPEN QUESTION. |
| Postgres `bytea` archive | S3-compatible object storage w/ Object-Lock (WORM) | PITFALLS flags "full images in Postgres → DB bloat" **at volume**. But `bytea` is the proven v1 store (`customer_files`, `inbound_document`) and keeps RLS + immutability trigger + backup in one place. **Recommend `bytea` for this phase**; document object storage as a future migration if scan volume grows. |
| Own catch-all mailbox + MailKit IMAP | Postmark Inbound / Mailgun Routes / SES-inbound→S3 | Managed inbound routes mail through a third party (DSGVO DPA needed). Own mailbox keeps mail on infra you control. **Recommend own mailbox**; mailbox infra (catch-all domain + IMAP creds) is an OPEN QUESTION/dependency. |
| New `receipt` entity | Extend `inbound_document` for everything | e-invoices already fit `inbound_document`; but camera/OCR receipts need per-field **confidence** + a **review state machine** `inbound_document` lacks. Recommend a `receipt` aggregate that **links to** `inbound_document` for Tier A rather than duplicating the parse/store. |

**Installation:**
```bash
# user-local .NET 10 SDK (MEMORY: dotnet-10-sdk-path — NOT the PATH default .NET 8)
dotnet add src/modules/Numera.Modules.Belege package Azure.AI.DocumentIntelligence   # 1.0.0
# MailKit, Hangfire, PdfPig, ZUGFeRD-csharp, NodaMoney already referenced — no new adds
```
(Module placement is a planner decision: a new `Numera.Modules.Belege`, or fold into `Numera.Modules.Sales` next to `EInvoice/Inbound`. Given the tight coupling to `InboundParser`/`inbound_document`/`SupplierMatcher`, folding beside `EInvoice/Inbound` is the lower-friction option.)

## The Anchor: BELEG-05 Booking Path (exact seam)

**`ExpensePostingInput` shape (verified — `ExpensePostingSource.cs:4-10`):**
```csharp
public sealed record ExpensePostingInput(
    string? ExpenseAccount,    // override Aufwandskonto number, or null → SKR default (4980/6300)
    string? CreditorAccount,   // override Kreditorenkonto, or null → SKR default (1600/3300)
    decimal RatePercent,       // 19 or 7  (see gap below)
    decimal Net,               // Bemessungsgrundlage
    decimal Tax,               // Vorsteuer; 0 → no Vorsteuer leg emitted
    DateOnly EntryDate);       // Buchungsdatum
```

**What it books (`BuildPostings`):** `Soll Aufwand (Net)` + `Soll Vorsteuer (Tax)` if `Tax>0` + `Haben Kreditor (Net+Tax)`. Balanced by construction.

**How to call it (mirror `SalesDocumentEndpoints.PostInvoiceAsync:1272-1330` + `PaymentService`):**
```csharp
// 1. Idempotency guard (copy the invoice/payment pattern verbatim):
var sourceRef = beleg.Id.ToString();
if (await db.Set<JournalEntry>().AnyAsync(e =>
        e.SourceType == LedgerSourceType.Expense && e.SourceRef == sourceRef, ct))
    return; // already booked

// 2. Resolve chart variant from LedgerSettings (mirror TryResolveChartVariant)
var chartVariant = settings.ChartVariant;

// 3. Build the input from the REVIEWED/confirmed Beleg fields (human-approved, not raw OCR)
var input = new ExpensePostingInput(
    ExpenseAccount: beleg.ExpenseAccountOverride,       // e.g. from learned per-supplier rule
    CreditorAccount: matchedPartner?.CreditorAccount,
    RatePercent: beleg.VatRatePercent,
    Net: beleg.NetAmount,
    Tax: beleg.VatAmount,
    EntryDate: beleg.ExpenseDate);

// 4. Header — SourceType.Expense ALREADY EXISTS (LedgerSourceType.Expense = 3)
var header = new JournalEntry {
    TenantId = tenantId,
    EntryDate = beleg.ExpenseDate,
    SourceRef = sourceRef,
    SourceType = LedgerSourceType.Expense,
    Description = $"Eingangsrechnung {beleg.SupplierName} {beleg.InvoiceNumber}",
    PostingType = PostingType.Normal,
};

// 5. Post inside a transaction the CALLER owns (PostAsync does NOT open one):
await using var tx = await db.Database.BeginTransactionAsync(ct);
await postingEngine.PostAsync(
    new ExpensePostingSource(tenantId, chartVariant, input, accountResolver), header, ct);
await tx.CommitAsync(ct);
```

**Ledger gap to flag (verified `SkrMapping.ExpenseMapping:82-94`):** `ExpenseMapping` supports **only 19% and 7%**; any other rate (including **0% / steuerfrei / no-Vorsteuer**) **throws** `ArgumentOutOfRangeException` — even when an `ExpenseAccount` override is supplied, because `ResolveExpense` calls `ExpenseMapping(variant, rate)` for the Automatikkonto/BU-key. So a receipt from a **Kleinunternehmer supplier**, an **insurance/tax/fee** with no input VAT, or a **§13b/i.g. purchase** cannot be booked by the current source. This is a real, bounded gap — the planner should scope a small extension (a 0%-rate expense path with no Vorsteuer leg, a generic expense account, no BU key) OR explicitly defer non-VAT expenses. This also ties to the Phase-11 note that Kz 89/61/46/47 have no incoming posting path (§13b/i.g.-Erwerb belong to THIS phase if in scope).

**Storno:** expense corrections follow the same reversal-only rule (`ReversalPostingSource`, `PostingType.Storno`, `ReversesEntryId`) as invoices — a booked Beleg is never edited/deleted; a wrong booking is reversed and re-booked. Do not add an UPDATE/DELETE path.

## BELEG-03: Tier A e-invoice (zero OCR, reuse Phase-5)

A received XRechnung/ZUGFeRD becomes a structured Beleg with **no OCR** via the existing pipeline:
- `InboundParser.Parse(bytes, contentType, fileName)` → detects PDF vs XML, extracts embedded XML from a ZUGFeRD PDF/A-3 via **PdfPig** `/EmbeddedFiles`, parses via **ZUGFeRD-csharp** `InvoiceDescriptor.Load`, projects to `InboundReadModel`.
- `InboundReadModel.BreakdownRows` gives `{ TaxCategory, VatRatePercent, TaxableBase, TaxAmount }` **per VAT rate** — this maps **directly** onto `ExpensePostingInput(Net=TaxableBase, Tax=TaxAmount, RatePercent=VatRatePercent)`. A multi-rate invoice → **multiple `ExpensePostingInput`s or a composite source** (one Beleg, N expense legs) — note the current `ExpensePostingSource` books a single rate; a multi-rate e-invoice needs either N postings in one entry or a per-rate loop. Flag for the planner.
- `SupplierMatcher` already resolves the seller → `BusinessPartner` (VAT id, then name).
- `InboundEInvoiceService.IngestAsync` already stores the immutable original + read model + validation + match in `inbound_document`.

**Relationship to the Beleg model:** the cleanest design is a `receipt` (Beleg) row that, for Tier A, carries a FK to the existing `inbound_document.Id` (source of the immutable original + parsed fields) and adds the **booking-proposal + review-state + resulting JournalEntry** columns. This avoids duplicating the WORM original and the parse. Tier B receipts have no `inbound_document`; they carry their own archived original + `IReceiptExtractor` output. Both converge on the same confirm→book endpoint.

## BELEG-01/02: OCR port design

**Port (net-new):**
```csharp
public interface IReceiptExtractor
{
    Task<ReceiptExtraction> ExtractAsync(byte[] bytes, string contentType, CancellationToken ct);
}
public sealed record ReceiptExtraction(
    ExtractedField<string>?   SupplierName,
    ExtractedField<string>?   SupplierVatId,
    ExtractedField<string>?   InvoiceNumber,
    ExtractedField<DateOnly>? InvoiceDate,
    ExtractedField<decimal>?  NetAmount,
    ExtractedField<decimal>?  VatAmount,
    ExtractedField<decimal>?  GrossAmount,
    ExtractedField<decimal>?  VatRatePercent,
    string? Currency);
public sealed record ExtractedField<T>(T Value, double Confidence);  // per-field confidence
```

**Azure adapter (concrete):** `Azure.AI.DocumentIntelligence` 1.0.0 GA, `DocumentIntelligenceClient(endpoint, AzureKeyCredential | DefaultAzureCredential)`, `AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", BinaryData.FromBytes(bytes))`. The `prebuilt-invoice` model returns **`VendorName`, `VendorTaxId`, `InvoiceId`, `InvoiceDate`, `InvoiceTotal`, `SubTotal`, `TotalTax`, `TaxDetails` (per-rate), `CustomerName`, `Items`** — each field in `AnalyzedDocument.Fields` carries a **`.Confidence`** (per-field), which maps to `ExtractedField.Confidence`. `prebuilt-receipt` is tuned for point-of-sale receipts (Merchant, TransactionDate, Total, TaxDetails); for German supplier *invoices* `prebuilt-invoice` is the better fit — the planner may route by document type or default to `prebuilt-invoice`. 27 languages incl. German; supports PDF + JPEG/PNG/TIFF/HEIF.

**DSGVO facts (verified):** deploy in an **EU region** (Germany West Central / West Europe / Sweden Central), **sign the Microsoft DPA**, document ≤24h input deletion in the Verfahrensdokumentation. Disconnected-container option exists for full on-prem residency.

**Pricing (verified, indicative):** S0 `prebuilt-invoice` ≈ **$10 / 1,000 pages** (~$0.01/page); F0 free tier processes only the first 2 pages. Confirm live EU-region pricing before committing.

**Async:** Azure DI is a long-running analyze operation → **run extraction in a Hangfire job** (PITFALLS performance trap: never OCR synchronously in the request; upload returns immediately, extraction fills fields asynchronously, Beleg flips `Captured→Extracted`).

**Recommendation:** default DI registration = a **stub `IReceiptExtractor`** (returns empty/low-confidence, lets the user type fields) so the phase ships without a paid dependency; register the Azure adapter when the user green-lights it. This keeps OCR provider choice an OPEN QUESTION without blocking the rest of the phase.

## BELEG-04: GoBD WORM archive

**Storage decision: reuse Postgres `bytea` + the `customer_files` immutability template.** Both `customer_files` and `inbound_document.OriginalBytes` already store originals byte-for-byte as `bytea` under RLS — the pattern is proven, backed up, and tenant-isolated. Do NOT introduce object storage in this phase (flagged as a future migration for scan volume).

**Immutability enforcement (copy `20260728203515_CustomerFiles.cs` verbatim):**
```sql
ALTER TABLE receipt_archive ENABLE ROW LEVEL SECURITY;
ALTER TABLE receipt_archive FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON receipt_archive
  USING (tenant_id = current_setting('app.current_tenant')::uuid)
  WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
REVOKE UPDATE, DELETE ON receipt_archive FROM numera_app;
GRANT  INSERT, SELECT ON receipt_archive TO numera_app;
CREATE FUNCTION receipt_archive_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
  BEGIN RAISE EXCEPTION 'receipt_archive rows are append-only (GoBD); corrections = new version'; END; $$;
CREATE TRIGGER receipt_archive_immutable BEFORE UPDATE OR DELETE ON receipt_archive
  FOR EACH ROW EXECUTE FUNCTION receipt_archive_immutable();
```

**Index fields (metadata record):** `tenant_id`, `receipt_id` FK, `content_hash` (SHA-256 on ingest — dedup + integrity), `original_file_name`, `content_type`, `byte_size`, `source` (camera/upload/email/e-invoice), `received_at`, `uploaded_by_user_id`, link to `journal_entry_id` once booked. Store the **first received form** as the original (an emailed PDF is archived as received, never re-rendered).

**Retention:** default **10 years** (§147 AO). The immutability trigger already blocks hard-delete; document that **GoBD retention overrides DSGVO erasure** for tax-relevant originals (only non-tax PII is erasable) — extend the v1 Verfahrensdokumentation. Corrections/re-scans = a **new version row**, never mutate the original.

## BELEG-06: supplier match + dedup

**Match (reuse + extend `SupplierMatcher`):** exact **VAT-id** (strong key, BT-31) → exact **name** → *(new for OCR)* fuzzy name / **IBAN** match against `BusinessPartner.Iban`. Prefer `IsSupplier`, then earliest-created, for stability. Unmatched → "nicht zugeordnet" + a "create supplier" affordance (do not auto-create silently). The matched partner's `CreditorAccount` feeds `ExpensePostingInput.CreditorAccount`.

**Dedup:** compute a **content hash** (SHA-256 of original bytes) on ingest → identical re-uploads/re-forwards are caught immediately. For near-duplicates (same invoice, different scan), a **business key** = `(matched_supplier_id, invoice_number, gross_amount, invoice_date)` flags a suspected duplicate for **review** (mark, don't auto-hide — PITFALLS: never silently drop a legitimate identical charge). Email intake dedups additionally on **`Message-Id`** (see BELEG-07). The existing `InboundEInvoiceService` explicitly does NOT dedup (each upload = distinct artifact) — this phase adds the dedup layer on top.

## BELEG-07: per-tenant email intake

**Address scheme:** an **unguessable high-entropy per-tenant** address on a catch-all domain, e.g. `belege-{highEntropyToken}@inbox.numera.de` (or sub-addressed `{tenant}.{token}@…`). Store the token per tenant; make it rotatable. Guessable `belege-{firmenname}@` is a cross-tenant risk (PITFALLS 13).

**Polling job (net-new, Worker + Hangfire recurring):** MailKit **`ImapClient`** (MailKit is in the solution; only `SmtpClient` is used today — inbound is new code) connects to the catch-all mailbox, fetches unseen messages, and for each: resolve `token → tenant_id`, open a DI scope, **`SetTenant(tenantId)`** (the established job pattern — `SendDocumentEmailJob:68`, `GenerateRecurringInvoiceJob:41`), extract attachments (PDF/image/XML), route each to the Beleg pipeline (e-invoice XML/ZUGFeRD → Tier A `InboundParser`; image/plain PDF → Tier B `IReceiptExtractor`), then mark the message seen/move it.

**Idempotency:** dedup on **`Message-Id`** (persist processed ids) so a re-poll or redelivery never double-creates a Beleg. Make the job safe to re-run any window.

**Security (PITFALLS 13):** verify **SPF/DKIM/DMARC**, quarantine failures for manual review (never auto-archive a spoofed "invoice"); enforce attachment size/type limits; rate-limit per address; **malware-scan** attachments; store the **raw email (with headers)** as the GoBD original. Everything received is a *proposal* → human review, never auto-book. Map each intake strictly to **one** tenant; run the whole pipeline under that tenant's RLS.

**Dependency (OPEN QUESTION):** the catch-all domain + IMAP mailbox credentials are infra that must be provisioned; local dev/test uses **Mailpit** (already in the stack). Whether to ship email intake in this phase or fast-follow depends on that infra being available.

## BELEG-02/05: Beleg domain model + review/confirm flow

**`receipt` (Beleg) aggregate (net-new `ITenantEntity`, RLS + cross-tenant test):**

| Column | Purpose |
|--------|---------|
| `Id` (UUIDv7), `TenantId` | PK + RLS |
| `Source` | `Camera` / `Upload` / `Email` / `EInvoice` |
| `Status` | state machine (below) |
| `InboundDocumentId?` | FK to `inbound_document` for Tier A (reuses its WORM original + parse) |
| `ArchiveId?` / `OriginalBytes` | Tier B WORM original (bytea) + content hash |
| Extracted fields | `SupplierName`, `SupplierVatId`, `InvoiceNumber`, `InvoiceDate`/`ExpenseDate`, `NetAmount`, `VatAmount`, `GrossAmount`, `VatRatePercent`, `Currency` |
| `FieldConfidence` (jsonb) | per-field OCR confidence (null for e-invoice / manual) |
| `MatchedPartnerId?` | supplier match (provenance) |
| `ExpenseAccountOverride?` | learned/chosen Aufwandskonto |
| `ContentHash` | dedup + integrity |
| `JournalEntryId?` | set on book (BELEG-05 link) |
| `ReviewedByUserId?`, `ReviewedAt?`, `CreatedAt` | audit |

**State machine:**
```
Captured ──(extract job)──> Extracted ──(user corrects+confirms)──> Reviewed ──(book)──> Booked
   │                             │                                                          
   └────────────────────────────┴──> Duplicate (content/business-key hit) / Rejected (not a Beleg)
```
- **`Captured`**: original archived (WORM) immediately on ingest; extraction queued (Hangfire).
- **`Extracted`**: `IReceiptExtractor` (Tier B) or `InboundParser` (Tier A) filled fields + confidence. Low-confidence fields flagged in UI.
- **`Reviewed`**: human corrected/approved the fields, supplier, expense account, VAT key. **This is the gate — nothing is booked without it.**
- **`Booked`**: `ExpensePostingSource` posted a `JournalEntry`; `JournalEntryId` linked; the original stays immutable. Feeds EÜR Betriebsausgaben + USt-VA Kz 66.

**Confirm → book endpoint** does the anchor flow above (idempotency guard + `BeginTransaction` + `PostAsync` + commit), sets `Status=Booked`, links `JournalEntryId`, and (optionally) links the resulting open payable. **Never auto-transition Extracted→Booked** (PITFALLS 11: OCR is probabilistic; the taxpayer/Steuerberater is liable). High-confidence may pre-fill and pre-select a per-supplier learned account, but a human clicks confirm.

**Frontend:** new `web/src/features/belege/` feature (mirror the `reports`/`ustva` feature-folder + `lib/api` + TanStack Query conventions from Phase 11): capture (PWA camera + upload — camera exists in v1), a review queue, and a review-and-book form showing the archived original alongside the editable extracted fields with confidence badges.

## Architecture Patterns

### Recommended structure
```
src/modules/Numera.Modules.Sales/Belege/        (fold beside EInvoice/Inbound — tight coupling)
├── Receipt.cs                    # Beleg aggregate + state machine
├── ReceiptArchive.cs             # WORM original (bytea) — or bytea on Receipt
├── IReceiptExtractor.cs          # OCR port + ReceiptExtraction/ExtractedField records
├── StubReceiptExtractor.cs       # default no-op adapter (manual entry)
├── ReceiptDeduplicator.cs        # content-hash + business-key dedup
└── ReceiptBookingProposal.cs     # extracted fields → ExpensePostingInput mapping
src/Numera.Api/
├── Services/AzureReceiptExtractor.cs           # Azure DI adapter (registered when enabled)
├── Endpoints/ReceiptEndpoints.cs               # capture/upload/list/detail/review/confirm-book
├── Jobs/ExtractReceiptJob.cs                   # Hangfire: OCR async (SetTenant)
└── Jobs/PollBelegMailboxJob.cs                 # Hangfire recurring: IMAP intake (SetTenant per msg)
src/platform/Numera.Platform.Db/Migrations/…    # receipt + receipt_archive (RLS + immutability template)
web/src/features/belege/ + web/src/pages/…
```

### Pattern: reuse the WORM immutability migration
Copy `customer_files` migration's RLS+REVOKE+trigger block for every table holding an original (`receipt_archive`). Add each new table to the **Cross-Tenant test suite** in the same phase (standing Definition of Done — PITFALLS 16).

### Pattern: background job with tenant context
```csharp
// Source: src/Numera.Api/Jobs/SendDocumentEmailJob.cs:65-68
using var scope = _scopeFactory.CreateScope();
scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId); // RLS applies
```

### Anti-Patterns to Avoid
- **Auto-booking from OCR/email** (skipping `Reviewed`). Never. Human confirm is a hard product rule.
- **Editing/deleting an archived original or a booked JournalEntry.** WORM + reversal-only; corrections = new version / Stornobuchung.
- **Synchronous OCR in the request.** Offload to Hangfire; return the Beleg immediately.
- **Storing OCR amounts via `double`.** Parse Azure numeric fields to `decimal` at the boundary (PITFALLS 17).
- **A new table without RLS**, or a job (OCR/IMAP) running without `SetTenant`. Both are cross-tenant leaks.
- **Duplicating the inbound e-invoice original/parse** in the Beleg row instead of linking `inbound_document`.
- **Guessable per-tenant intake address**; **auto-archiving SPF/DKIM/DMARC-failing mail**.

## Don't Hand-Roll

| Problem | Don't build | Use instead | Why |
|---------|-------------|-------------|-----|
| Expense Buchungssatz (Aufwand/Vorsteuer/Kreditor) | A new posting rule | `ExpensePostingSource` (exists + tested) | Balance invariant, Automatikkonto/BU-key checks already enforced |
| e-invoice parse (XRechnung/ZUGFeRD) | An XML parser | `InboundParser` + ZUGFeRD-csharp + PdfPig | Handles UBL/CII + embedded-XML extraction, already validated |
| Supplier match | Custom matching | `SupplierMatcher` (VAT→name) | Deterministic, auditable, prefers `IsSupplier` |
| WORM immutable storage | Custom lock logic | `customer_files` migration template (RLS+REVOKE+trigger) | Proven append-only enforcement at the DB |
| OCR model | Train your own | Azure DI `prebuilt-invoice` behind `IReceiptExtractor` | Mature IDP; German + per-field confidence; swappable |
| Inbound mail parsing | US SaaS default | MailKit `ImapClient` own mailbox | DSGVO — keep mail on infra you control |
| Tenant scoping in jobs | Manual filters | `ICurrentTenant.SetTenant` + interceptor | Pool-safe RLS, proven |
| Money rounding | Custom | `RoundingPolicy` / `Money` (NodaMoney) | Cent-exact tie-out to the ledger |

**Key insight:** the accounting-correctness core (posting, balance, immutability, VAT keys, e-invoice parse, supplier match) already exists and is tested. This phase is **plumbing + a review UX + one cloud port** — the risk is in the OCR provider decision, the mailbox infra, and the Beleg state machine, not in the booking math.

## Common Pitfalls

### Pitfall 1: Auto-posting OCR results (accuracy + liability)
**What goes wrong:** scan→booking auto-creates a Buchung; a misread amount/VAT flows into a filed USt-VA. **Avoid:** OCR/e-invoice *propose*; a human *confirms* before `Booked`. Low-confidence fields flagged; booking links the immutable original. **Warning sign:** a Beleg in `Booked` with no `ReviewedByUserId`.

### Pitfall 2: Originals not revisionssicher (GoBD 10y)
**What goes wrong:** originals in mutable/deletable storage. **Avoid:** `bytea` + `customer_files` immutability template (REVOKE UPDATE/DELETE + trigger), content hash, 10y retention, retention overrides DSGVO erasure for tax docs. **Warning sign:** any UPDATE/DELETE grant on the archive table.

### Pitfall 3: Email intake spoofing / mis-routing
**What goes wrong:** guessable address, spoofed sender, one tenant's Beleg lands in another. **Avoid:** high-entropy rotatable address, SPF/DKIM/DMARC + quarantine, malware scan, strict single-tenant routing under RLS, `Message-Id` dedup, human review. **Warning sign:** any path where a message resolves to >1 tenant.

### Pitfall 4: 0%/no-Vorsteuer expenses throw
**What goes wrong:** `ExpenseMapping` only knows 19%/7%; a Kleinunternehmer-supplier / insurance / §13b receipt throws. **Avoid:** extend the source for a 0%-rate path (no Vorsteuer leg, generic Aufwandskonto, no BU key) OR explicitly scope out non-VAT expenses. **Warning sign:** `ArgumentOutOfRangeException` from a real receipt.

### Pitfall 5: Multi-rate e-invoice → one-rate posting
**What goes wrong:** a ZUGFeRD invoice with 19% + 7% lines maps to a single `ExpensePostingInput`. **Avoid:** loop `BreakdownRows` → N expense legs in one JournalEntry (or a composite posting source). **Warning sign:** booked net/VAT ≠ e-invoice `TotalTax`.

### Pitfall 6: New tables without RLS / OCR+IMAP jobs without tenant context
**Avoid:** RLS ENABLE+FORCE+policy in the migration; every job `SetTenant`; grow the cross-tenant suite (Beleg + archive: tenant A cannot read tenant B's Belege/OCR text/originals). Standing DoD.

### Pitfall 7: Duplicate Belege from re-forwarded email / re-upload
**Avoid:** content hash on ingest (exact dup), `Message-Id` dedup (email), business-key `(supplier, invoice_no, gross, date)` → flag for review (never silently drop). **Warning sign:** the same invoice booked twice.

### Pitfall 8: Money via float from OCR
**Avoid:** parse Azure numeric fields to `decimal` at the boundary; reuse `RoundingPolicy`. **Warning sign:** any `double` on the amount path.

## Code Examples

### Expense booking call (mirror invoice/payment hooks)
```csharp
// Source pattern: src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs:1272-1330 (PostInvoiceAsync)
//                 src/Numera.Api/Services/PaymentService.cs:281-322
// Idempotency guard → resolve chart variant → build ExpensePostingInput from REVIEWED fields →
// JournalEntry{ SourceType = LedgerSourceType.Expense } → PostAsync inside caller-owned tx.
```

### WORM archive migration (copy verbatim, rename)
```csharp
// Source: src/platform/Numera.Platform.Db/Migrations/20260728203515_CustomerFiles.cs:38-57
// ENABLE + FORCE RLS; tenant_isolation policy; REVOKE UPDATE,DELETE FROM numera_app;
// GRANT INSERT,SELECT; BEFORE UPDATE OR DELETE trigger RAISE EXCEPTION.
```

### Azure Document Intelligence adapter (shape)
```csharp
// Azure.AI.DocumentIntelligence 1.0.0 GA, API 2024-11-30
var client = new DocumentIntelligenceClient(new Uri(endpoint), credential); // EU region endpoint
var op = await client.AnalyzeDocumentAsync(
    WaitUntil.Completed, "prebuilt-invoice", BinaryData.FromBytes(bytes), cancellationToken: ct);
var doc = op.Value.Documents[0];
// doc.Fields["VendorName"|"VendorTaxId"|"InvoiceId"|"InvoiceDate"|"SubTotal"|"TotalTax"|"InvoiceTotal"]
//   → each field carries .Confidence (double) → ExtractedField<T>(value, confidence). Parse amounts to decimal.
```

## State of the Art

| Old approach | Current (2026) | Impact |
|--------------|----------------|--------|
| `Azure.AI.FormRecognizer` SDK | `Azure.AI.DocumentIntelligence` **1.0.0 GA**, API `2024-11-30`, model doc-intel 4.0 | Use the new package + `prebuilt-invoice`/`prebuilt-receipt` model ids |
| OCR everything | Two-tier: structured e-invoice (zero OCR) + OCR only for images | Higher accuracy + cheaper for the growing e-invoice share (2025+ B2B e-invoice mandate) |
| Object storage for scans | Postgres `bytea` for v1-scale (proven `customer_files`/`inbound_document`) | Keep RLS + immutability + backup unified; revisit at volume |

## Open Questions

1. **[BLOCKING business decision] Cloud OCR provider + live-vs-stub.** Recommend Azure AI Document Intelligence (`prebuilt-invoice`, EU region + DPA). But it is a paid external dependency. **Ship the `IReceiptExtractor` port + a stub default now, wire Azure live when the user green-lights it?** — or defer OCR entirely and ship manual-entry + e-invoice tiers first? Needs the user's call on provider, region, DPA, and budget. Spike accuracy on real German receipts before committing.
2. **[Dependency] Mailbox infrastructure for BELEG-07.** Catch-all domain (`inbox.numera.de`?) + IMAP mailbox credentials must be provisioned. Ship email intake this phase or fast-follow? Confirm the address scheme (`belege-{token}@` vs sub-addressing).
3. **Archive storage: `bytea` vs object storage.** Recommend `bytea` (proven, unified RLS/immutability). Confirm acceptable given the PITFALLS "DB bloat at volume" note, or decide an object-storage threshold now.
4. **Beleg model vs `inbound_document`.** Recommend a `receipt` aggregate that **links** `inbound_document` for Tier A rather than duplicating. Confirm the planner wants one converged `receipt` table over both tiers.
5. **Non-VAT / 0% expenses.** `ExpensePostingSource` throws for rates ≠ 7/19. Scope a small 0%-rate extension (no Vorsteuer leg) this phase, or defer non-VAT expenses? Ties to §13b/i.g.-Erwerb (Kz 89/61/46/47) deferred from Phase 11 — are those in scope here?
6. **Multi-rate e-invoice booking.** Confirm the planner extends the expense posting to N legs per `BreakdownRows`, or restricts Tier-A auto-proposal to single-rate invoices for MVP.
7. **Malware scanning** for email/upload attachments — which scanner/service (ClamAV sidecar?) and is it in scope this phase or a security fast-follow?

## Sources

### Primary (HIGH — read from the repo this session)
- `src/modules/Numera.Modules.Ledger/Posting/{ExpensePostingSource,AccountResolver,PostingEngine}.cs`, `IPostingSource.cs`, `JournalEntry.cs`, `LedgerSettings.cs`, `Seed/SkrMapping.cs` — the exact BELEG-05 booking seam + the 0%/multi-rate gaps
- `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` (PostInvoiceAsync + Storno + tx pattern), `src/Numera.Api/Services/PaymentService.cs` — the posting-hook pattern to mirror
- `src/modules/Numera.Modules.Sales/EInvoice/Inbound/{InboundParser,InboundDocument,SupplierMatcher}.cs`, `src/Numera.Api/Services/InboundEInvoiceService.cs` — BELEG-03 Tier A + BELEG-06 match (reuse)
- `src/modules/Numera.Modules.Crm/{CustomerFile,BusinessPartner}.cs` + `src/platform/Numera.Platform.Db/Migrations/20260728203515_CustomerFiles.cs` — the WORM immutability template + supplier master
- `src/Numera.Api/Services/MailKitEmailSender.cs`, `src/Numera.Api/Jobs/{SendDocumentEmailJob,GenerateRecurringInvoiceJob}.cs`, `src/Numera.Worker/Program.cs` — MailKit (outbound only today) + Hangfire job `SetTenant` pattern
- `.planning/research/{SUMMARY,STACK,FEATURES,PITFALLS}.md` + `.planning/phases/11-berichte-ust-voranmeldung/11-RESEARCH.md` — locked upstream decisions + the deferred Vorsteuer Kz 66 / EÜR Betriebsausgaben this phase unlocks

### Secondary (HIGH/MEDIUM — verified this session)
- [Azure.AI.DocumentIntelligence 1.0.0 (NuGet)](https://www.nuget.org/packages/Azure.AI.DocumentIntelligence/1.0.0) + [SDK README](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/documentintelligence/Azure.AI.DocumentIntelligence/README.md) — GA package, API 2024-11-30
- [Invoice data extraction — Document Intelligence (Microsoft Learn, doc-intel 4.0)](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/invoice?view=doc-intel-4.0.0) — `prebuilt-invoice` fields (VendorName/VendorTaxId/InvoiceId/InvoiceDate/SubTotal/TotalTax/InvoiceTotal/TaxDetails), 27 languages incl. German, per-field confidence, S0 500 MB
- [Invoice model schema (2024-11-30 GA)](https://github.com/Azure-Samples/document-intelligence-code-samples/blob/main/schema/2024-11-30-ga/invoice.md) — full field list to map into `IReceiptExtractor`
- [Azure Document Intelligence pricing 2026 (Parsli)](https://parsli.co/compare/azure-document-intelligence) / [docuocr](https://docuocr.com/blog/azure-document-intelligence-pricing) — S0 ≈ $10/1,000 pages invoice (verify live EU-region rate)

### Tertiary (MEDIUM/LOW — confirm during planning)
- EU-region live pricing (Germany West Central) + German-receipt accuracy — a spike, not a doc lookup
- PaddleOCR/docTR self-hosted escape-hatch specifics — only if cloud OCR is rejected

## Metadata

**Confidence breakdown:**
- BELEG-05 booking seam / e-invoice reuse / WORM template / job pattern: **HIGH** — read from source
- Two-tier architecture + never-auto-book + EU-region OCR: **HIGH** — locked upstream + PITFALLS
- Azure DI SDK/model/fields/pricing: **MEDIUM-HIGH** — Microsoft Learn + NuGet verified; live EU pricing + German accuracy need a spike
- Provider choice + mailbox infra: **OPEN** — genuine business/infra decisions, not research gaps

**Research date:** 2026-08-04
**Valid until:** ~2026-09-04 for codebase facts (until Ledger/Inbound change); Azure DI model/pricing re-verify per quarter (fast-moving cloud service).
