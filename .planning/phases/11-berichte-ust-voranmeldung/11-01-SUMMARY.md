---
phase: 11-berichte-ust-voranmeldung
plan: 01
subsystem: reporting
tags: [reporting, vat, cash-basis, postgres, rls]

requires:
  - phase: 10-04
    provides: frozen invoice postings and sales-document tax breakdowns
  - phase: 10-05
    provides: append-only payments, allocations and payment reversals
  - phase: 10-06
    provides: RLS-scoped raw SqlQuery read pattern
provides:
  - "Soll recognition grouped from frozen postings by journal entry date"
  - "Payment-date cash recognition attributed pro rata from frozen invoice tax buckets"
  - "Real-Postgres integration coverage for period movement, partial payment and reversal"
affects: [ust-va, euer, reports]

tech-stack:
  added: []
  patterns:
    - "RLS-scoped EF Database.SqlQuery reads without manual tenant predicates"
    - "Payment allocation divided by frozen invoice gross for cash-basis attribution"

key-files:
  created:
    - src/Numera.Api/Reporting/RecognitionModels.cs
    - src/Numera.Api/Reporting/RecognitionReader.cs
    - tests/Numera.IntegrationTests/ReportRecognitionTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-01-SUMMARY.md
  modified: []

key-decisions:
  - "Phase-10 production entities, posting engine, seed data and migrations remain unchanged"
  - "Cash recognition returns one row per allocation and frozen breakdown bucket, with no report-layer rounding"
  - "Negative reversal allocations flow through the same SQL and produce negative recognized net and VAT"

completed: 2026-08-03
---

# Phase 11-01: Shared report-time recognition read model

**Soll reporting now reads frozen tax-bearing postings by booking date, while Ist-USt-VA and EÜR share a payment-date read that attributes frozen invoice net and VAT pro rata without changing the Phase-10 ledger.**

## Accomplishments

- Added the exact typed `SollRecognitionRow` and `CashRecognitionRow` report rows.
- Added an RLS-scoped Soll query grouped by Kennziffer, stored tax category/rate and posting direction.
- Added an RLS-scoped cash query joining payments through allocations and open items to each invoice's frozen tax breakdown.
- Kept all calculations in PostgreSQL `numeric`/C# `decimal` and introduced no rounding.
- Left reversal rows unfiltered so their negative allocations reverse recognition in their own value-date period.
- Added four real-Postgres scenarios with hand-computed expected values: Soll invoice/other month, M1 invoice/M2 payment movement, half-payment pro rata, and payment reversal.

## Exact SQL

### Soll recognition

```sql
SELECT a.ustva_kennziffer AS "Kennziffer",
       p.tax_category AS "TaxCategory",
       p.tax_rate_percent AS "TaxRatePercent",
       p.direction AS "Direction",
       SUM(p.amount) AS "Amount"
  FROM postings p
  JOIN journal_entries je ON je.id = p.journal_entry_id
  JOIN accounts a ON a.id = p.account_id
 WHERE je.entry_date >= {from}
   AND je.entry_date <= {to}
   AND a.ustva_kennziffer IS NOT NULL
 GROUP BY a.ustva_kennziffer,
          p.tax_category,
          p.tax_rate_percent,
          p.direction
 ORDER BY a.ustva_kennziffer,
          p.tax_category,
          p.tax_rate_percent,
          p.direction
```

### Cash recognition

```sql
SELECT b.tax_category AS "TaxCategory",
       b.vat_rate_percent AS "VatRatePercent",
       (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.taxable_base AS "NetAmount",
       (pa.allocated_amount / NULLIF(sd.total_gross, 0)) * b.tax_amount AS "VatAmount",
       p.value_date AS "RecognizedOn"
  FROM payment p
  JOIN payment_allocation pa ON pa.payment_id = p.id
  JOIN open_items oi ON oi.id = pa.open_item_id
  JOIN sales_documents sd ON sd.id = oi.document_id
  JOIN sales_document_tax_breakdown b ON b.document_id = sd.id
 WHERE p.value_date >= {from}
   AND p.value_date <= {to}
 ORDER BY p.value_date,
          p.id,
          pa.id,
          b.tax_category,
          b.vat_rate_percent,
          b.id
```

The interpolated `{from}` and `{to}` values are parameterized by EF Core's `SqlQuery` API. PostgreSQL RLS supplies tenant isolation; neither query contains a tenant predicate.

## Column-name verification

The entity declarations and `NumeraDbContextModelSnapshot` agree on every requested identifier:

- `sales_document_tax_breakdown`: `document_id`, `taxable_base`, `tax_amount`, `vat_rate_percent`, `tax_category`
- `payment`: `id`, `value_date`, `amount`
- `payment_allocation`: `payment_id`, `open_item_id`, `allocated_amount`
- `open_items`: `id`, `document_id`
- `sales_documents`: `id`, `total_gross`
- `accounts`: `id`, `ustva_kennziffer`
- `postings`: `journal_entry_id`, `account_id`, `tax_category`, `tax_rate_percent`, `direction`, `amount`
- `journal_entries`: `id`, `entry_date`

No planned column name required correction. The singular table names `payment`, `payment_allocation` and `sales_document_tax_breakdown` were confirmed explicitly.

## Verification

- `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build src/Numera.Api --configuration Release --no-restore`: 0 warnings, 0 errors (SDK 10.0.301).
- `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build tests/Numera.IntegrationTests --configuration Release --no-restore`: 0 warnings, 0 errors (SDK 10.0.301).
- Integration tests were compiled but not run, per reviewer instruction.

## Deviations and uncertainties

- No production-code deviation from D1/D2/D3: no DI registration and no Phase-10 entity, engine, seed or migration change.
- The Phase-10 SKR03 seed intentionally has no Kennziffer on output-VAT accounts `1776`/`1771` because Plan 02 computes Kz 83. The Soll reader test assigns Kz `83` to those mutable accounts only inside its isolated tenant so the plan's requested output-VAT-leg assertion is exercised without changing production seed data.
- Runtime SQL/materialization remains to be confirmed by the reviewer-run Testcontainers suite; it was not executed locally by instruction.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-03*
