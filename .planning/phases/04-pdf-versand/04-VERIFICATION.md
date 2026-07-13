---
phase: 04-pdf-versand
verified: 2026-07-13T22:40:08Z
status: passed
score: 3/3 must-haves verified
---

# Phase 4: PDF & Versand Verification Report

**Phase Goal:** Nutzer kann Belege als professionelle PDFs im eigenen Layout erzeugen und direkt versenden - Rendering laeuft asynchron, damit die Finalisierung schnell bleibt.
**Verified:** 2026-07-13T22:40:08Z
**Status:** passed
**Re-verification:** No - initial verification

## Goal Achievement

### Observable Truths (Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer kann Belege als PDF mit eigenem Logo/Briefpapier erzeugen, in deutschem und englischem Layout | VERIFIED | InvoiceDocument (src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs) is a single resource-driven QuestPDF section-14 layout consuming PdfLabels.For("de"/"en"); renders logo (model.LogoBytes), issuer/recipient blocks, lines, totals, BG-23 breakdown, verbatim ExemptionReasonText Pflichttexte, bank block, culture-correct money/dates (de-DE / en-GB CultureInfo, never ambient). SnapshotReader.FromDocument builds the model ONLY from the frozen IssuerSnapshot/RecipientSnapshot jsonb plus persisted Lines/TaxBreakdown (case-insensitive parse, confirmed correct against the real camelCase freeze). GET /api/documents/{id}/pdf?lang=de-or-en (SalesDocumentEndpoints.cs) serves it; frontend DocumentDetailPage.tsx has a DE/EN Select plus download button shown only when NOT isDraft; CompanyProfileSettingsPage.tsx has a logo upload/preview card calling PUT /api/company-profile/logo (validated PNG/JPG max 1MB). 7/7 InvoiceDocumentTests (unit, DE+EN render to valid PDF-magic bytes, Pflichttext plus de-DE culture asserted) and 2 render-job integration tests (real Postgres, real section-14 PDF incl. a Kleinunternehmer section-19 Pflichttext render) pass, reran live, all green. Human-verify checkpoint in 04-05 was APPROVED after the orchestrator rendered and inspected real DE+EN PDFs through the production layout (section-14-complete, correct culture switch, verbatim German Pflichttext under English labels). |
| 2 | Nutzer kann einen Beleg direkt per E-Mail an den Kunden versenden (mit PDF-Anhang) | VERIFIED | MailKitEmailSender (src/Numera.Api/Services/MailKitEmailSender.cs) sends a multipart body with the PDF attached via MimeKit BodyBuilder; SendDocumentEmailJob renders-if-absent (DocumentPdfService.GetOrRender, never sends without an attachment), sends, then advances document_email to Sent and flips SalesDocument.SentAt / Status=Sent on first success (or records Failed plus LastError and rethrows for Hangfire AutomaticRetry(3)). POST /api/documents/{id}/send records a Queued document_email row then enqueues after commit. Frontend DocumentDetailPage.tsx has a send-by-email button (shown when NOT isDraft AND NOT isCancelled) with a send-status Badge driven by sentAt / Status===Sent. Reran DocumentEmailSendTests.Send_delivers_the_pdf_to_mailpit_and_flips_document_email_and_document_sentat live against a real Testcontainers Mailpit plus real Postgres: PASSED - captured exactly one Mailpit message with subject, recipient, and a RE-2026-00001.pdf (application/pdf) attachment; document_email flipped to Sent (SentAt set, AttemptCount=1); SalesDocument.SentAt / Status flipped; a second tenant context saw 0 rows (RLS). |
| 3 | PDF-Erzeugung laeuft ueber einen Worker-Tier und blockiert die Finalisierung nicht | VERIFIED | EnqueuePdfOnFinalize implementing IDomainEventHandler of InvoiceFinalized (src/Numera.Api/Events/EnqueuePdfOnFinalize.cs) ONLY calls jobs.Enqueue of RenderDocumentPdfJob and returns, no render call, no DB write. SalesDocumentEndpoints.cs finalize endpoint: FinalizeCoreAsync runs inside the transaction (freeze snapshot, breakdown, numbering, open item, status flip) and is unmodified by this phase; publisher.PublishAsync(new InvoiceFinalized(...)) fires strictly AFTER tx.CommitAsync() (confirmed by direct code read, lines 481-498). RenderDocumentPdfJob runs on the Api default-queue Hangfire server in its own DI scope with ICurrentTenant.SetTenant (RLS re-established), mirroring WelcomeEmailJob. The hard-gate test Finalize_hook_only_enqueues_the_render_job_and_never_renders_inline (a fake IBackgroundJobClient records exactly 1 Create call for RenderDocumentPdfJob.RunAsync, and asserts zero document_render rows exist after HandleAsync) - reran live: PASSED. |

