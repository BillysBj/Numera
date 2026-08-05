# Phase 13 Context — Banking & Zahlungsabgleich

**Captured:** 2026-08-04 (orchestrator decision questions; no full /gsd:discuss-phase run)
**Source:** user decisions locking the OPEN QUESTIONS from 13-RESEARCH.md

## Decisions (LOCKED — honor exactly, do not revisit)

### D1 — Wire finAPI LIVE against the SANDBOX this phase (behind a testable port)
Build the real finAPI AIS integration this phase: **Web Form 2.0** "Import a new Bank Connection" (SCA redirect), the **RECURRING ~90-day PSD2 consent** lifecycle + re-auth via "Update a Bank Connection", and **incremental, idempotent transaction sync** (89-day download window) — running against the **finAPI SANDBOX** (client credentials + base URL from configuration; the user provisions sandbox creds). No official .NET SDK → hand-write a thin typed `HttpClient` for the narrow AIS surface (client-credentials/OAuth token, user provisioning, bank-connection import, accounts, transactions), per research.
- Keep an `IBankConnectionProvider` (or equivalent) abstraction so INTEGRATION TESTS use a fake/mocked finAPI (WireMock or a test double) and prod can swap base URLs — CI must NEVER hit live/sandbox finAPI over the network. The finAPI-sandbox client is the wired real implementation behind the port.
- Store per-tenant finAPI consent + credentials/tokens **DSGVO-safe (encrypted at rest)**; provision a finAPI sub-user per tenant on first bank connect.
- SCA/consent re-auth UX (lead-time warning, connection-health surface) = Claude's discretion.

### D2 — RECEIVABLE side only (customer payments → Bank↔Forderung); payable side OUT
Reconciliation books ONLY the incoming/receivable direction by reusing the v1 `PaymentService.RecordAsync` (books Bank↔Forderung via the Phase-10 PaymentPostingSource, splits via N `PaymentAllocation`, reduces `OpenItem.OpenAmount`, closes the open item — ZERO new posting logic; correction/un-match via `ReverseAsync`).
- **OUT OF SCOPE (deferred):** the payable/Verbindlichkeit direction — an outgoing bank tx paying a *supplier* invoice (Bank↔Kreditor closing an open payable) is NOT built here, and therefore the Phase-12-deferred **EÜR Betriebsausgabe-on-payment stays deferred** (no supplier-payment path yet). Do NOT add an open-payable model or a supplier-payment posting. Outgoing/debit transactions may be synced + stored + shown as unmatched, but there is no payable to reconcile them to this phase.

### D3 — ALWAYS human-confirm; never auto-book
Even a high-confidence match is only a SUGGESTION in the Prüf-Queue (BANK-03). The user confirms before anything books; the confidence score only ranks/pre-fills the proposed allocation(s). NEVER auto-book a reconciliation (a wrong auto-booking pollutes the immutable journal → Storno). This mirrors the Belege never-auto-book gate. A booked reconciliation requires an explicit user confirmation (a reviewer id / confirm action) at the API boundary.

### D4 — File-import fallback: ALL THREE formats (CSV + MT940 + CAMT.053)
Build all three importers behind ONE `IBankStatementImporter` seam, converging on the same `bank_transaction` ingest + dedupe + reconciliation pipeline as finAPI-synced transactions:
- **CAMT.053** (ISO 20022 XML) — recommend a maintained lib (e.g. Money.Unifi) or XSD-based parse.
- **MT940** (SWIFT) — recommend SharpMt940Lib.Core or equivalent.
- **CSV** — hand-parse with a per-bank column mapping (CSV has no standard schema; keep it configurable/best-effort).
Adding NuGet packages here needs a restore (network) — flag it; the orchestrator runs the package add if the sandbox blocks it (as in Phase 12).

## Claude's Discretion (freedom areas — adopt the 13-RESEARCH.md recommendations)
- `bank_account` + `bank_transaction` model (self-describing ITenantEntity, RLS ENABLE+FORCE+tenant_isolation on every new table). Idempotent dedupe key: finAPI transaction id for synced tx; a content hash (value_date + amount + purpose + counterparty) for imported tx. A processed/dedupe marker like Phase-12's `ProcessedBelegeMail`.
- The sync job is Hangfire `[Queue("worker")]`, calling `ICurrentTenant.SetTenant(tenantId)` before touching the RLS DbContext (verified pattern from `ExtractReceiptJob`/`PollBelegMailboxJob`); incremental since last sync; `[AutomaticRetry]`; DSGVO-safe.
- **Reconciliation scoring (BANK-03):** amount (exact / partial) + Verwendungszweck/reference (invoice-number regex against open items) + Gegenpartei (IBAN/name → v1 `BusinessPartner` and/or the open item's `SalesDocument`); emit a confidence score + tier (high → pre-filled suggestion, low → queue). Query open items by remaining `OpenAmount`.
- **Manual match / correct / split (BANK-05):** a transaction maps to one or many open items (split → N `PaymentAllocation`s into `RecordAsync`); correct/un-match via `ReverseAsync`.
- Review/confirm queue + endpoints + frontend mirror the Phase-12 Belege review/confirm patterns; a `autonomous:false` **human-verify checkpoint** plan for the connect→sync→reconcile→confirm→book loop + the import fallback (external-integration + UI phase, like 11-07/12-07).
- Money stays `decimal`; every new table RLS; jobs SetTenant.

## Deferred Ideas (OUT of scope — do NOT include)
- **SEPA / PIS** — outgoing credit transfers, `pain.001`, any outgoing money movement (v2.1). AIS (read) only.
- Payable/Verbindlichkeit reconciliation + supplier-payment posting + EÜR-Betriebsausgabe-on-payment (D2 — deferred).
- GoCardless/Nordigen (closed); own FinTS/HBCI; own PSD2 aggregation/screen-scraping; storing raw bank login credentials.
- Learned booking rules (BANK-D2, v2.1); FX reconciliation (BANK-D3, v2.1); liquidity/cashflow view (BANK-D4, v2.1); SEPA direct debit (BANK-D5, v3).

## No-Fabrication / GoBD rule
Never auto-book an unconfirmed reconciliation. Booking a confirmed match goes through the v1 `PaymentService` (immutable journal, atomic). A synced/imported transaction is a fact to be reconciled — never invent an allocation the user didn't confirm. CI never contacts live/sandbox finAPI (use the port fake). Bank data is stored DSGVO-safe + RLS-isolated per tenant.
