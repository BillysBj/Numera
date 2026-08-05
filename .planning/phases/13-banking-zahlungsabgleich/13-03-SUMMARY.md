---
phase: 13-banking-zahlungsabgleich
plan: 03
subsystem: bank-statement-import
tags: [dotnet, banking, camt053, mt940, csv, import]

requires:
  - phase: 13-01
    provides: Shared IBankStatementImporter port, BankTransactionDraft, and source enum
provides:
  - "Dependency-free CAMT.053, MT940, and CSV statement importers"
  - "One normalized BankTransactionDraft output shape for every file format"
  - "Metadata-based dispatcher for the supported offline statement formats"
affects: [bank-transaction-ingest, import-deduplication, reconciliation, banking-api]

completed_tasks: [1, 2]
pending_tasks: []
completed: 2026-08-05
---

# Phase 13-03: Statement importers

The Banking module now imports CAMT.053 XML, MT940, and a defined CSV layout into the same
`BankTransactionDraft` contract used by the provider-neutral ingest pipeline. Credit amounts are
positive, debit amounts are negative, and no importer performs persistence, deduplication, matching,
or booking.

## Implemented

- Added a namespace-tolerant XLinq CAMT.053 parser that validates the
  `Document/BkToCstmrStmt/Stmt` structure and maps each `Ntry` amount/sign, value and booking dates,
  remittance text, direction-appropriate debtor/creditor name and IBAN, end-to-end reference, and
  account-servicer or entry reference.
- Added a dependency-free MT940 parser for `:61:` value date, D/C mark, and decimal amount. It reads
  structured `:86:` purpose/name/account subfields when present, retains useful unstructured purpose
  text as a fallback, accepts UTF-8 and Latin-1 input, and intentionally leaves `ProviderId` null for
  ingest-layer fingerprinting.
- Added a dependency-free RFC 4180 CSV parser with the documented fixed header
  `date,amount,purpose,counterparty_name,counterparty_iban`. It supports quoted German decimal-comma
  amounts, semicolon-delimited German exports, ISO/German dates, escaped quotes, and multiline quoted
  fields while keeping all money parsing on `decimal`.
- Added `BankStatementImportDispatcher`, which snapshots the supplied importer sequence, selects the
  first metadata-compatible importer, materializes its shared drafts, and rejects unknown formats
  with `ArgumentException("Nicht unterstütztes Kontoauszugsformat")`.
- Added minimal two-entry fixtures for every format and plain, database-free xUnit coverage for each
  importer, all dispatcher routes, the common normalized output, signs, dates, purpose, counterparty
  name/IBAN, CAMT references, MT940 null provider IDs, and unsupported-format rejection.

## Files created

- `src/modules/Numera.Modules.Banking/Import/Camt053Importer.cs`
- `src/modules/Numera.Modules.Banking/Import/Mt940Importer.cs`
- `src/modules/Numera.Modules.Banking/Import/CsvImporter.cs`
- `src/modules/Numera.Modules.Banking/Import/BankStatementImportDispatcher.cs`
- `tests/Numera.IntegrationTests/BankStatementImporterTests.cs`
- `tests/Numera.IntegrationTests/Fixtures/sample-camt053.xml`
- `tests/Numera.IntegrationTests/Fixtures/sample.mt940`
- `tests/Numera.IntegrationTests/Fixtures/sample.csv`
- `.planning/phases/13-banking-zahlungsabgleich/13-03-SUMMARY.md`

## Dependencies and project files

- No NuGet package was added; all three parsers use only .NET platform APIs.
- `Numera.Modules.Banking.csproj` and all other project files were unchanged.
- No Program.cs, DI registration, migration, or out-of-plan source file was touched.

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Banking project build with `--configuration Release --no-restore`: passed with 0 warnings and
  0 errors.
- Integration-test project build with `--configuration Release --no-restore`: passed with 0 warnings
  and 0 errors, compiling the new parser tests and their production dependencies.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- No functional plan deviation. The plan's accepted dependency-free fallback was used for CAMT.053,
  and the explicit no-package instruction was followed for MT940 and CSV as well.
- `IBankStatementImporter.CanImport` only receives `fileName` and `contentType`, not the content stream.
  It therefore performs the possible metadata check; CAMT root/statement validation and MT940 `:61:`
  marker validation happen immediately in `ParseAsync`. Supporting content-sniffing in `CanImport`
  would require changing the phase-13-01 port, which was outside this plan's ownership.
- Runtime parser assertions remain for the reviewer-run test command because integration tests were
  explicitly not executed here.
- No file was staged or committed. The pre-existing untracked `.claude/` directory was left untouched.
