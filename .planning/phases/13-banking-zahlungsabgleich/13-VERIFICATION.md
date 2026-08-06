---
phase: 13-banking-zahlungsabgleich
verified: 2026-08-06T08:32:00Z
status: passed
score: "4/4 success criteria fully verified"
re_verification:
  previous_status: gaps_found
  previous_score: "3/4 success criteria fully verified, 1 partial (Criterion 1: live finAPI path not end-to-end wired)"
  gaps_closed:
    - "Account materialization: POST /api/bank-accounts/connections/{connectionId}/refresh-accounts now calls IBankConnectionProvider.ListAccountsAsync and upserts BankAccount rows by FinApiAccountId (idempotent), plus refreshes consent"
    - "Worker provider config-gate: the Stub/finAPI config-gated registration was extracted into AddBankConnectionProvider (BankingModuleServiceCollectionExtensions.cs) and is now called from BOTH Api and Worker Program.cs, so worker-queue sync/consent jobs resolve the live provider when configured"
    - "CheckBankConsentJob is now scheduled recurring daily (0 6 * * *) via IRecurringJobManager in Worker/Program.cs, with a tenant-agnostic connection scan (RunAllAsync) that SetTenants per connection before refreshing consent"
  gaps_remaining: []
  regressions: []
---

# Phase 13: Banking und Zahlungsabgleich Verification Report

**Phase Goal:** Numera trifft die Realitaet - echte Kontoumsaetze ueber finAPI, automatisch abgeglichen mit den offenen Posten und ins Journal gebucht.
**Verified:** 2026-08-06T08:32:00Z
**Status:** passed
**Re-verification:** Yes - after gap closure (commit 7bfd11e)

## Goal Achievement

### Observable Truths (Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1a | finAPI-Bankkonto verbinden (Web Form/SCA) | VERIFIED | POST /api/bank-accounts/connect (BankAccountEndpoints.ConnectAsync) persists a BankConnection, calls the config-gated provider StartImportAsync, returns webFormId/redirectUrl; Stub returns 422 with a clear German message when unconfigured. FinApiClientTests.cs proves the token/web-form flow against a mocked HttpMessageHandler. |
| 1b | Umsaetze idempotent und dublettenfrei synchronisiert (live finAPI + import) | VERIFIED | Gap closed. New POST /api/bank-accounts/connections/{connectionId}/refresh-accounts (BankAccountEndpoints.RefreshAccountsAsync) calls IBankConnectionProvider.ListAccountsAsync and upserts BankAccount rows keyed by FinApiAccountId (idempotent create-or-update), so a completed Web Form/SCA now materializes syncable accounts. Worker/Program.cs now calls the same AddBankConnectionProvider config-gated registration as Api/Program.cs (previously it hardcoded Stub), so the worker-queue SyncBankTransactionsJob/SyncBankTransactionsFanOutJob resolve the live finAPI provider when configured. BankAccountRefreshTests proves account creation, metadata refresh on re-call, cross-tenant rejection (NotFound, zero provider calls), and consent persistence, all under real Postgres RLS. BankTransactionSyncTests plus the BankTransactionIngestService dedupe pipeline remain unchanged and green (idempotent re-run, incremental cursor). |
| 1c | PSD2-Consent/Re-Auth gehandhabt | VERIFIED | Gap closed. On-demand consent check (GET /connections/{id}/consent) and re-auth (POST /{connectionId}/reauth) continue to work correctly. CheckBankConsentJob is now registered recurring daily (0 6 * * *) in Worker/Program.cs via IRecurringJobManager; RunAllAsync does a tenant-agnostic bank_connection scan and SetTenants per connection before calling RunAsync to refresh consent under RLS. The proactive ~90-day expiry-flagging half of D1 now actually executes. |
| 2 | Umsaetze automatisch offenen Posten zugeordnet (Betrag + Referenz + Gegenpartei) mit Konfidenz-Score; niedrige Konfidenz in Pruef-Queue | VERIFIED | ReconciliationScorer.ScoreAsync (weighted reference/amount/IBAN/name scoring, 0..1, High only on exact amount+reference else Review), wired via GET /api/bank-transactions/{id}/suggestions; ReconciliationScorerTests proves exact/partial/no-match/outgoing tiers. Queue via GET /api/bank-transactions/ defaults to Unmatched/Suggested/Review. Unchanged since initial verification. |
| 3 | Bestaetigte Zuordnung erfasst die Zahlung und verbucht Bank-Forderung; offener Posten geschlossen | VERIFIED | POST /{id}/confirm (BankTransactionEndpoints.ConfirmAsync) validates incoming-only, allocation-sum==amount, calls PaymentService.RecordAsync verbatim (v1 posting, no new engine), sets MatchedPaymentId/Confirmed. BankReconciliationBookingTests proves the open item closes, a JournalEntry exists, split across two invoices works, idempotent double-confirm is safe. Unchanged since initial verification. |
| 4a | Manuell zuordnen/korrigieren/splitten | VERIFIED | ReconciliationMatchPage.tsx supports accept-suggestion, manual open-item pick, N-way split with client-side exact-sum validation; POST /{id}/unmatch calls PaymentService.ReverseAsync, resets state; BankReconciliationBookingTests proves un-match reopens the item. Unchanged since initial verification. |
| 4b | CSV/MT940/CAMT-Import als Fallback | VERIFIED | Camt053Importer, Mt940Importer, CsvImporter behind IBankStatementImporter, dispatched by BankStatementImportDispatcher, converging on BankTransactionDraft then BankTransactionIngestService.IngestAsync - the identical dedupe pipeline as finAPI sync (D4). POST /api/bank-accounts/import wires this; BankStatementImporterTests prove correct parsing per format. Unchanged since initial verification. |

