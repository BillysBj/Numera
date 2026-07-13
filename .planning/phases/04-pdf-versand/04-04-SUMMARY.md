---
phase: 04-pdf-versand
plan: 04
subsystem: email-dispatch
tags: [mailkit, mimekit, mailpit, hangfire, background-job, smtp, rls, tenant-context, document-email, sentat, testcontainers]

# Dependency graph
requires:
  - phase: 04-pdf-versand
    plan: 01
    provides: "document_email table (RLS: send-status + retry ledger, EmailStatus) + SalesDocument.SentAt whitelisted lifecycle column"
  - phase: 04-pdf-versand
    plan: 03
    provides: "DocumentPdfService.GetOrRender (render-if-absent) + RenderDocumentPdfJob tenant-re-establishment pattern + Api default-queue Hangfire"
  - phase: 03-belegkette-rechnungskern
    provides: "FinalizeCoreAsync + frozen RecipientSnapshot (carries recipient Email) + the mutate→audit→SaveChanges idiom"
provides:
  - "IEmailSender seam (EmailMessage + EmailAttachment) — provider-swappable dispatch (MailKit for v1, SendGrid/Postmark later)"
  - "MailKitEmailSender — multipart (HTML+text) body + PDF attachment via BodyBuilder, SMTP send (SecureSocketOptions.None/StartTls) from EmailOptions"
  - "SendDocumentEmailJob — Hangfire job (Api default queue): fresh scope + SetTenant + render-if-absent + send + document_email advance + SalesDocument.SentAt flip; rethrow so AutomaticRetry(3) handles transient SMTP faults"
  - "POST /api/documents/{id}/send — records a Queued document_email (RLS + audit) then enqueues after commit; 409 Draft / 404 unknown / 422 no-recipient"
  - "Mailpit service in docker-compose (SMTP 1025 / UI+API 8025) for local + testable dispatch"
affects: [04-05 (frontend send button → POST /{id}/send + send-status display)]

# Tech tracking
tech-stack:
  added: [MailKit 4.17.0 (Api host; MimeKit 4.17.0 transitive)]
  patterns:
    - "IEmailSender provider seam so SMTP/MailKit is swappable without touching the send job"
    - "Enqueue-after-commit: record the Queued document_email row, SaveChanges, THEN BackgroundJob.Enqueue (never send inline on the request thread)"
    - "Render-if-absent before send (GetOrRender) so an e-mail never goes out with a missing attachment (Pitfall 5)"
    - "Tenant re-establishment in a fresh DI scope inside the job (mirrors RenderDocumentPdfJob/WelcomeEmailJob) so RLS applies to the send + status writes"
    - "Failure path records EmailStatus.Failed + LastError + AttemptCount then rethrows → Hangfire AutomaticRetry(3) with backoff"

key-files:
  created:
    - src/Numera.Api/Services/IEmailSender.cs
    - src/Numera.Api/Services/EmailOptions.cs
    - src/Numera.Api/Services/MailKitEmailSender.cs
    - src/Numera.Api/Services/DocumentEmailTemplates.cs
    - src/Numera.Api/Jobs/SendDocumentEmailJob.cs
    - tests/Numera.IntegrationTests/DocumentEmailSendTests.cs
  modified:
    - docker-compose.yml
    - src/Numera.Api/Numera.Api.csproj
    - src/Numera.Api/appsettings.json
    - src/Numera.Api/Program.cs
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
    - src/Numera.Api/Contracts/SalesDocumentContracts.cs

key-decisions:
  - "Send runs on the Api's default-queue Hangfire server (the Api has the Sales/QuestPDF/MailKit refs; Numera.Worker does not — RESEARCH.md Pitfall 2, LOCKED)"
  - "The send language is a JOB ARGUMENT (RunAsync(tenantId, documentEmailId, language)), NOT a new document_email column — no schema change / migration in this plan"
  - "The recipient address is resolved at the ENDPOINT (request override else the frozen RecipientSnapshot Email) and stored on the document_email row; the job just uses email.ToAddress"
  - "On first success the job flips BOTH SalesDocument.SentAt AND Status=Sent (both whitelisted lifecycle columns); first-send wins (only when SentAt is null)"
  - "Mailpit connects with SecureSocketOptions.None when UseSsl=false (deterministic plain SMTP) rather than Auto's STARTTLS probe"

