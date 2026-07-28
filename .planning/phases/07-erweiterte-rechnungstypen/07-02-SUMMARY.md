# Plan 07-02 Summary — INV-07 Core

Implemented Abschlagsrechnung and Schlussrechnung as append-only `DocumentType` values
with independent `AR-` and `SR-` numbering series.

## Delivered

- Added the frozen `sales_document_prepayment` entity and EF migration with:
  - UUIDv7 client keys and `numeric(19,4)` monetary snapshots
  - FK to the owning Schlussrechnung
  - `ENABLE` + `FORCE ROW LEVEL SECURITY` and `tenant_isolation`
  - the existing sales-document child immutability trigger
- Finalization keeps full net, VAT breakdown, tax, and gross totals while setting a
  Schlussrechnung's BT-115 `AmountDue` to BT-112 minus frozen BT-113 gross prepayments.
- Invalid negative or over-gross prepayment totals abort before numbering.
- Open items for Rechnung, Abschlagsrechnung, and Schlussrechnung use `AmountDue`,
  including the Skonto preview base.
- Widened all four Rechnung gates: KoSIT dry-run, Storno eligibility, credit-note
  eligibility, and open-item creation.
- Recorded the accepted v1 out-of-order Storno limitation at the Storno gate. No
  deduction-chain cascade or recomputation was added.
- Added PostgreSQL 18 integration coverage for AR/SR numbering, full Abschlag
  receivable, Schlussrechnung residual receivable with unchanged VAT, rollback on
  over-deduction, Storno for both new types, and prepayment RLS isolation.

## Constraint Note

The plan task text requests a `Prepayments` collection on `SalesDocument.cs`, but that
file is absent from the plan frontmatter and the execution constraint permits only
frontmatter files. It was therefore not modified. The prepayment entity declares its
FK navigation to `SalesDocument`, is discovered through `ITenantEntity`, and the
finalize path queries `db.Set<SalesDocumentPrepayment>()` directly, so the delivered
schema and core behavior do not depend on the omitted parent collection.

## Verification

- `dotnet ef migrations add DownPaymentInvoices --project src/platform/Numera.Platform.Db --startup-project src/Numera.Api`
  - succeeded; EF CLI reported its installed 10.0.3 tool is older than runtime 10.0.10
- `dotnet build Numera.sln -c Debug`
  - succeeded with 0 warnings and 0 errors
- Focused `DownPaymentInvoiceTests`
  - 5 passed, 0 failed, 0 skipped
- `dotnet test tests/Numera.IntegrationTests`
  - 107 passed, 0 failed, 0 skipped