**Score:** 4 of 4 success criteria fully verified end-to-end.

### Required Artifacts (delta since initial verification)

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| BankAccountEndpoints.RefreshAccountsAsync (new) | Post-SCA account materialization | VERIFIED | Reads directly: calls provider.ListAccountsAsync, upserts BankAccount by FinApiAccountId (create when absent, update Iban/DisplayName/Currency/BankConnectionId when present), then refreshes and persists ConsentStatus/ConsentExpiresAt, returns the refreshed account list. Connection lookup is RLS-scoped (NumeraDbContext), so a foreign connectionId yields NotFound with zero provider calls (proven by BankAccountRefreshTests). |
| BankingModuleServiceCollectionExtensions.AddBankConnectionProvider (new) | Single config-gated Stub/FinApi swap shared by both hosts | VERIFIED | Reads directly: binds FinApiOptions, checks ClientId/ClientSecret/BaseUrl all non-empty then registers DataProtection plus IBankCredentialProtector plus typed FinApiClient plus FinApiBankConnectionProvider; else registers StubBankConnectionProvider. Called from both src/Numera.Api/Program.cs (line 151) and src/Numera.Worker/Program.cs (line 41), confirmed by direct read of both files. BankingProviderRegistrationTests proves Stub resolves with no config. |
| CheckBankConsentJob.RunAllAsync (new) plus Worker/Program.cs recurring registration | Daily tenant-agnostic consent-health sweep | VERIFIED | Reads directly: RunAllAsync opens a raw NpgsqlConnection (tenant-agnostic, mirroring the existing SyncBankTransactionsFanOutJob pattern), selects all tenant_id/id pairs from bank_connection, then calls the existing per-connection RunAsync (SetTenant-before-DbContext) for each. Worker/Program.cs registers recurringJobs.AddOrUpdate for CheckBankConsentJob calling RunAllAsync on cron 0 6 * * *, confirmed by direct read. |
| BankAccountRefreshTests.cs (new) | Account create/idempotent-update/cross-tenant/consent proof | VERIFIED | Read in full: seeds two tenants BankConnections, asserts cross-tenant refresh returns NotFound with zero provider ListAccountsAsync calls, asserts first refresh creates 2 BankAccount rows, asserts a second refresh with changed metadata updates DisplayName in place (still 2 rows, idempotent), asserts consent persisted within 1ms tolerance, asserts tenant B sees zero accounts. |
| BankingProviderRegistrationTests.cs (new) | DI contract: Stub is the safe default | VERIFIED | Read in full: builds an empty IConfiguration, calls AddBankConnectionProvider, asserts the resolved IBankConnectionProvider is StubBankConnectionProvider. |

