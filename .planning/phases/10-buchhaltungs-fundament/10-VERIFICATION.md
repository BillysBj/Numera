---
phase: 10-buchhaltungs-fundament
verified: 2026-08-03T15:46:41Z
status: passed
score: 4/4 success criteria verified (29/29 must-have truths across 6 plans)
---

# Phase 10: Buchhaltungs-Fundament Verification Report

**Phase Goal:** Numera bekommt ein echtes doppisches Buchungs-Fundament -- jede finalisierte Rechnung
und jede Zahlung wird automatisch, unveraenderbar und ausgeglichen verbucht. Das Fundament, aus dem
sich alle Berichte und die USt-VA speisen.

**Verified:** 2026-08-03T15:46:41Z
**Status:** passed
**Re-verification:** No -- initial verification

## Method note

gsd-tools.js verify artifacts/key-links could not parse this phase's PLAN frontmatter (the tool's
parseMustHavesBlock regex expects 4-space-indented artifacts:/key_links: keys under must_haves:,
but these plans use the standard 2-space YAML indent -- a tooling quirk, not a phase gap).
Verification below was done by direct code inspection against each plan's must_haves, cross-checked
against the migration SQL, the posting engine, the finalize/payment hooks, the read endpoints,
Festschreibung, and the six integration test files (read in full or in relevant part). No app
build/test re-run was performed because a live Numera.Api.exe (PID 98188) was holding a file lock on
the shared bin/Debug output tree (apparently a running dev session) -- killing another process
without being asked was avoided. Findings rely on static code inspection plus the already-reported
clean test run (131 unit + 213 integration, 0 failures) which the test file contents corroborate
(real Postgres, non-BYPASSRLS numera_app role, matching scenario names).

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer waehlt SKR03/SKR04 beim Setup; mandantenspezifischer aktiver, RLS-isolierter Kontensatz entsteht | VERIFIED | LedgerSetupEndpoints.SetupAsync creates LedgerSettings and calls ChartSeeder.SeedAsync in one transaction (src/Numera.Api/Endpoints/LedgerSetupEndpoints.cs); LedgerSetupTests proves 13-account SKR03/SKR04 charts, 409 on re-setup, and cross-tenant zero-visibility via IgnoreQueryFilters() against the real numera_app role (RLS FORCE, not just the EF filter) |
| 2 | Finalisierte Rechnung produces ausgeglichene Soll/Haben-Buchung (korrektes Konto und Steuerschluessel); Zahlung bucht Bank gegen Forderung and schliesst offenen Posten; Eingangsrechnung bucht Aufwand und Vorsteuer | VERIFIED | PostInvoiceAsync and PostStornoReversalAsync in SalesDocumentEndpoints.cs call PostingEngine.PostAsync inline inside the FinalizeCoreAsync transaction; PaymentService.RecordAsync and ReverseAsync do the same for payments; ExpensePostingSource.cs builds Aufwand+Vorsteuer vs. Verbindlichkeiten (engine and tests only, per LOCKED scope note in 10-03-PLAN.md). PostingEngine.PostAsync enforces sum(debit) == sum(credit) domain-side before persisting. LedgerPostingEngineTests (11 cases: 19pct/7pct/mixed/13b/multi-item payment/reversal/expense/Storno) plus LedgerInvoicePostingTests (6, incl. atomicity rollback) plus LedgerPaymentPostingTests (5, incl. atomicity and reversal) exercise this end-to-end against real Postgres |
| 3 | Gebuchte Journalzeilen sind DB-seitig unveraenderbar (REVOKE and Trigger, ausgeglichen erzwungen); Korrektur entsteht nur als Storno-Buchung | VERIFIED | Migration 20260803141314_LedgerEngine.cs REVOKEs UPDATE/DELETE on postings and DELETE on journal_entries (guarded by role-exists check); postings_immutable trigger unconditionally RAISEs on UPDATE/DELETE; journal_entries_immutable whitelist trigger RAISEs on DELETE, RAISEs if already festgeschrieben, and RAISEs if any frozen business column changes -- only journal_number, festgeschrieben_at and period_id may move; postings_balanced is a DEFERRABLE CONSTRAINT TRIGGER summing debit/credit per journal_entry_id at COMMIT. ReversalPostingSource and the payment reversal path swap Debit/Credit per leg -- a true Generalumkehr, with no edit path. FestschreibungTests.Festgeschriebene_entry_rejects_business_field_edits proves a raw UPDATE RAISEs after lock |
| 4 | Nutzer sieht Buchungsjournal und Kontoauszug je Konto; eine festgeschriebene Periode nimmt keine neuen Buchungen mehr an | VERIFIED | LedgerEndpoints.GetJournalAsync and GetAccountStatementAsync return keyset-paginated, RLS-scoped journal and per-account running-balance statements (windowed SQL). FestschreibungService.LockPeriodAsync stamps gapless year-NNNNNN numbers in (entry_date, id) order inside one transaction, then flips FiscalPeriod.Status to Locked; the plan-01 journal_entries_period_lock trigger then rejects any INSERT with an entry_date in that period. FestschreibungTests.Booking_into_locked_period_fails_and_rolls_back_payment_settlement proves a payment attempt into a locked period throws a PostgresException ("festgeschrieben (locked)") and leaves zero orphan state (no Payment, no Allocation, no JournalEntry, OpenItem unchanged) |