patterns-established:
  - "Drive the REAL SendDocumentEmailJob in-test over a minimal DI container (scoped ICurrentTenant + DbContext + DocumentPdfService + the real MailKitEmailSender) pointed at a Testcontainers Mailpit — deterministic + faithful to production tenant re-establishment"
  - "Assert dispatch by querying Mailpit's HTTP API (GET /api/v1/messages + /api/v1/message/{id}) for the captured message + PDF attachment"

# Metrics
duration: 60min
completed: 2026-07-13
---

# Phase 4 Plan 04: E-mail Dispatch Summary

**A user sends a finalized document to the customer by e-mail with the rendered §14 PDF attached via `POST /api/documents/{id}/send`: the endpoint records a Queued `document_email` row and enqueues `SendDocumentEmailJob` (Api default queue) which re-establishes tenant context, renders-if-absent through `DocumentPdfService.GetOrRender`, sends via MailKit/SMTP (captured by Mailpit locally + in tests), then advances `document_email` to Sent and flips `SalesDocument.SentAt` — with Hangfire retrying transient SMTP faults.**

## Performance

- **Duration:** ~60 min
- **Started:** 2026-07-13T20:58:54Z
- **Completed:** 2026-07-13T21:58:36Z
- **Tasks:** 3
- **Files:** 12 (6 created, 6 modified)

## Accomplishments
- **`IEmailSender` seam + `MailKitEmailSender`.** `EmailMessage` (To, Subject, HtmlBody, TextBody, one `EmailAttachment`) is the provider-agnostic contract; `MailKitEmailSender` builds a `multipart/alternative` body with the PDF attached via `BodyBuilder` and sends over SMTP (`SecureSocketOptions.None` for Mailpit / `StartTls` when `UseSsl`, auth only when a username is configured). `EmailOptions` binds the `Email` config section (Mailpit defaults host `localhost` / port `1025`). `DocumentEmailTemplates.Build(language, number)` supplies the bilingual (de/en) covering subject + HTML/text body (the legal content is in the PDF).
- **`SendDocumentEmailJob`.** Mirrors `RenderDocumentPdfJob`/`WelcomeEmailJob`: fresh DI scope + `ICurrentTenant.SetTenant` (RLS applies to the send + status writes), `[AutomaticRetry(Attempts = 3)]`, Api default queue. It loads the RLS-scoped `document_email` row, renders-if-absent via `GetOrRender` (never sends without an attachment — Pitfall 5), sends via `IEmailSender`, then on success sets `Status=Sent` + `SentAt` + `AttemptCount++` and flips `SalesDocument.SentAt`/`Status=Sent` (first-send wins) in one `SaveChanges`; on failure records `Status=Failed` + `LastError` + `AttemptCount++` and rethrows so Hangfire retries.
- **`POST /api/documents/{id}/send`.** Guards the doc is finalized (409 Draft, 404 unknown), resolves the recipient (request `toAddress` else the frozen `RecipientSnapshot` `Email`, 422 if neither), records a Queued `document_email` row (RLS + audit `sales_document.email_queued`), and `BackgroundJob.Enqueue<SendDocumentEmailJob>` AFTER the row commits (enqueue-after-commit). Returns 202 with the row id + status. `SendDocumentEmailRequest { toAddress?, language? }` added; `ResolveRecipientEmail` reads the recipient e-mail from the frozen jsonb (Web camelCase, PascalCase tolerated).
- **Mailpit in docker-compose** (`axllent/mailpit:latest`, SMTP 1025 / UI+API 8025) — additive alongside postgres/keycloak, no auth.
- **End-to-end integration test** on real postgres:18 + a real Testcontainers Mailpit: finalize through the production core → run the real `SendDocumentEmailJob` → assert Mailpit captured exactly one message to the recipient with the expected subject and a `RE-2026-00001.pdf` (`application/pdf`) attachment, that `document_email` flipped to Sent (SentAt set, AttemptCount=1) and `SalesDocument.SentAt`/Status flipped, and that `document_email` is RLS-isolated (a second tenant sees 0). Full integration suite **67/67 green** (66 prior + 1 new); solution build 0 warnings.

