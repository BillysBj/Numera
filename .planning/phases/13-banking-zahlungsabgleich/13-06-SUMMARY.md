---
phase: 13-banking-zahlungsabgleich
plan: 06
subsystem: banking-api-reconciliation
tags: [banking, reconciliation, payments, hangfire, multipart, postgres]

requires:
  - phase: 13-banking-zahlungsabgleich
    provides: Bank entities, ingest/dedupe, statement importers, scorer, sync job, provider port
  - phase: payments-ledger
    provides: PaymentService RecordAsync/ReverseAsync and Bank-to-receivable posting
provides:
  - Authenticated bank connection, consent, account list, sync, and statement-import endpoints
  - Authenticated review queue, ranked suggestions, explicit confirm, split, unmatch, and ignore endpoints
  - Confirm-to-book convergence through PaymentService with no duplicate posting logic
  - Real-Postgres integration coverage for confirmation, split, reversal, human gate, and idempotency

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-05
---

# Phase 13-06: Banking endpoints and confirm-to-book convergence

The banking API now exposes the connected-account and offline-import surfaces and converges every
human-confirmed incoming reconciliation on the existing payment service. Suggestions remain
read-only. Only `POST /api/bank-transactions/{id}/confirm` creates a payment and ledger booking;
correction uses the existing payment reversal service.

## Implemented

- Added numeric-enum wire records for bank Web Forms, account/consent state, import results, the
  paged transaction queue, ranked candidates, confirm allocations, and match command results.
- Added authenticated `/api/bank-accounts` endpoints for:
  - starting a connection Web Form and persisting a pending `BankConnection`;
  - starting re-authorization;
  - listing tenant-visible accounts with consent and synchronization health;
  - refreshing/returning a connection consent snapshot;
  - enqueueing the existing tenant-aware `SyncBankTransactionsJob` with HTTP 202;
  - uploading CSV, MT940, or CAMT.053 statements with 10 MiB/type validation, then dispatching and
    ingesting through the same `BankTransactionIngestService` dedupe path used by provider sync.
- Added authenticated `/api/bank-transactions` endpoints for the default open review queue,
  optional status/account filters, read-only ranked suggestions, explicit confirmation, reversal,
  and ignore.
- Confirmation enforces the receivable-only direction, an exact allocation sum, and BankTransfer.
  Multiple `PaymentAllocationInput` rows pass through unchanged for native split settlement.
- Already-confirmed rows return their existing payment and do not call the payment service again.
  Suggestions do not mutate confidence, match status, allocations, payments, or journal entries.
- Ignore never books and rejects an already-booked row until it is explicitly unmatched.
- Registered `ReconciliationScorer`, all three `IBankStatementImporter` implementations, and
  `BankStatementImportDispatcher`; mapped both new endpoint groups in the API host.
- Added five Postgres integration tests covering exact confirmation and Bank/receivable legs, a
  EUR 300 split over EUR 200 + EUR 100 open items, unmatch/reversal, the human-confirm gate, and
  repeat-confirm idempotency.

## Exact payment seam reused

From `PaymentContracts.cs`, unchanged:

```csharp
public sealed record RecordPaymentRequest(
    decimal? Amount,
    DateOnly ValueDate,
    PaymentMethod Method,
    string? Reference,
    IReadOnlyList<PaymentAllocationInput> Allocations);

public sealed record PaymentAllocationInput(Guid OpenItemId, decimal Amount);
```

Confirm calls:

```csharp
new RecordPaymentRequest(
    Amount: transaction.Amount,
    ValueDate: transaction.ValueDate,
    Method: PaymentMethod.BankTransfer,
    Reference: transaction.EndToEndId ?? transaction.Purpose,
    Allocations: request.Allocations)
```

through `PaymentService.RecordAsync(request, CancellationToken)`. Unmatch calls
`PaymentService.ReverseAsync(Guid paymentId, CancellationToken)` with the stored
`MatchedPaymentId`. `PaymentMethod.BankTransfer` is numeric wire/database value `0`. No posting,
open-item, document-settlement, allocation, or reversal logic was duplicated in the banking API.

## Files created

- `src/Numera.Api/Contracts/BankingContracts.cs`
- `src/Numera.Api/Endpoints/BankAccountEndpoints.cs`
- `src/Numera.Api/Endpoints/BankTransactionEndpoints.cs`
- `tests/Numera.IntegrationTests/BankReconciliationBookingTests.cs`
- `.planning/phases/13-banking-zahlungsabgleich/13-06-SUMMARY.md`

## Files modified

- `src/Numera.Api/Program.cs`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- `dotnet build Numera.sln --configuration Release --no-restore`: passed with 0 warnings and
  0 errors.
- `dotnet build src/modules/Numera.Modules.Banking/Numera.Modules.Banking.csproj --configuration
  Release --no-restore`: passed with 0 warnings and 0 errors, explicitly confirming the module's
  Release output.
- The new integration tests compiled as part of the solution build.
- Integration tests were deliberately not run, per reviewer instruction.
- No package/project reference was added. No file was staged or committed.

## Deviations and uncertainties

- No functional plan deviation.
- The existing `BankAccount` model requires an `Iban` but has no import-only discriminator or
  nullable IBAN. Automatic import therefore reuses one tenant-visible account with
  `Iban = "IMPORT"`, `DisplayName = "Kontoauszug-Import"`, and no connection/provider account id.
- `PaymentService.RecordAsync` owns and commits its own database transaction. As required by the
  plan, the endpoint marks the bank transaction confirmed immediately after service success in a
  second save; making both commits one database transaction would require changing the established
  payment seam outside this plan's ownership.
- The solution configuration reports the Banking project output path as `bin/Debug` during a
  Release solution build. A separate explicit Release build emitted the same module successfully to
  `bin/Release`; the new API and integration-test assemblies also emitted under `bin/Release`.
- Runtime Postgres assertions remain for the reviewer-run integration-test command.

---
*Phase: 13-banking-zahlungsabgleich*
*Completed: 2026-08-05*
