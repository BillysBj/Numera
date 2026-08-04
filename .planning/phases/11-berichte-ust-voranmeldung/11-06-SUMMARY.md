---
phase: 11-berichte-ust-voranmeldung
plan: 06
subsystem: reporting-api-persistence
tags: [minimal-api, ustva, euer, rls, festschreibung, postgres]

requires:
  - phase: 11-01
    provides: Soll/Ist recognition reader
  - phase: 11-02
    provides: USt-VA calculator, report model and per-Kz account seam
  - phase: 11-03
    provides: EÜR calculator and report model
  - phase: 11-04
    provides: ISO-8859-15 USt-VA XML writer
  - phase: 11-05
    provides: USt-VA and EÜR QuestPDF documents
provides:
  - "Authenticated /api/reports review, drill-down and export endpoints"
  - "Besteuerungsart-aware Kz drill-down by journal date or payment ValueDate"
  - "RLS-scoped idempotent USt-VA Draft filing persistence"
  - "Submitted-filing database immutability and cross-tenant isolation coverage"
affects: [reporting-ui, ustva-filing, api-composition]

tech-stack:
  added: []
  patterns:
    - "Minimal API routes delegate to internal handler cores used by integration tests"
    - "Postgres advisory transaction lock for per-tenant/per-period filing idempotency"
    - "ENABLE+FORCE RLS plus tenant_isolation and immutable-on-submit trigger"

key-files:
  created:
    - src/Numera.Api/Reporting/UstVaFiling.cs
    - src/Numera.Api/Endpoints/ReportEndpoints.cs
    - src/platform/Numera.Platform.Db/Migrations/20260804092442_UstVaFiling.cs
    - src/platform/Numera.Platform.Db/Migrations/20260804092442_UstVaFiling.Designer.cs
    - tests/Numera.IntegrationTests/ReportEndpointsTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-06-SUMMARY.md
  modified:
    - src/Numera.Api/Program.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
    - tests/Numera.IntegrationTests/CrossTenantIsolationCompletenessTests.cs

key-decisions:
  - "Kleinunternehmer review returns HTTP 200 with isKleinunternehmer=true, empty lines and Zahllast 0; USt-VA exports return 409 and never persist a filing"
  - "Soll drill-down reads Kz-contributing account postings by journal entry_date; Ist drill-down traces PaymentAllocation to invoice tax breakdown by Payment.ValueDate"
  - "XML generation serializes per tenant/period with a Postgres advisory lock and inserts at most one Draft; a new Draft links the latest Submitted filing as BerichtigtVonFilingId"
  - "Festschreibung remains explicit through the existing owner-only lock endpoint; report models expose IsFestgeschrieben and PDFs retain the vorläufig badge"

completed: 2026-08-04
---

# Phase 11-06: Report endpoints, filing persistence and integration wiring

**The Phase-11 calculators and renderers are now exposed through authenticated, RLS-scoped report endpoints, with correct Soll/Ist drill-down and an auditable USt-VA filing snapshot.**

## Accomplishments

- Added `UstVaFiling : ITenantEntity` with UUIDv7 identity, period/taxation metadata, JSON Kz snapshot, Zahllast, optional XML bytes, Draft/Submitted status, timestamps and correction back-link.
- Generated migration `20260804092442_UstVaFiling` in `Numera.Platform.Db`, including the tenant-leading `(tenant_id, jahr, zeitraum)` index, ENABLE+FORCE RLS, `tenant_isolation`, runtime DML grants/revokes and an immutable-on-submit trigger.
- Added authenticated review routes for USt-VA and EÜR plus XML/USt-VA-PDF/EÜR-PDF downloads.
- Added a discriminated Kz drill-down. Soll returns journal contributions selected by entry date and the line's contributing accounts. Ist returns the contributing invoice and payment with pro-rata net/VAT attribution selected by payment `ValueDate`.
- Persisted XML exports as one Draft filing per tenant/period under an advisory transaction lock. Submitted predecessors are linked rather than edited.
- Centralized all reporting DI and endpoint mapping in `Program.cs`.
- Added six real-Postgres integration assertions and added `UstVaFiling` to both halves of the RLS completeness suite.

## Immutable-on-submit trigger SQL

`UstVaFilingStatus.Submitted` is stored as integer value `1`; the migration contains exactly:

```sql
CREATE FUNCTION ust_va_filing_immutable_on_submit() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF OLD.status = 1 THEN
    RAISE EXCEPTION 'submitted ust_va_filing rows are append-only; create a correction filing';
  END IF;
  IF TG_OP = 'DELETE' THEN
    RETURN OLD;
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER ust_va_filing_immutable_on_submit
BEFORE UPDATE OR DELETE ON ust_va_filing
FOR EACH ROW EXECUTE FUNCTION ust_va_filing_immutable_on_submit();
```

The same migration also executes `REVOKE UPDATE, DELETE ON ust_va_filing FROM numera_app;` inside the established role-existence guard.

## Verification

- Migration generation command: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" ef migrations add UstVaFiling --project src/platform/Numera.Platform.Db --startup-project src/Numera.Api --configuration Release --no-build`.
- API Release build: successful, **0 warnings, 0 errors**.
- Integration-test project Release build: successful, **0 warnings, 0 errors**.
- Final full-solution Release build: successful, **0 warnings, 0 errors**.
- Integration tests were compiled but not run, per explicit instruction.
- No files were staged or committed.

## Deviations and uncertainties

- No HTTP test host exists in the endpoint-test convention inspected for this repository. Tests therefore call the internal endpoint handler cores directly with a real `numera_app` Postgres context, exactly like `LedgerSetupTests` and `LedgerJournalReadTests`; route mapping/auth composition is compile-checked in `Program.cs` rather than exercised through HTTP.
- The optional "lock then generate" route was not added. Locking remains an explicit owner-only operation through the existing ledger endpoint, while every USt-VA response/PDF exposes the calculator's all-months-locked `IsFestgeschrieben` state.
- The EF CLI printed its existing tools/runtime skew warning (tools 10.0.3, runtime 10.0.10) but generated the migration successfully.
- Runtime execution of the new migration SQL and integration assertions remains reviewer-run because integration tests were explicitly excluded from this implementation run.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-04*