**Score:** 4/4 ROADMAP success criteria verified.

### Required Artifacts (by plan)

| Plan | Artifact | Status | Details |
|------|----------|--------|---------|
| 10-01 | Account.cs, JournalEntry.cs, Posting.cs, LedgerSettings.cs, FiscalPeriod.cs, Steuerschluessel.cs | VERIFIED | All entity extensions present exactly as specified (ChartVariant, Steuerschluessel, IsAutomatikkonto, UstvaKennziffer, IsActive, ParentNumber on Account; JournalNumber/PeriodId/PostingType/ReversesEntryId/SourceType/FestgeschriebenAt on JournalEntry; tax fields on Posting) |
| 10-01 | Migrations/20260803141314_LedgerEngine.cs | VERIFIED | RLS on ledger_settings/fiscal_periods; REVOKE+GRANT DO-block guarded by role-existence check; postings_immutable, journal_entries_immutable (whitelist), postings_balanced (deferred constraint trigger), journal_entries_period_lock triggers all present with matching SQL to the plan's verbatim templates; accounts table intentionally has no immutability trigger (grep confirms) |
| 10-02 | Seed/skr03.accounts.json, skr04.accounts.json, SkrMapping.cs, ChartSeeder.cs, LedgerSetupEndpoints.cs | VERIFIED | 13-account charts confirmed by test assertions (Assert.Equal(13, accounts.Count)); SkrMapping.RevenueMapping(Skr03, TaxCategory.S, 19m) verified to return (8400, 1776, Ust19) matching RESEARCH.md; setup endpoint atomic and idempotent (409 on retry) |
| 10-03 | Posting/AccountResolver.cs, InvoicePostingSource.cs, PaymentPostingSource.cs, ExpensePostingSource.cs, PostingEngine.cs, LedgerPostingEngineTests.cs | VERIFIED | AccountResolver delegates account-number resolution solely to SkrMapping; enforces Automatikkonto/manual-key conflict (LedgerTaxKeyConflictException); PostingEngine enforces sum(debit)==sum(credit) before SaveChangesAsync, does not open its own transaction (caller-owned, confirmed by constructor taking only NumeraDbContext) |
| 10-04 | SalesDocumentEndpoints.cs (PostingEngine call in FinalizeCoreAsync/storno), LedgerInvoicePostingTests.cs | VERIFIED | Inline call inside the finalize transaction (before doc.Status=Finalized flip and the final SaveChangesAsync); idempotency guard (AnyAsync check on SourceType=Invoice, SourceRef); graceful skip via TryResolveChartVariant returning null; Storno path builds ReversalPostingSource referencing originalEntry.Id |
| 10-05 | PaymentService.cs (PostingEngine call in RecordAsync/ReverseAsync), LedgerPaymentPostingTests.cs | VERIFIED | Same inline/idempotent/graceful-skip pattern as 10-04, confirmed by direct read of PaymentService.cs; no file overlap with 10-04's SalesDocumentEndpoints.cs (safe parallel wave, confirmed) |
| 10-06 | LedgerEndpoints.cs, FestschreibungService.cs, LedgerJournalReadTests.cs, FestschreibungTests.cs | VERIFIED | Journal/statement endpoints keyset-paginated (UUIDv7 cursor, no OFFSET) with windowed running-balance SQL; FestschreibungService.LockPeriodAsync uses a pg_advisory_xact_lock to serialize concurrent locks within the same tenant+year and avoid overlapping journal-number ranges (a defensive addition beyond the plan, documented as a non-functional deviation in 10-06-SUMMARY.md); no unlock endpoint exists (confirmed absent) |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| NumeraDbContext.ConfigureLedgerTableNames | ledger_settings, fiscal_periods | table-name map | WIRED | Both tables migrate and are queried via db.Set<LedgerSettings>() and db.Set<FiscalPeriod>() throughout the codebase |
| postings CONSTRAINT TRIGGER | balanced-per-entry invariant | DEFERRABLE INITIALLY DEFERRED | WIRED | Confirmed verbatim in migration; FestschreibungTests/LedgerPostingEngineTests exercise real inserts against it |
| journal_entries INSERT trigger | fiscal_periods.status | period lookup | WIRED | journal_entries_period_lock function present; FestschreibungTests.Booking_into_locked_period_fails_and_rolls_back_payment_settlement proves it fires |
| LedgerSetupEndpoints | ChartSeeder.SeedAsync + settings insert | one transaction | WIRED | Confirmed in SetupAsync body (single tx, rollback on seed conflict) |
| AccountResolver | SkrMapping | delegated resolution | WIRED | ResolveRevenue/ResolveExpense/ResolveStandard all call SkrMapping.*Mapping/StandardAccount -- no duplicated account-number literals found in AccountResolver.cs |
| PostingEngine.PostAsync | postings_balanced DB trigger | domain guard mirrors DB invariant | WIRED | PostAsync throws LedgerImbalanceException before SaveChangesAsync when sum(debit) != sum(credit) -- defence-in-depth confirmed |
| FinalizeCoreAsync | PostingEngine.PostAsync(InvoicePostingSource) | inline call before commit | WIRED | PostInvoiceAsync called from within FinalizeCoreAsync before the status flip and shared SaveChangesAsync/caller CommitAsync |
| storno endpoint | PostingEngine.PostAsync (Storno/ReversesEntryId) | Generalumkehr inside storno tx | WIRED | PostStornoReversalAsync sets PostingType.Storno + ReversesEntryId and is invoked from the storno endpoint flow |
| PaymentService.RecordAsync | PostingEngine.PostAsync(PaymentPostingSource) | inline before commit | WIRED | Confirmed in PaymentService.cs |
| PaymentService.ReverseAsync | PostingEngine.PostAsync (reversal) | atomic with open-item restore | WIRED | Confirmed in PaymentService.cs |
| FestschreibungService.LockPeriodAsync | journal_number+festgeschrieben_at+fiscal_periods.status | single transaction | WIRED | Confirmed; passes the plan-01 whitelist trigger by only touching the permitted columns |
| Kontoauszug read | postings running balance | SUM(...) OVER (...) | WIRED | Confirmed windowed SQL in GetAccountStatementAsync |

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| ACCT-01 | SATISFIED | Truth 1 above; LedgerSetupTests (4 tests) |
| ACCT-02 | SATISFIED | Truth 2/3 above; DB triggers + domain guard + LedgerPostingEngineTests (11) |
| ACCT-03 | SATISFIED | Truth 2 above; LedgerInvoicePostingTests (6) |
| ACCT-04 | SATISFIED | Truth 2 above; LedgerPaymentPostingTests (5) |
| ACCT-05 | SATISFIED (scope-limited by design) | Engine (ExpensePostingSource) + golden-file tests exist and are correct; the runtime entry point (Beleg capture / supplier-invoice intake) is explicitly deferred to the Belege phase per the LOCKED decision in 10-03-PLAN.md. This is a documented, intentional scope boundary, not a gap -- Phase 10's deliverable for ACCT-05 is the posting RULE, which is verified correct |
| ACCT-06 | SATISFIED | Truth 4 above; LedgerJournalReadTests |
| ACCT-09 | SATISFIED | Truth 4 above; FestschreibungTests (4 tests incl. DB-enforced rollback proof) |