**Score:** 3/3 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/modules/Numera.Modules.Sales/Rendering/DocumentRender.cs | RLS-scoped PDF blob entity | VERIFIED | ITenantEntity, UUIDv7 PK, PdfBytes/DocumentNumber/Language/ByteSize/RenderedAt |
| src/modules/Numera.Modules.Sales/Email/DocumentEmail.cs | RLS-scoped send ledger entity | VERIFIED | ITenantEntity, EmailStatus, AttemptCount, LastError, SentAt |
| src/platform/Numera.Platform.Db/Migrations/20260713130849_DocumentDelivery.cs | 2 tables plus hand-written RLS plus logo columns | VERIFIED | ENABLE/FORCE ROW LEVEL SECURITY plus CREATE POLICY tenant_isolation on both new tables; logo_bytes/logo_content_type added to company_profile |
| tests/Numera.IntegrationTests/DocumentDeliveryRlsTests.cs | Cross-tenant isolation proof | VERIFIED | 4 tests, reran live on real postgres:18 as numera_app, all pass |
| src/modules/Numera.Modules.Sales/Pdf/SnapshotReader.cs plus InvoiceDocument.cs plus PdfLabels.cs | Section-14 render unit from frozen snapshot, DE/EN | VERIFIED | Zero live master-data reads (only CompanyProfile.LogoBytes at the service layer); case-insensitive jsonb parse confirmed correct against real JsonSerializerDefaults.Web freeze |
| src/Numera.Api/Services/DocumentPdfService.cs | Shared render entrypoint (RenderAndStore/GetOrRender) | VERIFIED | Idempotent delete-then-add per (document, language); loads doc plus logo under RLS |
| src/Numera.Api/Jobs/RenderDocumentPdfJob.cs | Hangfire job, tenant re-establishment | VERIFIED | Fresh scope plus SetTenant, AutomaticRetry(3) |
| src/Numera.Api/Events/EnqueuePdfOnFinalize.cs | Enqueue-only finalize hook | VERIFIED | No inline render; registered in Program.cs DI |
| GET /api/documents/{id}/pdf | Render-if-absent download | VERIFIED | 404 unknown / 409 draft / 200 plus application/pdf |
| PUT/GET /api/company-profile/logo | Logo upload/serve | VERIFIED | Content-type plus size (max 1MB) validated; 409 if no profile |
| src/Numera.Api/Services/MailKitEmailSender.cs | MailKit SMTP send with PDF attachment | VERIFIED | BodyBuilder multipart plus attachment; StartTls/None per config |
| src/Numera.Api/Jobs/SendDocumentEmailJob.cs | Send job: render-if-absent, status, SentAt flip | VERIFIED | Failure path records Failed plus LastError, rethrows for retry |
| POST /api/documents/{id}/send | Enqueue send (Queued record) | VERIFIED | 409 Draft / 404 unknown / 422 no-recipient; enqueue-after-commit |
| docker-compose.yml (mailpit) | Local/test SMTP capture | VERIFIED | numera-mailpit container confirmed running (docker ps) |
| tests/Numera.IntegrationTests/DocumentEmailSendTests.cs | End-to-end Mailpit send proof | VERIFIED | Reran live against real Testcontainers Mailpit, passed, attachment plus status plus RLS isolation all asserted |
| web/src/features/documents/DocumentDetailPage.tsx | DE/EN download plus send plus status badge, finalized-only | VERIFIED | NOT isDraft gates download and send; NOT isCancelled additionally gates send; sendStatus badge driven by sentAt/Status |
| web/src/features/settings/CompanyProfileSettingsPage.tsx | Logo upload plus preview | VERIFIED | Client-side type/size validation mirrors server; cache-busted preview |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| SalesDocumentEndpoints finalize to InvoiceFinalized | EnqueuePdfOnFinalize to RenderDocumentPdfJob | IDomainEventHandler registered in Program.cs; publisher.PublishAsync fires strictly after tx.CommitAsync() | WIRED | Confirmed by direct source read (SalesDocumentEndpoints.cs:475-498) and by the enqueue-not-inline test |
| RenderDocumentPdfJob | document_render (db.Add) | DocumentPdfService.RenderAndStoreCore | WIRED | Idempotent replace confirmed by re-render test (still exactly 1 row) |
| DocumentPdfService | CompanyProfile.LogoBytes | Live read (presentation, not legal snapshot) | WIRED | RenderAndStoreCore selects LogoBytes/LogoContentType live each render |
| POST /{id}/send | SendDocumentEmailJob (Hangfire) | BackgroundJob.Enqueue after Queued row commits | WIRED | Confirmed in SalesDocumentEndpoints.cs (send handler) |
| SendDocumentEmailJob | DocumentPdfService.GetOrRender plus MailKitEmailSender | render-if-absent then SMTP send | WIRED | Confirmed live via Mailpit-backed integration test - one message, PDF attached, correct filename |
| DocumentDetailPage download button | GET /{id}/pdf | downloadDocumentPdf blob to object-URL anchor | WIRED | documents.ts fetch confirmed; button calls download.mutate(pdfLang) |
| DocumentDetailPage send button | POST /{id}/send | sendDocumentEmail mutation to status badge | WIRED | send.mutate() on click; badge reads sentAt/Status post-refresh |
| CompanyProfileSettingsPage logo field | PUT /company-profile/logo | uploadCompanyLogo(file) multipart | WIRED | Confirmed in companyProfile.ts plus LogoSection component |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|-----------------|
| DOCS-02 (Belege als PDF mit eigenem Logo/Briefpapier, DE/EN) | SATISFIED | None |
| DOCS-03 (Belege direkt per E-Mail an Kunden versenden) | SATISFIED | None |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs | 146-148 | Recipient block concatenates frozen Name plus LegalForm unconditionally (example: "Kunde AG" plus "AG" becomes "Kunde AG AG") when the frozen name already includes the legal form | Info (cosmetic) | Not a section-14 defect - the legal recipient identification is still correct and complete, just visually redundant in this one case. Already recorded as a known non-blocking follow-up in the 04-05 SUMMARY; does not block phase goal achievement per explicit scope note. |