### Key Link Verification (delta since initial verification)

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| Program.cs (Worker) | IBankConnectionProvider (FinApi vs Stub) | AddBankConnectionProvider config-gated swap | WIRED (was NOT WIRED) | Confirmed by direct read of src/Numera.Worker/Program.cs line 41 - now calls the same extension method as Api, no more hardcoded Stub. |
| BankAccountEndpoints.RefreshAccountsAsync | BankAccount creation | ListAccountsAsync plus upsert by FinApiAccountId | WIRED (was NOT WIRED) | Confirmed by direct read; every field of the draft is mapped and persisted on both create and update paths. |
| CheckBankConsentJob | recurring schedule | IRecurringJobManager.AddOrUpdate in Worker/Program.cs, daily 0 6 * * * | WIRED (was NOT WIRED) | Confirmed by direct read of src/Numera.Worker/Program.cs lines 67-70. |

All other key links (confirm to RecordAsync, unmatch to ReverseAsync, import to the shared dedupe pipeline) were already WIRED in the initial verification and are unchanged.

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| BANK-01 (finAPI connect + PSD2 consent + sync) | SATISFIED | Connect/redirect, post-SCA account materialization, background live sync, and recurring consent-health flagging are now all wired end-to-end behind the port. |
| BANK-02 (idempotent dublettenfrei sync) | SATISFIED | Proven for the fake-provider, live-provider-shaped, and file-import paths; the dedupe mechanism is uniform once a BankAccount exists, and BankAccount creation is now itself proven idempotent. |
| BANK-03 (auto-match + confidence + Pruef-Queue) | SATISFIED | Unchanged; ReconciliationScorer plus queue endpoints verified. |
| BANK-04 (confirm books Bank-Forderung, closes open item) | SATISFIED | Unchanged; ConfirmAsync calls RecordAsync, tests prove closure plus journal entry. |
| BANK-05 (manual match/correct/split) | SATISFIED | Unchanged; split plus unmatch verified in both API and UI. |
| BANK-06 (CSV/MT940/CAMT import fallback) | SATISFIED | Unchanged; all three importers plus dispatcher plus tests verified. |

### Anti-Patterns Found

None outstanding. The three anti-patterns/gaps identified in the initial verification (hardcoded Worker Stub registration, missing account-materialization endpoint, orphaned unscheduled consent job) are all closed and re-verified against the current code, not merely against SUMMARY claims.

### Human Verification Required

The phase-level human-verify checkpoint (13-07 Task 3) was already reported approved by the user prior to this re-verification. No further human verification is required to close this phase; ordinary production readiness caveats remain documented as accepted boundaries rather than gaps: exact finAPI live-sandbox endpoint/body shapes are a MEDIUM-confidence item (mocked in tests; recommend a smoke-test against real sandbox credentials before the first live tenant onboarding, but this does not block the phase goal, which is satisfied behind the tested port with Stub/fake coverage).

### Re-verification Notes

This re-verification re-read every changed file directly (BankAccountEndpoints.cs, BankingModuleServiceCollectionExtensions.cs, both Program.cs hosts, CheckBankConsentJob.cs, and both new test files in full) rather than trusting the commit message or SUMMARY claims. All three previously-identified gaps are confirmed closed in the actual code:

1. Account materialization now exists and is idempotent, cross-tenant-safe (RLS via NumeraDbContext, confirmed NotFound plus zero provider calls for a foreign connection id), and refreshes consent in the same call.
2. The Worker host now resolves the live finAPI provider when configured, via the same extracted AddBankConnectionProvider seam used by the Api host - no more silent always-Stub behavior for background jobs.
3. CheckBankConsentJob is now actually scheduled and runs on a daily cron across all tenants connections, closing the D1 recurring-consent-and-expiry-flagging requirement.

No regressions were found in the previously-verified areas (reconciliation scoring, confirm-to-book, manual match/split/unmatch, file import) - their endpoints, entities, and tests are unchanged.

---

*Verified: 2026-08-06T08:32:00Z*
*Verifier: Claude (gsd-verifier)*