## Task Commits

1. **Task 1: Mailpit + MailKit IEmailSender seam + config** — `6f8568c` (feat)
2. **Task 2: SendDocumentEmailJob + POST /{id}/send + status tracking + SentAt flip** — `09e3772` (feat)
3. **Task 3: End-to-end Mailpit send integration test** — `e4ed53f` (test)

## Decisions Made
- **Default-queue execution.** The send job runs on the Api's Hangfire server (default queue), not the `worker` queue whose host lacks the Sales/QuestPDF/MailKit references (RESEARCH.md Pitfall 2, LOCKED). Still off the HTTP request thread.
- **Language as a job argument, not a column.** `document_email` (from 04-01) has no language column; adding one would require a migration this plan does not own. The send language rides as a `SendDocumentEmailJob.RunAsync` argument (mirroring `RenderDocumentPdfJob`), so no schema change was needed.
- **Recipient resolved at the endpoint.** The endpoint resolves the recipient address (request override else the frozen `RecipientSnapshot` `Email`) and stores it on the `document_email` row; the job simply uses `email.ToAddress`. Keeps the job free of snapshot-parsing and makes the stored row self-describing.
- **Flip both SentAt and Status=Sent on first success.** Both are whitelisted lifecycle columns the `sales_document_immutable` trigger permits; `DocumentStatus.Sent` is exactly "the finalized document was dispatched", and Storno still accepts a `Sent` invoice.
- **Deterministic plain SMTP for Mailpit.** `SecureSocketOptions.None` when `UseSsl=false` avoids `Auto`'s STARTTLS probe, keeping local + test dispatch deterministic.

## Deviations from Plan

### Sequencing improvement
**1. Program.cs DI split across Task 1 and Task 2 (so each commit builds)**
- The plan listed `Program.cs` in both Task 1 and Task 2. Task 1 registered `EmailOptions` + `IEmailSender` (the pieces its files need); Task 2 added `AddTransient<SendDocumentEmailJob>()`. This keeps each commit self-consistent (Task 1 builds 0-warnings without the not-yet-created job type). No functional difference — identical to how 04-03 sequenced its DI.

### Trivial build fixes (within-task, not behavioural)
- `MailKitEmailSender`: `AuthenticateAsync(user, password ?? "")` for CS8604 nullable-arg (password is optional in `EmailOptions`).
- Test: `TaxCategory` lives in `Numera.Platform.Money` (added the using); Testcontainers 4.13's parameterless `ContainerBuilder()` is `[Obsolete(error)]` → used the `ContainerBuilder("axllent/mailpit:latest")` image-parameter constructor.

**Total: 1 sequencing improvement + 2 trivial compile fixes. No architectural change; no scope creep; no schema/migration change.**

## Authentication Gates
None — Mailpit accepts anonymously; no account/key needed for local or test dispatch.

## Issues Encountered
- The machine's default `dotnet` shim resolves no compatible SDK; used the user-local SDK 10 (`C:\Users\Admin\AppData\Local\Microsoft\dotnet`) with `DOTNET_ROOT` + `DOTNET_MULTILEVEL_LOOKUP=0` per STATE.md.

## User Setup Required
- **Local dev:** `docker compose up -d mailpit` to capture outbound mail; inspect it at http://localhost:8025. No real mail is sent in dev/test.
- **Production:** override the `Email` section (Host/Port/UseSsl/FromAddress + Username/Password) to point at a real SMTP relay.

## Next Phase Readiness
- **04-05 (frontend + human-verify):** wire a "Send" button to `POST /api/documents/{id}/send` (optional recipient override + language) and surface the `document_email` send status (Queued/Sent/Failed + attempts + last error). The human-verify checkpoint can send a finalized invoice and confirm the message + PDF land in Mailpit.
- **Deploy note (carried from 04-03, RESEARCH.md Pitfall 3):** the render (invoked by the send job's render-if-absent) runs on the Api; a slim Linux image needs `libfontconfig1` + a font for SkiaSharp. Flagged, not blocking.

## Self-Check: PASSED

All 6 created source/test files exist on disk; all 3 task commits (6f8568c, 09e3772, e4ed53f) are present in git history; full integration suite 67/67 green; solution build 0 warnings.