Note: .planning/REQUIREMENTS.md still shows these as unchecked boxes / "Pending" -- this is a
tracking-document bookkeeping item, not a code gap; the actual implementation and tests satisfy each
requirement as evidenced above.

### Anti-Patterns Found

None. Grep across all Phase-10 source files (Numera.Modules.Ledger, LedgerEndpoints.cs,
LedgerSetupEndpoints.cs, FestschreibungService.cs, PaymentService.cs) for TODO, FIXME, XXX, HACK,
PLACEHOLDER and NotImplementedException returned zero matches. No stub return patterns found in any
posting source or endpoint reviewed.

### Human Verification Required

None identified as blocking. All success criteria are DB/API-level and were verified by direct code
inspection plus existing real-Postgres integration tests (non-BYPASSRLS app role, so RLS/REVOKE/
triggers are genuinely exercised, not mocked). Optional confidence-building step for a human: manually
run POST /api/ledger/setup, finalize an invoice, record a payment, and lock the period via a live
API session to visually confirm the journal/statement JSON shapes -- but this is a nice-to-have, not a
requirement for the phase goal, since the equivalent assertions already exist in the test suite.

### Known Intentional Scope Boundaries (confirmed, not gaps)

- ACCT-05 expense posting: engine + golden-file tests only; no runtime entry point (deferred to
  Belege phase) -- confirmed via absence of any caller of ExpensePostingSource outside tests.