No TODO/FIXME/placeholder/stub patterns found in any Phase-4 production file (DocumentRender.cs, DocumentEmail.cs, SnapshotReader.cs, InvoiceDocument.cs, PdfLabels.cs, DocumentPdfService.cs, RenderDocumentPdfJob.cs, EnqueuePdfOnFinalize.cs, MailKitEmailSender.cs, SendDocumentEmailJob.cs, SalesDocumentEndpoints.cs additions, CompanyProfileEndpoints.cs additions, DocumentDetailPage.tsx additions, CompanyProfileSettingsPage.tsx additions).

### Live Verification Performed

- dotnet build Numera.sln (user-local .NET 10 SDK) resulted in 0 warnings, 0 errors.
- dotnet test tests/Numera.Platform.Tests --filter InvoiceDocumentTests resulted in 7/7 passed.
- dotnet test tests/Numera.IntegrationTests --filter DocumentDeliveryRlsTests-or-DocumentRenderJobTests-or-DocumentEmailSendTests (real Testcontainers postgres:18 plus a real Testcontainers Mailpit) resulted in 8/8 passed.
- dotnet test Numera.sln (full suite) resulted in Numera.Platform.Tests 61/61, Numera.IntegrationTests 67/67, all green.
- cd web then npm run build resulted in tsc plus vite build green (a pre-existing over-500kB chunk-size advisory is unrelated to this phase).
- docker ps confirmed numera-mailpit and numera-postgres running and healthy, consistent with the compose service the 04-04 plan added.

### Human Verification Required

None outstanding. The blocking human-verify checkpoint in 04-05 (Task 3) was already completed and APPROVED by the user during phase execution: the orchestrator rendered real DE+EN PDFs through the production InvoiceDocument layout with a sample logo and the user confirmed section-14-completeness, the DE-EN culture/label switch, and the verbatim German Pflichttext under English labels; the email round-trip was proven by the (now re-verified, still-passing) Mailpit end-to-end test. No further human action is required to close Phase 4.

### Gaps Summary

None. All three phase success criteria (DOCS-02 PDF generation with own letterhead in DE/EN; DOCS-03 direct email send with PDF attachment; async non-blocking rendering via the worker/Hangfire tier) are verified against real production code, real Postgres RLS, and a real Mailpit SMTP capture, not merely against SUMMARY claims. The single known issue (recipient Name plus LegalForm cosmetic duplication) is explicitly non-blocking per the phase own scope and does not affect section-14 legal correctness, downloadability, or send functionality.

---
Verified: 2026-07-13T22:40:08Z
Verifier: Claude (gsd-verifier)
