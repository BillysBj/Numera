---
phase: 12-belege-ausgaben
plan: 06
subsystem: receipt-email-intake
tags: [dotnet, mailkit, imap, greenmail, hangfire, rls, audit]

requires:
  - phase: 12-belege-ausgaben
    plan: 04
    provides: Shared scan/archive/Receipt/extraction-enqueue ingest pipeline
provides:
  - "Unique cryptographically random per-tenant receipt intake addresses"
  - "Worker-queue IMAP poll with strict token-to-one-tenant routing"
  - "Message-Id and attachment-content idempotency with RLS-scoped processing"
  - "GreenMail integration coverage for capture, dedup, RLS, and EICAR quarantine"
affects: [receipt-review, external-mailbox-provisioning, worker-deployment]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-06: Per-tenant receipt email intake

Each tenant can now provision one `belege-{token}@{domain}` address. The token is generated from
32 cryptographically random bytes and stored behind a unique index; it is never derived from tenant
or company data. A recurring Worker job polls the configured shared IMAP inbox, accepts only an
unambiguous active token mapping, establishes that tenant in a fresh scope, and sends supported
PDF/image/XML attachments through the shared scan-first receipt ingest pipeline.

## Implemented

- Added self-describing `TenantBelegeMailbox` and `ProcessedBelegeMail` CRM entities with one-row-
  per-tenant, globally unique token, and `(TenantId, MessageId)` uniqueness constraints.
- Added migration `20260804165712_BelegeMailIntake` plus its designer and updated model snapshot.
  Both operational tables have `ENABLE/FORCE ROW LEVEL SECURITY` and matching `tenant_isolation`
  policies; no WORM revoke or immutability trigger was applied to these mutable tables.
- Added the exact-token `resolve_belege_mailbox(text)` security-definer function. This is the narrow
  pre-tenant routing aperture required because RLS cannot expose the mapping before the worker knows
  which tenant GUC to set. It returns only active exact-token tenant IDs and grants execute only to
  `numera_app`; all subsequent reads and writes occur after `SetTenant` under normal RLS.
- Added `GET /api/receipts/mailbox`, which lazily provisions the current tenant's address and returns
  the address, active state, and creation time. Token collision/first-access races are retried and
  resolved through the unique constraints.
- Added config-bound `BelegeMailboxOptions` (`Host`, `Port`, `UseSsl`, credentials, `Domain`, and a
  five-minute default `PollCron`).
- Added `PollBelegMailboxJob` with `worker` queue, automatic retry, and distributed no-overlap guard.
  It polls UNSEEN mail, considers `Delivered-To` and MIME `To`, rejects unresolved or multi-tenant
  routing by marking the mail seen without ingest, and marks successful/duplicate mail seen.
- The poll loads supported attachments, computes a stable composite attachment content hash, checks
  both Message-Id and content hash, invokes `ReceiptIngestService.IngestAsync` with `Source=Email`
  and no uploaded user, then records the processed-message row.
- Extended the shared ingest type resolver minimally so XML attachments use the same scan/archive/
  receipt/extraction-enqueue pipeline requested by the plan.
- Extended the Worker with a project reference to the Api assembly and a small registration helper
  for exactly the polling closure: MailKit IMAP, scanner selection, receipt dedup/ingest, audit, and
  the Hangfire client. No receipt extractor, posting engine, account resolver, or ledger booking
  service is registered in the Worker.
- Registered a fixed reserved application principal
  `00000000-0000-0000-0000-000000000001` as the Worker's `ICurrentUser`. The existing `AuditWriter`
  therefore remains strict and writes the system actor atomically with headless Receipt changes;
  no null-user tolerance was added to the general audit path.
- Added four compile-verified GreenMail tests covering benign PDF capture + WORM archive + audit,
  same-Message-Id redelivery idempotency, tenant-B-only visibility under database RLS, and EICAR
  quarantine without an archive or bookable journal link. The test uses the existing generic
  Testcontainers package and `greenmail/standalone:2.1.3`; no package was added.

## Files modified

- `src/Numera.Api/Program.cs`
- `src/Numera.Api/Services/ReceiptIngestService.cs`
- `src/Numera.Worker/Numera.Worker.csproj`
- `src/Numera.Worker/Program.cs`
- `src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs`

## Files created

- `src/modules/Numera.Modules.Crm/TenantBelegeMailbox.cs`
- `src/Numera.Api/Jobs/PollBelegMailboxJob.cs`
- `src/Numera.Api/Endpoints/MailboxEndpoints.cs`
- `src/Numera.Api/Services/BelegeMailboxOptions.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260804165712_BelegeMailIntake.cs`
- `src/platform/Numera.Platform.Db/Migrations/20260804165712_BelegeMailIntake.Designer.cs`
- `tests/Numera.IntegrationTests/BelegeMailIntakeTests.cs`
- `.planning/phases/12-belege-ausgaben/12-06-SUMMARY.md`

## Verification

- SDK: `10.0.301` from `C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Api Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Worker Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Integration-test project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Migration snapshot contains both new CRM entities and all three generated unique indexes.
- Repository whitespace check passed; Git reported only existing LF-to-CRLF working-copy notices.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- A narrowly scoped security-definer resolver was added beyond the two tables because direct lookup
  through an unscoped `NumeraDbContext` is intentionally blocked by FORCE RLS. The migration role
  must own/create this function with RLS-bypass authority, matching the current migration setup.
- `ReceiptIngestService` was minimally extended for XML because the plan explicitly requires emailed
  XML to traverse the shared ingest method, while the 12-04 implementation accepted only PDF/images.
- The Worker needed a direct project reference to `Numera.Api` to consume the shared Api-owned job and
  ingest service. No NuGet reference or restore was added.
- EF migration scaffolding succeeded but reported that the installed EF tool `10.0.3` is older than
  runtime `10.0.10`; generated output and all Release builds still completed successfully.
- GreenMail runtime assertions remain for the reviewer-run integration suite; only compilation was
  performed here. External production catch-all DNS/mailbox provisioning remains configuration work.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was untouched.
