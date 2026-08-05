---
phase: 13-banking-zahlungsabgleich
plan: 01
subsystem: banking-foundation
tags: [dotnet, ef-core, postgres, rls, banking, finapi-port, multi-tenant]

requires:
  - phase: 10-12
    provides: Receivable payments, immutable posting flow, and established module/RLS patterns
provides:
  - "Tenant-isolated bank connection, account, and normalized transaction model"
  - "Unique per-tenant/account transaction deduplication guarantee"
  - "Provider and statement-import ports with a no-network stub default"
  - "Shared Banking DI seam for API and Worker"
affects: [bank-sync, statement-import, reconciliation, finapi, banking-api]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-05
---

# Phase 13-01: Banking module foundation

The Banking module now provides the tenant-scoped connection, account, and normalized transaction
model required by the rest of Phase 13. Provider and file-import boundaries converge on
`BankTransactionDraft`, the shipping provider is a no-network stub, and the API and Worker share a
minimal `AddBankingModule` registration seam. No finAPI HTTP client, statement parser, scoring,
background job, endpoint, or payable-side posting behavior was added.

## Implemented

- Added `Numera.Modules.Banking`, mirroring the CRM project shape and referencing Platform.Db and
  Platform.Money.
- Added sealed UUIDv7 `ITenantEntity` models for `BankConnection`, `BankAccount`, and
  `BankTransaction`, including nullable encrypted-credential storage seams, PSD2 consent state,
  incremental sync cursor, signed `numeric(19,4)` transaction amounts, reconciliation state,
  nullable matched Payment link, and `numeric(5,4)` confidence.
- Added explicit account/connection and transaction/account relationships. Import-only accounts can
  leave `BankConnectionId` null; a transaction must reference a bank account.
- Added the three requested transaction indexes, including the unique
  `(tenant_id, bank_account_id, dedupe_key)` idempotency constraint.
- Added all Banking enums plus `IBankConnectionProvider`, `IBankStatementImporter`, their DTO records,
  and the normalized `BankTransactionDraft` record.
- Added `StubBankConnectionProvider`: account and transaction reads are empty, consent reports
  `Active` without expiry, and connect/re-auth throws the required German configuration message.
- Added API and integration-test project references so the Banking assembly is copied next to
  Platform.Db and available for both reflective EF discovery and direct test type use.
- Added the minimal `AddBankingModule(IServiceCollection, IConfiguration)` seam and registered it with
  `IBankConnectionProvider -> StubBankConnectionProvider` in both API and Worker.
- Generated the phase's single migration, `20260805130444_Banking`, and updated the model snapshot.
  The generated migration and designer contain all three Banking entities, confirming design-time
  auto-discovery through the API project reference and output-directory module probing.
- Added `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, and the verbatim
  `tenant_isolation` policy pattern for `bank_connection`, `bank_account`, and `bank_transaction`,
  with matching Down teardown. No append-only revoke or immutability trigger was added because match
  state is mutable.
- Extended the consolidating cross-tenant isolation suite with read-isolation and cross-tenant write
  rejection cases for all three Banking tables, including the required account parent for a bank
  transaction.

## Files modified

- `src/Numera.Api/Numera.Api.csproj`
- `src/Numera.Api/Program.cs`
- `src/Numera.Worker/Numera.Worker.csproj`
- `src/Numera.Worker/Program.cs`
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs`
- `tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj`
- `tests/Numera.IntegrationTests/CrossTenantIsolationCompletenessTests.cs`

## Files created

- `src/modules/Numera.Modules.Banking/Numera.Modules.Banking.csproj`
- `src/modules/Numera.Modules.Banking/BankConnection.cs`
- `src/modules/Numera.Modules.Banking/BankAccount.cs`
- `src/modules/Numera.Modules.Banking/BankTransaction.cs`
- `src/modules/Numera.Modules.Banking/BankingEnums.cs`
- `src/modules/Numera.Modules.Banking/IBankConnectionProvider.cs`
- `src/modules/Numera.Modules.Banking/IBankStatementImporter.cs`
- `src/modules/Numera.Modules.Banking/BankTransactionDraft.cs`
- `src/modules/Numera.Modules.Banking/StubBankConnectionProvider.cs`
- `src/Numera.Api/Services/BankingModuleServiceCollectionExtensions.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260805130444_Banking.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260805130444_Banking.Designer.cs`
- `.planning/phases/13-banking-zahlungsabgleich/13-01-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Banking project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Full `Numera.sln` Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- `ef migrations list` shows `20260805130444_Banking` last.
- Migration inspection confirms three Banking tables, all six ENABLE/FORCE statements, all three
  `tenant_isolation` policies, their Down teardown, and the unique dedupe index.
- Model snapshot and migration designer contain `BankConnection`, `BankAccount`, and
  `BankTransaction`, confirming module auto-discovery works.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- Worker required a direct project reference to `Numera.Modules.Banking`: its reference to the
  executable API project did not expose Banking as a compile-time transitive dependency. This is the
  only plan-file scope addition and is required for the explicitly requested Worker registrations.
- The brand-new project initially had no assets file. A targeted restore was run against the existing
  host NuGet package cache after the sandbox could not read the host user's NuGet configuration; no
  package or package version was added or changed.
- EF scaffolding/listing reports that the installed EF CLI tool `10.0.3` is older than runtime
  `10.0.10`. Scaffolding, model discovery, migration listing, and both Release builds succeeded.
- Runtime Postgres migration application and RLS assertions remain for the reviewer-run Testcontainers
  integration suite. No integration tests were executed here.
- No files were staged or committed. Pre-existing untracked planning/context and `.claude/` files were
  left untouched.