- Commercial Gutschrift booking is out of scope; IsReceivableInvoice gate correctly skips it (only
  Storno/Generalumkehr is the covered correction mechanism) -- confirmed in SalesDocumentEndpoints.cs.
- TaxCategory.K maps to an unseeded account (8125/4125 SKR03/SKR04) and raises
  LedgerAccountNotFoundException if reached -- confirmed in SkrMapping.cs (a later USt-VA seam,
  Kz 41).
- Non-calendar fiscal-year start (LedgerSettings.FiscalYearStartMonth) is captured but not yet
  honored by numbering/locking, which key off the calendar year/month -- confirmed in
  FestschreibungService.LockPeriodAsync and the migration's period-lock trigger, both using
  EXTRACT(YEAR/MONTH FROM entry_date).

### Gaps Summary

No gaps found. All four ROADMAP success criteria are backed by real, wired, tested code: the schema
enforces GoBD immutability/balance/period-lock at the DB level (REVOKE + triggers, not just app-layer
checks); the posting engine produces provably balanced Buchungssaetze with correct SKR accounts and
Steuerschluessel; invoice finalize and payment record/reverse both book inline and atomically with
idempotency guards and graceful degradation for tenants without ledger setup; and the read views
(Buchungsjournal, Kontoauszug) plus Festschreibung (gapless numbering + permanent period lock) are
implemented and proven against real Postgres running as the least-privilege application role. The
three documented scope boundaries (ACCT-05 entry point, commercial Gutschrift, non-calendar fiscal
year) were pre-declared as deferred in the plans and are consistently honored in the implementation --
they do not block Phase 10's stated goal.

---

*Verified: 2026-08-03T15:46:41Z*
*Verifier: Claude (gsd-verifier)*
